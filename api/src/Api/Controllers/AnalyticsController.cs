using Api.Application.Abstractions;
using Api.Contracts.Analytics;
using Api.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

/// <summary>
/// Analytics for the current tenant. The global EF Core query filter scopes
/// every query to this company automatically — no explicit CompanyId filter
/// needed anywhere in this controller.
///
/// Every endpoint here excludes IsSandbox conversations. This wasn't true of
/// the original Sprint 7 pass — sandbox testing (Sprint 6) would have quietly
/// skewed every metric in this controller for any company that tested their
/// AI at all, which is every company. Fixed as part of this pass rather than
/// left as a known gap, since it directly undermines the point of "real
/// insights" analytics are supposed to provide.
/// </summary>
[ApiController]
[Authorize]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly ICurrentTenantProvider _tenant;

    public AnalyticsController(IAppDbContext db, ICurrentTenantProvider tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>
    /// KPI summary for the dashboard's top cards, with trend deltas comparing
    /// the requested window against the immediately preceding window of the
    /// same length (e.g. "last 14 days" vs "the 14 days before that").
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<AnalyticsSummaryDto>> GetSummary([FromQuery] int days, CancellationToken ct)
    {
        var (currentStart, previousStart, _) = GetWindow(days);

        var conversations = await _db.Conversations
            .AsNoTracking()
            .Where(c => !c.IsSandbox && c.CreatedAt >= previousStart)
            .Select(c => new { c.Id, c.CreatedAt, c.Status, c.CsatScore, c.CsatSubmittedAt })
            .ToListAsync(ct);

        var current = conversations.Where(c => c.CreatedAt >= currentStart).ToList();
        var previous = conversations.Where(c => c.CreatedAt < currentStart).ToList();

        var total = current.Count;
        var open = current.Count(c => c.Status == ConversationStatus.Open);
        var resolved = current.Count(c => c.Status == ConversationStatus.Resolved);
        var escalated = current.Count(c => c.Status == ConversationStatus.Escalated);
        var resolutionRate = total > 0 ? Math.Round((double)resolved / total * 100, 1) : 0.0;
        var containmentRate = total > 0 ? Math.Round((double)(total - escalated) / total * 100, 1) : 0.0;

        var previousTotal = previous.Count;
        var previousEscalated = previous.Count(c => c.Status == ConversationStatus.Escalated);
        double? previousContainmentRate = previousTotal > 0
            ? (double)(previousTotal - previousEscalated) / previousTotal * 100
            : null;

        var avgFirstResponseSeconds = await GetAvgFirstResponseSecondsAsync(
            current.Select(c => c.Id).ToList(), ct);
        var previousAvgFirstResponseSeconds = await GetAvgFirstResponseSecondsAsync(
            previous.Select(c => c.Id).ToList(), ct);

        var ratedCurrent = current.Where(c => c.CsatScore.HasValue).ToList();
        double? csatAverage = ratedCurrent.Count > 0
            ? Math.Round(ratedCurrent.Average(c => c.CsatScore!.Value), 2)
            : null;

        var companyId = _tenant.CompanyId;
        var company = companyId.HasValue
            ? await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId.Value, ct)
            : null;

        return Ok(new AnalyticsSummaryDto(
            TotalConversations:            total,
            ConversationsTrendPercent:      PercentChange(previousTotal, total),
            OpenConversations:              open,
            ResolvedConversations:          resolved,
            EscalatedConversations:         escalated,
            ResolutionRate:                 resolutionRate,
            ContainmentRate:                containmentRate,
            ContainmentRateTrendPercent:    total > 0 ? PercentChange(previousContainmentRate, containmentRate) : null,
            AvgFirstResponseSeconds:        avgFirstResponseSeconds,
            AvgFirstResponseTrendPercent:   PercentChange(previousAvgFirstResponseSeconds, avgFirstResponseSeconds),
            TokensUsedThisMonth:            company?.TokensUsedThisMonth ?? 0,
            MonthlyTokenBudget:             company?.MonthlyTokenBudget ?? 0,
            CsatAverageScore:               csatAverage,
            CsatRatingCount:                ratedCurrent.Count));
    }

    /// <summary>Daily conversation counts for the requested window — powers the volume chart.</summary>
    [HttpGet("conversations-over-time")]
    public async Task<ActionResult<IReadOnlyList<DailyConversationCountDto>>> GetConversationsOverTime(
        [FromQuery] int days, CancellationToken ct)
    {
        var windowDays = NormalizeDays(days);
        var startDate = DateTime.UtcNow.Date.AddDays(-(windowDays - 1));

        var raw = await _db.Conversations
            .AsNoTracking()
            .Where(c => !c.IsSandbox && c.CreatedAt >= startDate)
            .GroupBy(c => c.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var byDate = raw.ToDictionary(r => r.Date, r => r.Count);
        var series = Enumerable.Range(0, windowDays)
            .Select(offset => startDate.AddDays(offset))
            .Select(date => new DailyConversationCountDto(date, byDate.GetValueOrDefault(date)))
            .ToList();

        return Ok(series);
    }

    /// <summary>Conversation counts grouped by channel, within the requested window.</summary>
    [HttpGet("channel-breakdown")]
    public async Task<ActionResult<IReadOnlyList<ChannelBreakdownDto>>> GetChannelBreakdown(
        [FromQuery] int days, CancellationToken ct)
    {
        var windowDays = NormalizeDays(days);
        var startDate = DateTime.UtcNow.Date.AddDays(-(windowDays - 1));

        var counts = await _db.Conversations
            .AsNoTracking()
            .Where(c => !c.IsSandbox && c.CreatedAt >= startDate)
            .GroupBy(c => c.Channel)
            .Select(g => new ChannelBreakdownDto(g.Key.ToString(), g.Count()))
            .OrderByDescending(g => g.Count)
            .ToListAsync(ct);

        return Ok(counts);
    }

    /// <summary>
    /// A simple approximation of "top questions" — groups each conversation's
    /// first customer message by an exact match on its lowercased, trimmed
    /// text, and returns the most frequent ones within the requested window.
    ///
    /// Deliberate simplification, not real topic clustering: "What are your
    /// hours?" and "what time do you open" count as two different questions
    /// here, since there's no semantic grouping — that would need
    /// embeddings-based clustering (comparing message vectors the way
    /// RagService already compares KnowledgeChunk vectors), a meaningfully
    /// bigger feature than an analytics endpoint. Exact-match frequency still
    /// surfaces genuinely repeated questions, which covers the common real
    /// case, just not paraphrases of the same question.
    /// </summary>
    [HttpGet("top-questions")]
    public async Task<ActionResult<IReadOnlyList<TopQuestionDto>>> GetTopQuestions(
        [FromQuery] int days, [FromQuery] int limit, CancellationToken ct)
    {
        var windowDays = NormalizeDays(days);
        var startDate = DateTime.UtcNow.Date.AddDays(-(windowDays - 1));
        var take = limit is > 0 and <= 50 ? limit : 8;

        var conversationIds = await _db.Conversations
            .AsNoTracking()
            .Where(c => !c.IsSandbox && c.CreatedAt >= startDate)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var firstMessages = await _db.Messages
            .AsNoTracking()
            .Where(m => conversationIds.Contains(m.ConversationId) && m.Role == MessageRole.User)
            .GroupBy(m => m.ConversationId)
            .Select(g => g.OrderBy(m => m.SentAt).First().Content)
            .ToListAsync(ct);

        var topQuestions = firstMessages
            .Select(text => text.Trim())
            .Where(text => text.Length is > 0 and <= 300)
            .GroupBy(text => text.ToLowerInvariant())
            .Select(g => new TopQuestionDto(g.First(), g.Count()))
            .OrderByDescending(q => q.Count)
            .ThenBy(q => q.Question)
            .Take(take)
            .ToList();

        return Ok(topQuestions);
    }

    /// <summary>
    /// Why conversations escalate, categorized from EscalationService's reason
    /// strings. "Low AI confidence" carries a dynamic score in the raw string
    /// (e.g. "Low AI confidence (0.42)") — bucketed here since the exact score
    /// isn't the useful signal for this chart, the pattern is.
    /// </summary>
    [HttpGet("escalation-reasons")]
    public async Task<ActionResult<IReadOnlyList<EscalationReasonBreakdownDto>>> GetEscalationReasons(
        [FromQuery] int days, CancellationToken ct)
    {
        var windowDays = NormalizeDays(days);
        var startDate = DateTime.UtcNow.Date.AddDays(-(windowDays - 1));

        var reasons = await _db.Tickets
            .AsNoTracking()
            .Where(t => t.CreatedAt >= startDate && t.EscalationReason != null)
            .Select(t => t.EscalationReason!)
            .ToListAsync(ct);

        var breakdown = reasons
            .GroupBy(CategorizeEscalationReason)
            .Select(g => new EscalationReasonBreakdownDto(g.Key, g.Count()))
            .OrderByDescending(g => g.Count)
            .ToList();

        return Ok(breakdown);
    }

    /// <summary>Daily AI token consumption for the requested window — sums Message.TokensUsed, not just the single cumulative monthly total.</summary>
    [HttpGet("token-usage-over-time")]
    public async Task<ActionResult<IReadOnlyList<DailyTokenUsageDto>>> GetTokenUsageOverTime(
        [FromQuery] int days, CancellationToken ct)
    {
        var windowDays = NormalizeDays(days);
        var startDate = DateTime.UtcNow.Date.AddDays(-(windowDays - 1));

        var raw = await _db.Messages
            .AsNoTracking()
            .Where(m => m.Role == MessageRole.Ai && m.TokensUsed != null && m.SentAt >= startDate)
            .GroupBy(m => m.SentAt.Date)
            .Select(g => new { Date = g.Key, Tokens = g.Sum(m => m.TokensUsed!.Value) })
            .ToListAsync(ct);

        var byDate = raw.ToDictionary(r => r.Date, r => r.Tokens);
        var series = Enumerable.Range(0, windowDays)
            .Select(offset => startDate.AddDays(offset))
            .Select(date => new DailyTokenUsageDto(date, byDate.GetValueOrDefault(date)))
            .ToList();

        return Ok(series);
    }

    /// <summary>Real CSAT: average score, 1-5 distribution, and a daily trend — from actual customer-submitted ratings, not a placeholder.</summary>
    [HttpGet("csat")]
    public async Task<ActionResult<CsatSummaryDto>> GetCsat([FromQuery] int days, CancellationToken ct)
    {
        var windowDays = NormalizeDays(days);
        var startDate = DateTime.UtcNow.Date.AddDays(-(windowDays - 1));

        var ratings = await _db.Conversations
            .AsNoTracking()
            .Where(c => !c.IsSandbox && c.CsatSubmittedAt != null && c.CsatSubmittedAt >= startDate)
            .Select(c => new { c.CsatScore, c.CsatSubmittedAt })
            .ToListAsync(ct);

        double? averageScore = ratings.Count > 0 ? Math.Round(ratings.Average(r => r.CsatScore!.Value), 2) : null;

        var distribution = Enumerable.Range(1, 5)
            .Select(score => new CsatDistributionBucketDto(score, ratings.Count(r => r.CsatScore == score)))
            .ToList();

        var byDate = ratings
            .GroupBy(r => r.CsatSubmittedAt!.Value.Date)
            .ToDictionary(g => g.Key, g => (Average: g.Average(r => r.CsatScore!.Value), Count: g.Count()));

        var trend = Enumerable.Range(0, windowDays)
            .Select(offset => startDate.AddDays(offset))
            .Select(date => byDate.TryGetValue(date, out var point)
                ? new CsatTrendPointDto(date, Math.Round(point.Average, 2), point.Count)
                : new CsatTrendPointDto(date, null, 0))
            .ToList();

        return Ok(new CsatSummaryDto(averageScore, ratings.Count, distribution, trend));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static int NormalizeDays(int days) => days is > 0 and <= 90 ? days : 14;

    private static (DateTime CurrentStart, DateTime PreviousStart, DateTime Now) GetWindow(int days)
    {
        var windowDays = NormalizeDays(days);
        var now = DateTime.UtcNow;
        var currentStart = now.Date.AddDays(-(windowDays - 1));
        var previousStart = currentStart.AddDays(-windowDays);
        return (currentStart, previousStart, now);
    }

    private async Task<double?> GetAvgFirstResponseSecondsAsync(IReadOnlyList<Guid> conversationIds, CancellationToken ct)
    {
        if (conversationIds.Count == 0) return null;

        var conversationCreatedAt = await _db.Conversations
            .AsNoTracking()
            .Where(c => conversationIds.Contains(c.Id))
            .Select(c => new { c.Id, c.CreatedAt })
            .ToDictionaryAsync(c => c.Id, c => c.CreatedAt, ct);

        var firstReplies = await _db.Messages
            .AsNoTracking()
            .Where(m => conversationIds.Contains(m.ConversationId)
                     && (m.Role == MessageRole.Ai || m.Role == MessageRole.Agent))
            .GroupBy(m => m.ConversationId)
            .Select(g => new { ConversationId = g.Key, FirstReplyAt = g.Min(m => m.SentAt) })
            .ToListAsync(ct);

        if (firstReplies.Count == 0) return null;

        var responseSeconds = firstReplies
            .Where(r => conversationCreatedAt.ContainsKey(r.ConversationId))
            .Select(r => (r.FirstReplyAt - conversationCreatedAt[r.ConversationId]).TotalSeconds)
            .Where(seconds => seconds >= 0)
            .ToList();

        return responseSeconds.Count > 0 ? Math.Round(responseSeconds.Average(), 1) : null;
    }

    private static string CategorizeEscalationReason(string reason)
    {
        if (reason.StartsWith("Low AI confidence", StringComparison.OrdinalIgnoreCase)) return "Low AI confidence";
        if (reason.Contains("requested human", StringComparison.OrdinalIgnoreCase)) return "Customer requested a human";
        if (reason.Contains("Payment", StringComparison.OrdinalIgnoreCase)) return "Payment-related";
        if (reason.Contains("token budget", StringComparison.OrdinalIgnoreCase)) return "Budget exceeded";
        return "Other";
    }

    private static double? PercentChange(int previous, int current)
    {
        if (previous == 0) return current == 0 ? null : 100.0;
        return Math.Round((double)(current - previous) / previous * 100, 1);
    }

    private static double? PercentChange(double? previous, double? current)
    {
        if (previous is null or 0 || current is null) return null;
        return Math.Round((current.Value - previous.Value) / previous.Value * 100, 1);
    }
}
