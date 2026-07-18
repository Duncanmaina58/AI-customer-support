using System.Text.Json;
using System.Text.Json.Nodes;
using Api.Application.Abstractions;
using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.AI;
using Api.Infrastructure.Channels;
using Api.Infrastructure.Security;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Services;

/// <summary>
/// Sprint 5 follow-up: the actual "customer emailed us, now what" pipeline —
/// thread lookup, save, RAG, AI reply, escalation, send — factored out of
/// EmailWebhookController so ImapPollingService (the free IMAP alternative to
/// Brevo's paid inbound webhook) can share it instead of re-implementing the
/// same ~10 steps. Both callers just need to get a raw email into
/// (fromEmail, fromName, subject, messageId, inReplyTo, bodyText) first —
/// Brevo hands that to us as JSON, IMAP hands it to us as a MimeMessage.
/// </summary>
public class EmailPipelineService
{
    private readonly ConversationService  _conversations;
    private readonly RagService           _rag;
    private readonly IAiProvider          _aiProvider;
    private readonly EscalationService    _escalation;
    private readonly TicketService        _tickets;
    private readonly TokenBudgetService   _tokenBudget;
    private readonly IBrevoEmailClient    _brevo;
    private readonly IImapSmtpEmailClient _imapSmtp;
    private readonly IChannelCredentialProtector _protector;
    private readonly ILogger<EmailPipelineService> _logger;

    public EmailPipelineService(
        ConversationService  conversations,
        RagService           rag,
        IAiProvider          aiProvider,
        EscalationService    escalation,
        TicketService        tickets,
        TokenBudgetService   tokenBudget,
        IBrevoEmailClient    brevo,
        IImapSmtpEmailClient imapSmtp,
        IChannelCredentialProtector protector,
        ILogger<EmailPipelineService> logger)
    {
        _conversations = conversations;
        _rag           = rag;
        _aiProvider    = aiProvider;
        _escalation    = escalation;
        _tickets       = tickets;
        _tokenBudget   = tokenBudget;
        _brevo         = brevo;
        _imapSmtp      = imapSmtp;
        _protector     = protector;
        _logger        = logger;
    }

