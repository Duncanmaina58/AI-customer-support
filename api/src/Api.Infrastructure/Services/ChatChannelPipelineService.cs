using Api.Application.Abstractions;
using Api.Domain.Enums;
using Api.Infrastructure.AI;
using Api.Infrastructure.Actions;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Services;

/// <summary>
/// Sprint 6: the "customer messaged us on Messenger/Telegram, now what" pipeline
/// — thread lookup, save, RAG, AI reply, escalation, ticket, reply. Identical in
/// shape to WhatsAppWebhookController.ProcessMessageAsync (the established
/// precedent for this exact flow), factored into a shared service specifically
/// so Messenger and Telegram don't each duplicate it. WhatsApp's own webhook is
/// deliberately left as-is rather than refactored to also use this — it already
/// works, and touching it risks regressing something that doesn't need to change.
///
/// Sprint 9: also the Action Engine's integration point for these two channels —
/// see the two new steps between "save inbound message" and "generate AI reply"
/// below, and ActionEngineService's own doc comment for the full design.
/// WhatsApp is intentionally NOT wired into the Action Engine yet, for the same
/// "don't touch what already works" reason noted above; see
/// docs/security-audit.md's Sprint 9 addendum.
///
/// Sending is handled by a caller-supplied delegate rather than a shared sender
/// interface: each channel's webhook controller already needs its own client and
/// its own decrypted credentials to verify/process the inbound payload, so it's
/// simplest for it to also own "how do I reply", with this service staying
/// completely channel-agnostic.
/// </summary>
public class ChatChannelPipelineService
{
    private readonly ConversationService _conversations;
    private readonly RagService          _rag;
    private readonly IAiProvider         _aiProvider;
    private readonly EscalationService   _escalation;
    private readonly TicketService       _tickets;
    private readonly TokenBudgetService  _tokenBudget;
    private readonly IActionRegistry     _actionRegistry;
    private readonly ActionEngineService _actionEngine;
    private readonly ILogger<ChatChannelPipelineService> _logger;

    public ChatChannelPipelineService(
        ConversationService conversations,
        RagService          rag,
        IAiProvider          aiProvider,
        EscalationService    escalation,
        TicketService        tickets,
        TokenBudgetService   tokenBudget,
        IActionRegistry      actionRegistry,
        ActionEngineService  actionEngine,
        ILogger<ChatChannelPipelineService> logger)
    {
        _conversations  = conversations;
        _rag            = rag;
        _aiProvider     = aiProvider;
        _escalation     = escalation;
        _tickets        = tickets;
        _tokenBudget    = tokenBudget;
        _actionRegistry = actionRegistry;
        _actionEngine   = actionEngine;
        _logger         = logger;
    }

