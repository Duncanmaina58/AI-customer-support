namespace Api.Contracts.Analytics;

/// <summary>
/// TrendPercent fields: null means "no data in the previous period to compare
/// against" (e.g. a brand-new company) — the frontend shows no arrow rather
/// than a misleading "+∞%" or "0%".
/// </summary>
public record AnalyticsSummaryDto(
    int TotalConversations,
    double? ConversationsTrendPercent,
    int OpenConversations,
    int ResolvedConversations,
    int EscalatedConversations,
    double ResolutionRate,
    /// <summary>% of conversations the AI handled without ever escalating to a human — a standard support-ops "deflection"/containment metric.</summary>
    double ContainmentRate,
    double? ContainmentRateTrendPercent,
    double? AvgFirstResponseSeconds,
    double? AvgFirstResponseTrendPercent,
    int TokensUsedThisMonth,
    int MonthlyTokenBudget,
    double? CsatAverageScore,
    int CsatRatingCount);

public record DailyConversationCountDto(DateTime Date, int Count);

public record ChannelBreakdownDto(string Channel, int Count);

public record TopQuestionDto(string Question, int Count);

public record EscalationReasonBreakdownDto(string Reason, int Count);

public record DailyTokenUsageDto(DateTime Date, int Tokens);

public record CsatDistributionBucketDto(int Score, int Count);

public record CsatTrendPointDto(DateTime Date, double? AverageScore, int RatingCount);

public record CsatSummaryDto(
    double? AverageScore,
    int RatingCount,
    IReadOnlyList<CsatDistributionBucketDto> Distribution,
    IReadOnlyList<CsatTrendPointDto> Trend);
