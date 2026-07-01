namespace Api.Contracts.Analytics;

public record AnalyticsSummaryDto(
    int TotalConversations,
    int OpenConversations,
    int ResolvedConversations,
    int EscalatedConversations,
    double ResolutionRate,
    int TokensUsedThisMonth,
    int MonthlyTokenBudget);
