using Api.Domain.Common;
using Api.Domain.Enums;

namespace Api.Domain.Entities;

/// <summary>
/// A human user belonging to a Company — agents, admins, the company owner.
/// (Distinct from the AI itself, which acts as a virtual sender on Messages.)
/// </summary>
public class Agent : AuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public AgentRole Role { get; set; } = AgentRole.Agent;

    public DateTime? LastActiveAt { get; set; }
    public bool IsActive { get; set; } = true;

    // ---- Auth hardening -----------------------------------------------
    // See AuthController + AgentSecurityToken for the verification/reset
    // flows that populate these, and Api.Infrastructure.Identity.PasswordPolicy
    // for what a valid PasswordHash was checked against before hashing.

    /// <summary>Null until the owner of this email clicks the verification link. Soft-gated: an unverified agent can still use the product, but sees a "verify your email" banner (see Sprint 8's onboarding UX principle of not blocking first-run value).</summary>
    public DateTime? EmailVerifiedAt { get; set; }

    /// <summary>Reset to 0 on every successful login. Drives the temporary lockout below — see AuthController.Login.</summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>Set after too many consecutive failed attempts; login is refused (423) until this passes, even with the correct password — see AuthController.Login.</summary>
    public DateTime? LockedOutUntil { get; set; }

    /// <summary>When the current PasswordHash was set — shown to the agent in Security settings ("Password last changed on ...") and used to decide whether to warn about a stale password.</summary>
    public DateTime? PasswordChangedAt { get; set; }

    /// <summary>IP of the most recent successful login — compared against the current one on each login to decide whether a "new sign-in" notification email is warranted. Not a security control by itself (IPs are trivially spoofable/shared), just a courtesy heads-up.</summary>
    public string? LastLoginIp { get; set; }

    public DateTime? LastLoginAtUtc { get; set; }
}