    public async Task ProcessInboundEmailAsync(
        Guid companyId,
        ChannelConnection connection,
        string fromEmail,
        string? fromName,
        string subject,
        string messageId,
        string? inReplyTo,
        string bodyText,
        CancellationToken ct = default)
    {
        var mode = EmailChannelMetadata.ReadMode(connection.MetadataJson);

        // ---- Thread lookup or new conversation ----
        Conversation? conversation = null;
        if (!string.IsNullOrEmpty(inReplyTo))
            conversation = await _conversations.GetByEmailMessageIdAsync(companyId, inReplyTo, ct);

        if (conversation is null)
        {
            conversation = await _conversations.CreateEmailConversationAsync(
                companyId, fromEmail, fromName, messageId, subject, ct);
        }
        else
        {
            await _conversations.UpdateEmailMessageIdAsync(conversation.Id, messageId, ct);
        }

        // ---- Save inbound message ----
        await _conversations.AppendMessageAsync(
            conversation.Id, companyId, MessageRole.User, bodyText, ct: ct);

        var senderName  = EmailChannelMetadata.ReadSenderName(connection.MetadataJson) ?? "Support";
        var senderEmail = EmailChannelMetadata.ReadSenderEmail(connection.MetadataJson) ?? fromEmail;

        // ---- Sprint 7: 100% token budget cutoff ----
        // Pauses the AI but still creates a ticket, per the Phase 1 doc, so an
        // over-budget company's customers aren't simply left unanswered.
        if (await _tokenBudget.IsOverBudgetAsync(companyId, ct))
        {
            var budgetTicket = await _tickets.CreateAsync(
                companyId:        companyId,
                conversationId:   conversation.Id,
                subject:          subject.Length > 300 ? subject[..300] : subject,
                priority:         TicketPriority.Medium,
                escalationReason: "Monthly AI token budget exceeded",
                ct:               ct);

            var budgetReplyText =
                $"Your AI conversation limit has been reached. Upgrade your plan or wait until your next " +
                $"billing date. Our team has been notified (ticket #{budgetTicket.TicketNumber}) and will " +
                $"follow up with you shortly.\n\nBest regards,\n{senderName}";

            await SendReplyAsync(
                BuildReplyEmail(subject, budgetReplyText, senderName, senderEmail, fromEmail, fromName, messageId, inReplyTo),
                connection, mode, ct);
            return;
        }

        // ---- RAG retrieval (non-fatal) ----
        IReadOnlyList<string> chunks = [];
        try { chunks = await _rag.RetrieveAsync(companyId, bodyText, topK: 4, ct: ct); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAG retrieval failed | company={CompanyId}", companyId);
        }

        // ---- AI reply ----
        var historyLines = await _conversations.GetRecentHistoryLinesAsync(
            conversation.Id, limit: 10, ct: ct);

        var aiResult = await _aiProvider.GenerateReplyAsync(new AiReplyRequest(
            CompanyId:                companyId,
            ConversationId:           conversation.Id,
            CustomerMessage:          bodyText,
            RecentHistory:            historyLines,
            RetrievedKnowledgeChunks: chunks), ct);

        // ---- Escalation check ----
        var escalation = await _escalation.EvaluateAsync(
            companyId, conversation.Id, bodyText, aiResult.ConfidenceScore, ct);

        string replyText;
        if (escalation.ShouldEscalate)
        {
            var ticket = await _tickets.CreateAsync(
                companyId:        companyId,
                conversationId:   conversation.Id,
                subject:          subject.Length > 300 ? subject[..300] : subject,
                priority:         escalation.Priority,
                assignedTeam:     escalation.AssignedTeam,
                escalationReason: escalation.Reason,
                ct:               ct);

            replyText =
                $"Thank you for reaching out.\n\n" +
                $"I've created a support ticket (#{ticket.TicketNumber}) for your inquiry. " +
                $"A member of our team will follow up with you shortly.\n\n" +
                $"Your ticket reference: #{ticket.TicketNumber}\n\n" +
                $"Best regards,\n{senderName}";

            await _conversations.AppendMessageAsync(
                conversation.Id, companyId, MessageRole.System,
                $"[Escalated — {escalation.Reason}] AI draft: {aiResult.ReplyText}",
                ct: ct);
        }
        else
        {
            replyText = aiResult.ReplyText;
        }

        // ---- Save AI reply + track tokens ----
        await _conversations.AppendMessageAsync(
            conversation.Id, companyId, MessageRole.Ai, replyText,
            confidenceScore: aiResult.ConfidenceScore,
            modelUsed:       aiResult.ModelUsed,
            tokensUsed:      aiResult.TokensUsed,
            ct:              ct);

        await _conversations.IncrementTokenUsageAsync(companyId, aiResult.TokensUsed, ct);
        await _tokenBudget.CheckAndSendBudgetWarningIfNeededAsync(companyId, ct);

        // ---- Send reply, preserving email thread headers ----
        await SendReplyAsync(
            BuildReplyEmail(subject, replyText, senderName, senderEmail, fromEmail, fromName, messageId, inReplyTo),
            connection, mode, ct);

        _logger.LogInformation(
            "Email reply sent | company={CompanyId} conv={ConvId} to={Email} mode={Mode} escalated={Escalated}",
            companyId, conversation.Id, fromEmail, mode, escalation.ShouldEscalate);
    }

    private static BrevoOutboundEmail BuildReplyEmail(
        string subject, string replyText, string senderName, string senderEmail,
        string toEmail, string? toName, string messageId, string? inReplyTo)
    {
        var replySubject = subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
            ? subject
            : $"Re: {subject}";

        return new BrevoOutboundEmail(
            SenderName:   senderName,
            SenderEmail:  senderEmail,
            ToEmail:      toEmail,
            ToName:       toName,
            Subject:      replySubject,
            TextContent:  replyText,
            InReplyTo:    messageId,
            References:   inReplyTo is null ? messageId : $"{inReplyTo} {messageId}",
            ReplyToEmail: senderEmail);
    }

