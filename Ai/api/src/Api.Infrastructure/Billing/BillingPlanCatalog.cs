using Api.Domain.Enums;

namespace Api.Infrastructure.Billing;

public record BillingPlanInfo(
    CompanyPlan Plan,
    string      Name,
    decimal     PriceKes,
    int         ConversationLimit,
    int         MonthlyTokenBudget,
    int         ChannelLimit,
    int         KnowledgeBaseLimit,
    int         AgentLimit,
    IReadOnlyList<string> Features);

/// <summary>
/// Sprint 7: the three Phase 1 pricing tiers, matching the doc's section 8.1
/// exactly on price/conversation limits. A real database table would be
/// overkill for three fixed tiers that only change via a code deploy — this
/// static catalog is the single source of truth, used by the pricing page,
/// the billing dashboard, and BillingController's M-Pesa amount calculation.
///
/// Token budgets (the unit this codebase actually *enforces* against, per
/// Sprint 4/5's design) are picked to comfortably cover each tier's
/// conversation limit at typical per-conversation token usage, not derived
/// from the doc (which only specifies conversation counts, not tokens).
/// Starter's 100,000 matches Company.MonthlyTokenBudget's existing default,
/// so a brand-new signup on the default plan needs no data migration.
/// </summary>
public static class BillingPlanCatalog
{
    public static readonly IReadOnlyDictionary<CompanyPlan, BillingPlanInfo> Plans =
        new Dictionary<CompanyPlan, BillingPlanInfo>
        {
            [CompanyPlan.Starter] = new(
                CompanyPlan.Starter, "Starter", 2_500m,
                ConversationLimit: 1_000, MonthlyTokenBudget: 100_000,
                ChannelLimit: 2, KnowledgeBaseLimit: 1, AgentLimit: 2,
                Features:
                [
                    "1,000 conversations / month",
                    "2 channels",
                    "1 knowledge base (up to 50 pages)",
                    "Basic analytics",
                ]),

            [CompanyPlan.Growth] = new(
                CompanyPlan.Growth, "Growth", 8_000m,
                ConversationLimit: 5_000, MonthlyTokenBudget: 500_000,
                ChannelLimit: 5, KnowledgeBaseLimit: 5, AgentLimit: 3,
                Features:
                [
                    "5,000 conversations / month",
                    "5 channels",
                    "5 knowledge bases",
                    "Tickets & escalation",
                    "3 agent seats",
                    "Full analytics",
                ]),

            [CompanyPlan.Enterprise] = new(
                CompanyPlan.Enterprise, "Enterprise", 25_000m,
                ConversationLimit: int.MaxValue, MonthlyTokenBudget: 2_000_000,
                ChannelLimit: int.MaxValue, KnowledgeBaseLimit: int.MaxValue, AgentLimit: int.MaxValue,
                Features:
                [
                    "Unlimited conversations",
                    "All channels",
                    "Unlimited knowledge bases",
                    "Dedicated onboarding",
                    "Priority support",
                ]),
        };

    /// <summary>KES 0.80 per conversation over the plan's monthly limit — Phase 1 doc section 8.1.</summary>
    public const decimal OverageRatePerConversation = 0.80m;

    public static BillingPlanInfo Get(CompanyPlan plan) => Plans[plan];
}
