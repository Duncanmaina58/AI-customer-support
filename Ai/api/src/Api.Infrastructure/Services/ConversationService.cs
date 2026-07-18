using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Services;

/// <summary>
/// Low-level service that creates conversations and appends messages, explicitly
/// bypassing the global EF Core tenant query filter. All callers MUST pass an
/// explicit companyId — this is the correct pattern for webhook handlers and
/// SignalR hubs that operate without a JWT / HTTP context.
///
/// Used by: ChatHub (web chat widget), WhatsAppWebhookController, and any future
/// channel webhook that arrives without an authenticated tenant context.
/// </summary>
public class ConversationService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(AppDbContext db, ILogger<ConversationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Returns the most recent still-active conversation for (companyId, customerId,
    /// channel), or creates a new one. "Still active" means Open OR Escalated —
    /// Escalated is included so that once a ticket is raised, the customer keeps
    /// talking in the *same* conversation (same SignalR group, same message thread
    /// the agent is replying to) instead of silently forking into an orphaned new
    /// conversation with no ticket. Resolved conversations are excluded on purpose:
    /// once a ticket is done, the next message starts a clean new conversation.
    /// IgnoreQueryFilters is required because callers have no authenticated tenant
    /// context — they provide the companyId explicitly instead.
    /// </summary>
    public async Task<Conversation> GetOrCreateAsync(
        Guid companyId,
        string customerId,
        ChannelType channel,
        bool isSandbox = false,
        CancellationToken ct = default)
    {
        var existing = await _db.Conversations
            .IgnoreQueryFilters()
            .Where(c =>
                c.CompanyId == companyId &&
                c.CustomerId == customerId &&
                c.Channel == channel &&
                (c.Status == ConversationStatus.Open || c.Status == ConversationStatus.Escalated))
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
            return existing;

        var conversation = new Conversation
        {
            CompanyId = companyId,
            CustomerId = customerId,
            Channel = channel,
            Status = ConversationStatus.Open,
            // Sprint 6: set once at creation and never changed afterward — see
            // the property's own doc comment on Conversation.IsSandbox.
            IsSandbox = isSandbox,
        };

        _db.Conversations.Add(conversation);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "New conversation {ConvId} | company={CompanyId} customer={CustomerId} channel={Channel}",
            conversation.Id, companyId, customerId, channel);

        return conversation;
    }

    /// <summary>
    /// Appends a message to an existing conversation.
    /// Message.CompanyId is denormalised (see Phase 1 schema doc) so it must be
    /// supplied explicitly here — we cannot derive it from the entity graph at
    /// this point without an extra DB round-trip.
    /// </summary>
    public async Task<Message> AppendMessageAsync(
        Guid conversationId,
        Guid companyId,
        MessageRole role,
        string content,
        double? confidenceScore = null,
        string? modelUsed = null,
        int? tokensUsed = null,
        CancellationToken ct = default)
    {
        var message = new Message
        {
            ConversationId = conversationId,
            CompanyId = companyId,
            Role = role,
            Content = content,
            ConfidenceScore = confidenceScore,
            ModelUsed = modelUsed,
            TokensUsed = tokensUsed,
            SentAt = DateTime.UtcNow,
        };

        _db.Messages.Add(message);
        await _db.SaveChangesAsync(ct);
        return message;
    }

    /// <summary>
    /// Atomically increments Company.TokensUsedThisMonth by <paramref name="tokensUsed"/>.
    ///
    /// Uses EF Core 8 ExecuteUpdateAsync (single UPDATE statement) instead of
    /// load-modify-save to avoid optimistic-concurrency conflicts when multiple
    /// conversations for the same company process messages simultaneously.
    ///
    /// Company is not tenant-scoped so no IgnoreQueryFilters() needed here.
    /// </summary>
    public async Task IncrementTokenUsageAsync(Guid companyId, int tokensUsed, CancellationToken ct = default)
    {
        if (tokensUsed <= 0) return;

        await _db.Companies
            .Where(c => c.Id == companyId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.TokensUsedThisMonth, c => c.TokensUsedThisMonth + tokensUsed),
                ct);
    }

    /// <summary>
    /// For email threading: looks up an OPEN email conversation where
    /// <see cref="Conversation.EmailMessageId"/> matches the inbound email's
    /// In-Reply-To header. Returns null if no matching thread is found
    /// (i.e. this is the start of a new conversation).
    /// </summary>
    public async Task<Conversation?> GetByEmailMessageIdAsync(
        Guid   companyId,
        string emailMessageId,
        CancellationToken ct = default)
    {
        return await _db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.CompanyId    == companyId
                     && c.Channel      == ChannelType.Email
                     && c.EmailMessageId == emailMessageId
                     && c.Status       == ConversationStatus.Open)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Creates a new email conversation. Called when an inbound email has no
    /// In-Reply-To header, or when the referenced thread is no longer open.
    /// </summary>
    public async Task<Conversation> CreateEmailConversationAsync(
        Guid   companyId,
        string fromEmail,
        string? fromName,
        string emailMessageId,
        string subject,
        CancellationToken ct = default)
    {
        var conversation = new Conversation
        {
            CompanyId           = companyId,
            CustomerId          = fromEmail,
            CustomerDisplayName = fromName,
            Channel             = ChannelType.Email,
            Status              = ConversationStatus.Open,
            EmailMessageId      = emailMessageId,
            EmailSubject        = subject,
        };

        _db.Conversations.Add(conversation);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "New email conversation {ConvId} | company={CompanyId} from={Email}",
            conversation.Id, companyId, fromEmail);

        return conversation;
    }

    /// <summary>
    /// Updates the EmailMessageId stored on a conversation to the most recent
    /// inbound message's Message-ID. This is used as In-Reply-To on the next
    /// outbound reply so the customer's mail client keeps the thread intact.
    /// </summary>
    public async Task UpdateEmailMessageIdAsync(
    Guid   conversationId,
    string newMessageId,
    CancellationToken ct = default)
{
    var conv = await _db.Conversations
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

    if (conv is null) return;
    conv.EmailMessageId = newMessageId;
    await _db.SaveChangesAsync(ct);
}

    /// <summary>
    /// Marks a conversation as Resolved. Used by ConversationsController and TicketService.
    /// </summary>
   public async Task ResolveAsync(Guid conversationId, CancellationToken ct = default)
{
    var conv = await _db.Conversations
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

    if (conv is null) return;
    conv.Status = ConversationStatus.Resolved;
    await _db.SaveChangesAsync(ct);
}

    /// <summary>
    /// Loads the most recent messages for a conversation, bypassing the tenant filter.
    /// Callers must ensure conversationId already belongs to the correct company.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetRecentHistoryLinesAsync(
        Guid conversationId,
        int limit = 10,
        CancellationToken ct = default)
    {
        // Materialize first, then TakeLast in memory: TakeLast on an IQueryable is
        // not reliably translated to SQL across EF Core providers (Npgsql included),
        // so doing it server-side risks either a runtime translation error or, worse,
        // silently pulling the whole table. Per-conversation message counts are small
        // enough that this round-trip-then-slice approach is the safe, simple choice.
        var allLines = await _db.Messages
            .IgnoreQueryFilters()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.SentAt)
            .Select(m => m.Role == MessageRole.User
                ? $"Customer: {m.Content}"
                : $"AI: {m.Content}")
            .ToListAsync(ct);

        return allLines.TakeLast(limit).ToList();
    }

    /// <summary>
    /// Sprint 5 fix: finds the customer's most recent conversation for this
    /// company+channel, regardless of status, so a page refresh (or the widget
    /// re-mounting) can rehydrate chat history instead of silently starting a
    /// blank new conversation. Returns null if the customer has never messaged
    /// this company on this channel before.
    /// </summary>
    public async Task<Conversation?> GetMostRecentAsync(
        Guid companyId,
        string customerId,
        ChannelType channel,
        CancellationToken ct = default)
    {
        return await _db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.CompanyId == companyId && c.CustomerId == customerId && c.Channel == channel)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Customer-facing message history for a conversation: User/Ai/Agent only —
    /// System messages (internal escalation notes for agents) are never shown to
    /// the customer, in the widget or anywhere else.
    /// </summary>
    public async Task<IReadOnlyList<Message>> GetCustomerFacingMessagesAsync(
        Guid conversationId,
        CancellationToken ct = default)
    {
        return await _db.Messages
            .IgnoreQueryFilters()
            .Where(m => m.ConversationId == conversationId && m.Role != MessageRole.System)
            .OrderBy(m => m.SentAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Sprint 7: records a customer's 1-5 star rating for a conversation.
    /// One rating per conversation — a resubmission is silently ignored rather
    /// than overwriting (returns false), matching how most chat products treat
    /// a rating as a one-time close-out action. Called from an unauthenticated
    /// context (the customer isn't a logged-in agent), so — like every other
    /// customer-facing method on this service — it takes an explicit
    /// conversationId rather than relying on tenant context, and needs no
    /// companyId at all since a conversation's id alone is enough to find and
    /// rate it (no cross-tenant ambiguity: ids are globally unique GUIDs).
    /// </summary>
 public async Task<bool> SubmitCsatRatingAsync(Guid conversationId, int score, CancellationToken ct = default)
{
    if (score is < 1 or > 5)
        throw new ArgumentOutOfRangeException(nameof(score), "CSAT score must be between 1 and 5.");

    var conv = await _db.Conversations
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(c => c.Id == conversationId && c.CsatSubmittedAt == null, ct);

    if (conv is null) return false;

    conv.CsatScore       = score;
    conv.CsatSubmittedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
    return true;
}
}
