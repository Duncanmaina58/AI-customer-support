using Api.Application.Abstractions;
using Api.Domain.Enums;
using Api.Infrastructure.AI;
using Api.Infrastructure.Actions;
using Api.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Api.Hubs;

/// <summary>
/// Sprint 5 update: after AI reply generation, EscalationService evaluates the
/// configured rules. If escalation is triggered, TicketService creates a ticket,
/// the conversation is marked Escalated, and the customer receives a "ticket
/// created" message instead of the AI's uncertain reply.
///
/// Sprint 9 update: Action Engine integration. Full pipeline per SendMessage:
///   1. Resolve company by public key
///   2. GetOrCreate conversation
///   3. Save customer message
///   3a. Pending OTP verification check ← Sprint 9 (non-sandbox only)
///   4. RAG retrieval (non-fatal)
///   5. Generate AI reply via GroqChatProvider (now also returns DetectedIntent)
///   6. Action intent? → ActionEngineService ← Sprint 9 (takes priority below)
///   6b. Else: EscalationService.EvaluateAsync ← Sprint 5
///   7a. If escalated → TicketService.CreateAsync → stream "ticket created" msg
///   7b. If not → stream AI reply tokens
///   8. Save reply + track tokens (Sprint 9: including any action-compose tokens)
///
/// Sprint 9 actions never actually execute in Sandbox mode — same philosophy as
/// escalation-in-sandbox above: testing the AI shouldn't risk firing a real
/// webhook against a company's production backend. See the IsSandbox branches below.
/// </summary>
[AllowAnonymous]
public class ChatHub : Hub
{
    private readonly IAppDbContext       _db;
    private readonly ConversationService _conversations;
    private readonly RagService          _rag;
    private readonly IAiProvider         _aiProvider;
    private readonly EscalationService   _escalation;
    private readonly TicketService       _tickets;
    private readonly TokenBudgetService  _tokenBudget;
    private readonly IActionRegistry     _actionRegistry;
    private readonly ActionEngineService _actionEngine;
    private readonly ILogger<ChatHub>    _logger;

    public ChatHub(
        IAppDbContext       db,
        ConversationService conversations,
        RagService          rag,
        IAiProvider         aiProvider,
        EscalationService   escalation,
        TicketService       tickets,
        TokenBudgetService  tokenBudget,
        IActionRegistry     actionRegistry,
        ActionEngineService actionEngine,
        ILogger<ChatHub>    logger)
    {
        _db             = db;
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

    /// <summary>
    /// Sprint 6: resolves a company by either its production PublicApiKey (the
    /// real widget) or its SandboxToken (the private test chat / dashboard test
    /// panel) — whichever matches. The caller is what determines "sandbox or
    /// not"; there's nothing sandbox-specific about the connection itself.
    /// </summary>
    private async Task<(Api.Domain.Entities.Company? Company, bool IsSandbox)> ResolveCompanyAsync(
        string key, CancellationToken ct)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.PublicApiKey == key, ct);
        if (company is not null) return (company, false);

