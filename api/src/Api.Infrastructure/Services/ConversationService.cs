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
    /// Returns the most recent open conversation for (companyId, customerId, channel),
    /// or creates a new one. IgnoreQueryFilters is required because callers have no
    /// authenticated tenant context — they provide the companyId explicitly instead.
    /// </summary>
    public async Task<Conversation> GetOrCreateAsync(
        Guid companyId,
        string customerId,
        ChannelType channel,
        CancellationToken ct = default)
    {
        var existing = await _db.Conversations
            .IgnoreQueryFilters()
            .Where(c =>
                c.CompanyId == companyId &&
                c.CustomerId == customerId &&
                c.Channel == channel &&
                c.Status == ConversationStatus.Open)
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
}