    public async Task ProcessInboundAsync(
        Guid companyId,
        ChannelType channel,
        string customerId,
        string text,
        Func<string, CancellationToken, Task> sendReplyAsync,
        CancellationToken ct = default)
    {
        var conversation = await _conversations.GetOrCreateAsync(companyId, customerId, channel, ct: ct);

        // Same short-circuit as ChatHub: once escalated, a ticket already exists
        // and a human is expected to handle it — don't re-run the AI pipeline
        // (which would spam duplicate tickets on every follow-up message).
        if (conversation.Status == ConversationStatus.Escalated)
        {
            await _conversations.AppendMessageAsync(conversation.Id, companyId, MessageRole.User, text, ct: ct);
            await sendReplyAsync("Thanks — I've added that to your open ticket. An agent will follow up shortly.", ct);
            return;
        }

        await _conversations.AppendMessageAsync(conversation.Id, companyId, MessageRole.User, text, ct: ct);

        // Sprint 9 Action Engine: if this conversation has an outstanding OTP
        // challenge (from a previous RequiresVerification action request), this
        // message is the verification attempt, not a fresh question — intercept
        // it before RAG/AI/budget/escalation ever see it.
        var pendingVerificationOutcome = await _actionEngine.TryHandlePendingVerificationAsync(
            companyId, conversation.Id, customerId, text, ct);

        if (pendingVerificationOutcome is not null)
        {
            await _conversations.AppendMessageAsync(
                conversation.Id, companyId, MessageRole.Ai, pendingVerificationOutcome.ReplyText, ct: ct);
            await sendReplyAsync(pendingVerificationOutcome.ReplyText, ct);
            return;
        }

        // Sprint 7: 100% token budget cutoff — pause the AI, but still create
        // a ticket so the customer isn't left hanging (Phase 1 doc's exact
        // wording below).
        if (await _tokenBudget.IsOverBudgetAsync(companyId, ct))
        {
            var ticket = await _tickets.CreateAsync(
                companyId:        companyId,
                conversationId:   conversation.Id,
                subject:          text.Length > 120 ? text[..120] : text,
                priority:         TicketPriority.Medium,
                escalationReason: "Monthly AI token budget exceeded",
                ct:               ct);

            await sendReplyAsync(
                "Your AI conversation limit has been reached. Upgrade your plan or wait until your next " +
                $"billing date. Our team has been notified (ticket #{ticket.TicketNumber}) and will follow up.",
                ct);
            return;
        }

        var historyLines = await _conversations.GetRecentHistoryLinesAsync(conversation.Id, limit: 10, ct: ct);

        IReadOnlyList<string> chunks = [];
        try { chunks = await _rag.RetrieveAsync(companyId, text, topK: 4, ct: ct); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAG retrieval failed | company={CompanyId} channel={Channel}", companyId, channel);
        }

        var availableActions = await BuildAvailableActionsAsync(companyId, ct);

        var aiResult = await _aiProvider.GenerateReplyAsync(new AiReplyRequest(
            CompanyId:                companyId,
            ConversationId:           conversation.Id,
            CustomerMessage:          text,
            RecentHistory:            historyLines,
            RetrievedKnowledgeChunks: chunks,
            AvailableActions:         availableActions), ct);

        var escalation = await _escalation.EvaluateAsync(companyId, conversation.Id, text, aiResult.ConfidenceScore, ct);

        string replyText;
        var totalTokensUsed = aiResult.TokensUsed;

        // Sprint 9 Action Engine: takes priority over the escalation branch below
        // — an action intent means the customer wants something DONE, not
        // answered or escalated.
        if (aiResult.Intent is { Type: IntentType.Action, ActionType: not null })
        {
            var actionOutcome = await _actionEngine.HandleDetectedActionAsync(
                companyId, conversation.Id, customerId, text, aiResult.Intent, ct);
            replyText = actionOutcome.ReplyText;
            totalTokensUsed += actionOutcome.TokensUsed;
        }
        else if (escalation.ShouldEscalate)
        {
            var ticket = await _tickets.CreateAsync(
                companyId:        companyId,
                conversationId:   conversation.Id,
                subject:          text.Length > 120 ? text[..120] : text,
                priority:         escalation.Priority,
                assignedTeam:     escalation.AssignedTeam,
                escalationReason: escalation.Reason,
                ct:               ct);

            replyText =
                $"I've raised a support ticket (#{ticket.TicketNumber}) for you. " +
                $"Our team will follow up shortly. Your reference number is #{ticket.TicketNumber}.";

            await _conversations.AppendMessageAsync(
                conversation.Id, companyId, MessageRole.System,
                $"[Escalated — {escalation.Reason}] AI draft: {aiResult.ReplyText}",
                ct: ct);
        }
        else
        {
            replyText = aiResult.ReplyText;
        }

        await _conversations.AppendMessageAsync(
            conversation.Id, companyId, MessageRole.Ai, replyText,
            confidenceScore: aiResult.ConfidenceScore,
            modelUsed:       aiResult.ModelUsed,
            tokensUsed:      totalTokensUsed,
            ct:              ct);

        await _conversations.IncrementTokenUsageAsync(companyId, totalTokensUsed, ct);
        await _tokenBudget.CheckAndSendBudgetWarningIfNeededAsync(companyId, ct);

        await sendReplyAsync(replyText, ct);

        _logger.LogInformation(
            "{Channel} reply sent | company={CompanyId} conv={ConvId} escalated={Escalated}",
            channel, companyId, conversation.Id, escalation.ShouldEscalate);
    }

    /// <summary>Sprint 9: the system prompt's "things you can do" list, built fresh per turn — cheap (one indexed query) and always reflects the latest dashboard edits with zero caching invalidation to get wrong.</summary>
    private async Task<IReadOnlyList<AvailableAction>> BuildAvailableActionsAsync(Guid companyId, CancellationToken ct)
    {
        var definitions = await _actionRegistry.GetActiveActionsAsync(companyId, ct);
        return definitions
            .Select(d => new AvailableAction(d.ActionType, d.DisplayName, d.ParameterSchema))
            .ToList();
    }
}