        company = await _db.Companies.FirstOrDefaultAsync(c => c.SandboxToken == key, ct);
        return (company, company is not null);
    }

    public async Task JoinCompanyGroup(string companyPublicKey)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(companyPublicKey));
    }

    /// <summary>
    /// Sprint 5 fix: called once by the widget right after connecting, so that
    /// (a) a page refresh rehydrates prior chat history instead of showing a blank
    /// slate, and (b) this connection is subscribed to the conversation's SignalR
    /// group immediately — the group an agent's dashboard reply is broadcast to
    /// (see TicketsController.Reply) — rather than only joining it lazily on the
    /// customer's *next* SendMessage call, by which point an agent reply sent in
    /// between could otherwise have nowhere to land.
    /// Returns null if this customer has never messaged this company before.
    /// </summary>
    public async Task<object?> GetHistory(string key, string sessionId)
    {
        var ct = Context.ConnectionAborted;

        var (company, isSandbox) = await ResolveCompanyAsync(key, ct);
        if (company is null) return null;

        var conversation = await _conversations.GetMostRecentAsync(
            company.Id, sessionId, ChannelType.WebChat, ct);
        if (conversation is null) return null;

        await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroupName(conversation.Id), ct);

        var messages = await _conversations.GetCustomerFacingMessagesAsync(conversation.Id, ct);

        return new
        {
            conversationId = conversation.Id.ToString(),
            escalated      = conversation.Status == ConversationStatus.Escalated,
            isSandbox,
            csatScore      = conversation.CsatScore,
            messages       = messages.Select(m => new
            {
                role = m.Role.ToString(),
                text = m.Content,
            }),
        };
    }

    /// <summary>
    /// Sprint 7: customer submits a 1-5 star rating for their conversation.
    /// Called from the widget's rating control (see ChatPanel) — no auth, since
    /// the customer isn't a logged-in agent; the conversationId alone (a GUID
    /// they can only have learned from GetHistory/ReplyComplete's own payload
    /// for their own conversation) is enough to identify what to rate.
    /// Returns false if this conversation was already rated (first rating wins).
    /// </summary>
    public async Task<bool> SubmitCsatRating(string conversationId, int score)
    {
        var ct = Context.ConnectionAborted;

        if (!Guid.TryParse(conversationId, out var id)) return false;

        try
        {
            return await _conversations.SubmitCsatRatingAsync(id, score, ct);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public async Task SendMessage(string key, string sessionId, string text)
    {
        var ct = Context.ConnectionAborted;

        try
        {
            // 1. Company lookup — production key or sandbox token
            var (company, isSandbox) = await ResolveCompanyAsync(key, ct);

            if (company is null)
            {
                await Clients.Caller.SendAsync("Error", "Invalid company key.", ct);
                return;
            }

            // 2. Conversation
            var conversation = await _conversations.GetOrCreateAsync(
                company.Id, sessionId, ChannelType.WebChat, isSandbox: isSandbox, ct: ct);

            // Subscribe this connection to the conversation's group — idempotent,
            // and necessary even if GetHistory already did it: a reconnect gets a
            // new Context.ConnectionId, and a brand-new conversation didn't exist
            // for GetHistory to find yet.
            await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroupName(conversation.Id), ct);

            // 3. Save customer message
            await _conversations.AppendMessageAsync(
                conversation.Id, company.Id, MessageRole.User, text, ct: ct);

            // Sprint 5 fix: once a conversation is escalated, a ticket already
            // exists and a human is expected to reply — don't run the AI/RAG/
            // escalation pipeline again (which would spam duplicate tickets every
            // time the customer sends a follow-up while waiting). Just log the
            // message against the same thread the agent is looking at and
            // acknowledge receipt.
            if (conversation.Status == ConversationStatus.Escalated)
            {
                await Clients.Caller.SendAsync("ReplyComplete", new
                {
                    conversationId = conversation.Id.ToString(),
                    fullText       = "Thanks — I've added that to your open ticket. An agent will follow up shortly.",
                    isSandbox      = conversation.IsSandbox,
                }, ct);
                return;
            }

            // 3a. Sprint 9 Action Engine: pending OTP verification check — if this
            // conversation has an outstanding challenge from a previous
            // RequiresVerification action request, this message is the
            // verification attempt, not a fresh question. Skipped for sandbox —
            // actions never actually execute there (see step 6 below), so no
            // sandbox conversation should ever have a real pending OTP to begin with.
            if (!conversation.IsSandbox)
            {
                var pendingVerificationOutcome = await _actionEngine.TryHandlePendingVerificationAsync(
                    company.Id, conversation.Id, sessionId, text, ct);

                if (pendingVerificationOutcome is not null)
                {
                    await _conversations.AppendMessageAsync(
                        conversation.Id, company.Id, MessageRole.Ai, pendingVerificationOutcome.ReplyText, ct: ct);

                    await Clients.Caller.SendAsync("ReplyComplete", new
                    {
                        conversationId = conversation.Id.ToString(),
                        fullText       = pendingVerificationOutcome.ReplyText,
                        isSandbox      = false,
                    }, ct);
                    return;
                }
            }

            // Sprint 7: 100% token budget cutoff — pause the AI, but still
            // create a ticket, so the customer isn't left hanging. Sandbox
            // conversations are exempt: a company maxed out on their real
            // plan should still be able to test their AI in sandbox (e.g. to
            // decide whether to upgrade), since sandbox never counted against
            // that budget in the first place.
            if (!conversation.IsSandbox && await _tokenBudget.IsOverBudgetAsync(company.Id, ct))
            {
                var budgetTicket = await _tickets.CreateAsync(
                    companyId:        company.Id,
                    conversationId:   conversation.Id,
                    subject:          text.Length > 120 ? text[..120] : text,
                    priority:         TicketPriority.Medium,
                    escalationReason: "Monthly AI token budget exceeded",
                    ct:               ct);

                await Clients.Caller.SendAsync("ReplyComplete", new
                {
                    conversationId = conversation.Id.ToString(),
                    fullText       =
                        "Your AI conversation limit has been reached. Upgrade your plan or wait until your next " +
                        $"billing date. Our team has been notified (ticket #{budgetTicket.TicketNumber}) and will follow up.",
                    isSandbox      = false,
                }, ct);
                return;
            }

            await Clients.Caller.SendAsync("TypingStart", ct);

            // 4. History + RAG
            var historyLines = await _conversations.GetRecentHistoryLinesAsync(
                conversation.Id, limit: 10, ct: ct);

            IReadOnlyList<string> knowledgeChunks = [];
            try
            {
                knowledgeChunks = await _rag.RetrieveAsync(company.Id, text, topK: 4, ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "RAG retrieval failed for company {CompanyId}; proceeding without context",
                    company.Id);
            }

            // Sprint 9: "things you can do" list for the system prompt.
            var actionDefinitions = await _actionRegistry.GetActiveActionsAsync(company.Id, ct);
            var availableActions  = actionDefinitions
                .Select(d => new AvailableAction(d.ActionType, d.DisplayName, d.ParameterSchema))
                .ToList();

            // 5. Generate AI reply
            var aiResult = await _aiProvider.GenerateReplyAsync(new AiReplyRequest(
                CompanyId:                company.Id,
                ConversationId:           conversation.Id,
                CustomerMessage:          text,
                RecentHistory:            historyLines,
                RetrievedKnowledgeChunks: knowledgeChunks,
                AvailableActions:         availableActions), ct);

            // 6. Escalation check (Sprint 5) — still evaluated even for action
            // intents (unchanged from before); the new branch just below takes
            // priority over it when the model detected an action.
            var escalation = await _escalation.EvaluateAsync(
                company.Id, conversation.Id, text, aiResult.ConfidenceScore, ct);

            string replyToStream;
            var totalTokensUsed = aiResult.TokensUsed;

            if (aiResult.Intent is { Type: IntentType.Action, ActionType: not null })
            {
                // Sprint 9 Action Engine — takes priority over escalation below.
                if (conversation.IsSandbox)
                {
                    // Actions never actually execute in sandbox — same reasoning as
                    // escalation-in-sandbox above: testing shouldn't risk firing a
                    // real webhook against a company's production backend.
                    replyToStream =
                        $"🧪 Sandbox: this would trigger the '{aiResult.Intent.ActionType}' action in production. " +
                        "No real webhook was called.";
                }
                else
                {
                    var actionOutcome = await _actionEngine.HandleDetectedActionAsync(
                        company.Id, conversation.Id, sessionId, text, aiResult.Intent, ct);
                    replyToStream = actionOutcome.ReplyText;
                    totalTokensUsed += actionOutcome.TokensUsed;
                }
            }
            else if (escalation.ShouldEscalate)
            {
                if (conversation.IsSandbox)
                {
                    // Sprint 6: sandbox mode never creates real tickets — per the
                    // Phase 1 doc, sandbox conversations "do NOT trigger ticket
                    // creation". Show what *would* happen in production instead,
                    // so testing is still informative.
                    replyToStream =
                        $"🧪 Sandbox: this message would escalate to a support ticket in production " +
                        $"(reason: {escalation.Reason}). No ticket was actually created.";
                }
                else
                {
                    // 7a. Create ticket and send escalation message
                    var ticket = await _tickets.CreateAsync(
                        companyId:        company.Id,
                        conversationId:   conversation.Id,
                        subject:          text.Length > 120 ? text[..120] : text,
                        priority:         escalation.Priority,
                        assignedTeam:     escalation.AssignedTeam,
                        escalationReason: escalation.Reason,
                        ct:               ct);

                    replyToStream =
                        $"I've raised a support ticket (#{ticket.TicketNumber}) for you. " +
                        $"Our team will follow up shortly. " +
                        $"Your reference number is #{ticket.TicketNumber}.";
                }

                // Save the AI's original uncertain reply as a system note for agents.
                await _conversations.AppendMessageAsync(
                    conversation.Id, company.Id, MessageRole.System,
                    $"[Escalated — {escalation.Reason}] AI draft: {aiResult.ReplyText}",
                    ct: ct);
            }
            else
            {
                // 7b. No escalation — use the AI reply directly
                replyToStream = aiResult.ReplyText;
            }

            // 8. Stream reply tokens
            var words = replyToStream.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var sb    = new System.Text.StringBuilder();

            foreach (var word in words)
            {
                ct.ThrowIfCancellationRequested();
                var token = word + " ";
                sb.Append(token);
                await Clients.Caller.SendAsync("ReceiveToken", token, ct);
                await Task.Delay(40, ct);
            }

            var fullReply = sb.ToString().TrimEnd();

            await Clients.Caller.SendAsync("ReplyComplete", new
            {
                conversationId = conversation.Id.ToString(),
                fullText       = fullReply,
                isSandbox      = conversation.IsSandbox,
            }, ct);

            // 9. Persist reply + token usage (Sprint 9: totalTokensUsed also
            // includes the Action Engine's confirmation-compose call, if any).
            await _conversations.AppendMessageAsync(
                conversation.Id, company.Id, MessageRole.Ai, fullReply,
                confidenceScore: aiResult.ConfidenceScore,
                modelUsed:       aiResult.ModelUsed,
                tokensUsed:      totalTokensUsed,
                ct:              ct);

            if (!conversation.IsSandbox)
            {
                // Sprint 6: sandbox conversations "do NOT count against their
                // conversation limit" per the Phase 1 doc — the AI call itself
                // still costs real tokens against Groq, but this company's
                // billed/budgeted usage is never charged for it.
                await _conversations.IncrementTokenUsageAsync(company.Id, totalTokensUsed, ct);
                await _tokenBudget.CheckAndSendBudgetWarningIfNeededAsync(company.Id, ct);
            }

            _logger.LogInformation(
                "Web chat reply sent | company={CompanyId} conv={ConvId} sandbox={IsSandbox} escalated={Escalated} tokens={Tokens}",
                company.Id, conversation.Id, conversation.IsSandbox, escalation.ShouldEscalate, totalTokensUsed);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Client disconnected mid-stream | sessionId={SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ChatHub.SendMessage error | sessionId={SessionId}", sessionId);
            try { await Clients.Caller.SendAsync("Error", "Something went wrong. Please try again."); }
            catch { /* client gone */ }
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }

    private static string GroupName(string key) => $"company:{key}";

    /// <summary>
    /// Public so TicketsController.Reply can broadcast an agent's reply to the
    /// exact widget connection(s) subscribed to this conversation, using the same
    /// naming scheme this hub uses when a customer connects — see GetHistory and
    /// SendMessage above.
    /// </summary>
    public static string ConversationGroupName(Guid conversationId) => $"conversation:{conversationId}";
}
