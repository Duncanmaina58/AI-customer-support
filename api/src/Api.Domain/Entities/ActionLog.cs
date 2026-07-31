using Api.Domain.Common;
using Api.Domain.Enums;

namespace Api.Domain.Entities;

/// <summary>
/// Sprint 9 Action Engine: an immutable record of one action execution
/// attempt — written exactly once by ActionEngineService, whether the action
/// succeeded or failed. Required for bank/telco compliance per the Phase 2
/// spec, so "immutable" isn't just a design preference here — see
/// AppDbContext.SaveChangesAsync, which throws if any ActionLog row is ever
/// found in the Modified or Deleted state, rather than relying on convention
/// alone (application code simply never calling .Update()/.Remove() on this
/// DbSet is not "verified", it's "hoped for" — the override actually enforces it).
///
/// Deliberately does NOT inherit AuditableEntity: there's no UpdatedAt,
/// because there must never be an update.
/// </summary>
public class ActionLog : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public Guid ConversationId { get; set; }

    /// <summary>Null if the action type was requested but never matched a registered ActionDefinition (logged anyway, for visibility into what customers are asking for that isn't wired up yet).</summary>
    public Guid? ActionDefinitionId { get; set; }
    public ActionDefinition? ActionDefinition { get; set; }

    /// <summary>Denormalized from ActionDefinition.ActionType for fast audit queries without a join, and so the log is still meaningful even when ActionDefinitionId is null.</summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>Who requested the action — the customer's channel identifier (phone number, web session id, etc.), same shape as Conversation.CustomerId.</summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>
    /// The extracted parameters (e.g. {"order_id":"12345"}), ENCRYPTED at rest
    /// via ActionEngineSecretProtector — the spec flags these as "may be PII",
    /// and order/account/phone numbers are exactly that.
    /// </summary>
    public string ParametersEncrypted { get; set; } = string.Empty;

    public bool IdentityVerified { get; set; }
    public VerificationMethod VerificationMethod { get; set; } = VerificationMethod.None;

    public int? WebhookStatusCode { get; set; }
    public string? WebhookResponseJson { get; set; }

    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public int DurationMs { get; set; }
}
