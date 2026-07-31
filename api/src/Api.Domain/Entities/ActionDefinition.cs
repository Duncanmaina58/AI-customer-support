using Api.Domain.Common;

namespace Api.Domain.Entities;

/// <summary>
/// Sprint 9 Action Engine: one registered "thing the AI can do on this
/// company's behalf" — e.g. action_type "cancel_order" pointing at the
/// company's own backend webhook. Created/edited from the dashboard's Actions
/// page (ActionDefinitionsController); looked up at chat time by
/// IActionRegistry keyed on (CompanyId, ActionType).
/// </summary>
public class ActionDefinition : AuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    /// <summary>Machine identifier the AI's intent-detection JSON block refers to, e.g. "cancel_order". Unique per company.</summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>Human-readable name shown in the dashboard and used when composing confirmation messages, e.g. "Cancel an order".</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The client's own backend endpoint. HTTPS required — enforced in ActionDefinitionsController, not here.</summary>
    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>
    /// The shared HMAC-SHA256 signing secret, ENCRYPTED at rest (reversible —
    /// see Api.Infrastructure.Security.ActionEngineSecretProtector), not a
    /// one-way hash: WebhookActionRunner needs the raw secret back on every
    /// call to compute a fresh signature, which a one-way hash can never
    /// provide. Named to match the spec's schema, despite that name normally
    /// implying a one-way hash elsewhere in this codebase (e.g. Company.SecretApiKeyHash) —
    /// see the design note in docs/security-audit.md's Sprint 9 addendum.
    /// </summary>
    public string WebhookSecretEncrypted { get; set; } = string.Empty;

    /// <summary>If true, the customer must complete OTP verification before this action runs.</summary>
    public bool RequiresVerification { get; set; }

    /// <summary>If true, skips verification even if RequiresVerification is set — for actions like balance/status checks where reading data isn't sensitive enough to warrant an OTP round-trip. IsReadOnly wins over RequiresVerification when both are set.</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>Optional JSON Schema describing the parameters the AI should extract from the customer's message — shown to the model in the system prompt, not enforced server-side in Sprint 9 (a validation pass against this schema is a natural Sprint 10+ hardening step).</summary>
    public string? ParameterSchema { get; set; }

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Soft-disable without losing history — ActionLogs reference this row, so it's never hard-deleted once used (ActionDefinitionsController blocks delete if any ActionLogs exist; use IsActive = false instead).</summary>
    public bool IsActive { get; set; } = true;
}
