using Api.Domain.Enums;

namespace Api.Domain.Entities;

/// <summary>
/// A Company is a tenant. Every other tenant-scoped entity hangs off this via CompanyId.
/// </summary>
public class Company
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>Public key used by the script-tag widget and mobile SDK.</summary>
    public string PublicApiKey { get; set; } = string.Empty;

    /// <summary>Secret key used for server-to-server REST API calls. Store only a hash.</summary>
    public string SecretApiKeyHash { get; set; } = string.Empty;

    public CompanyPlan Plan { get; set; } = CompanyPlan.Starter;
    public string TimeZone { get; set; } = "Africa/Nairobi";
    public string DefaultCurrency { get; set; } = "KES";

    // --- Onboarding wizard (Sprint 2) ---
    /// <summary>e.g. "Healthcare", "Retail", "Hospitality" - free text, shown as a dropdown of suggestions in the UI.</summary>
    public string? Industry { get; set; }
    public string? LogoUrl { get; set; }
    public BrandVoice BrandVoice { get; set; } = BrandVoice.Friendly;
    /// <summary>ISO 639-1 code, e.g. "en", "sw".</summary>
    public string PrimaryLanguage { get; set; } = "en";
    /// <summary>JSON: {"mon":{"open":"08:00","close":"18:00"},...,"closedDays":["sun"]}. Null until wizard step 3 is saved.</summary>
    public string? BusinessHoursJson { get; set; }
    /// <summary>Set the moment the onboarding wizard's final step completes - null means "still onboarding".</summary>
    public DateTime? OnboardingCompletedAt { get; set; }

    // --- Forward-looking fields used by later sprints, defined now to avoid a second
    //     migration touching this table again (Sprint 4 token budgeting, Sprint 5 escalation) ---
    public int MonthlyTokenBudget { get; set; } = 100_000;
    public int TokensUsedThisMonth { get; set; }
    /// <summary>JSON escalation rules (Sprint 5) - e.g. sentiment threshold, keyword triggers. Null = use defaults.</summary>
    public string? EscalationRulesJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<Agent> Agents { get; set; } = new List<Agent>();
    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    public ICollection<ChannelConnection> ChannelConnections { get; set; } = new List<ChannelConnection>();
}