    private async Task SendReplyAsync(
        BrevoOutboundEmail outbound, ChannelConnection connection, string mode, CancellationToken ct)
    {
        if (mode == EmailChannelMetadata.ModeImap)
        {
            var credentials = EmailChannelMetadata.DecryptImapCredentials(connection, _protector);
            await _imapSmtp.SendAsync(outbound, credentials, ct);
        }
        else
        {
            await _brevo.SendAsync(outbound, ct);
        }
    }
}

/// <summary>
/// Sprint 5 follow-up: small helpers for reading/writing the Email channel's
/// MetadataJson ({ inboundMode, displayEmail, senderName, senderEmail }) and
/// decrypting the right credentials type for whichever mode is stored — kept in
/// one place so ChannelsController, EmailWebhookController, EmailPipelineService,
/// and ImapPollingService can't drift on the JSON shape.
/// </summary>
public static class EmailChannelMetadata
{
    public const string ModeWebhook = "Webhook";
    public const string ModeImap    = "Imap";

    public static string ReadMode(string metadataJson)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(metadataJson);
            if (doc.TryGetProperty("inboundMode", out var mode) && mode.GetString() == ModeImap)
            {
                return ModeImap;
            }
        }
        catch (JsonException) { /* fall through to default */ }

        return ModeWebhook;
    }

    public static string? ReadSenderEmail(string metadataJson) => ReadStringProperty(metadataJson, "senderEmail");
    public static string? ReadSenderName(string metadataJson)  => ReadStringProperty(metadataJson, "senderName");

    /// <summary>
    /// Sprint 5 follow-up fix: the timestamp Imap mode starts processing mail
    /// from — mail received before this is ignored regardless of \Seen status,
    /// so a mailbox's pre-existing unread backlog (very common — old
    /// newsletters, notifications, etc. sitting unread for months/years)
    /// doesn't flood in as "new" conversations the moment a company connects.
    /// Null for Webhook-mode connections (not applicable) and for legacy Imap
    /// connections made before this field existed — see
    /// ImapPollingService.PollAllCompaniesAsync for the one-time backfill that
    /// handles the latter.
    /// </summary>
    public static DateTime? ReadProcessingStartedAtUtc(string metadataJson)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(metadataJson);
            if (doc.TryGetProperty("processingStartedAtUtc", out var value)
                && value.TryGetDateTime(out var dt))
            {
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
        }
        catch (JsonException) { /* fall through */ }

        return null;
    }

    /// <summary>
    /// Rewrites MetadataJson with processingStartedAtUtc set, preserving every
    /// other field already present (inboundMode, displayEmail, senderEmail,
    /// senderName) — used for the legacy-connection backfill in
    /// ImapPollingService, so it doesn't need to know or reconstruct the rest
    /// of the shape.
    /// </summary>
    public static string WithProcessingStartedAtUtc(string metadataJson, DateTime utcNow)
    {
        var node = JsonNode.Parse(metadataJson)?.AsObject() ?? new JsonObject();
        node["processingStartedAtUtc"] = utcNow.ToString("O");
        return node.ToJsonString();
    }

    private static string? ReadStringProperty(string metadataJson, string property)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(metadataJson);
            return doc.TryGetProperty(property, out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static ImapChannelCredentials DecryptImapCredentials(
        ChannelConnection connection, IChannelCredentialProtector protector)
    {
        var json = protector.Decrypt(connection.CredentialsEncrypted);
        return JsonSerializer.Deserialize<ImapChannelCredentials>(json)
            ?? throw new InvalidOperationException("Stored IMAP credentials could not be deserialized.");
    }
}
