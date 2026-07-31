using Api.Domain.Enums;

namespace Api.Domain.Entities;

/// <summary>
/// Auth hardening: a single-use, expiring, hashed token proving control of an
/// inbox — used for both email verification and password reset (distinguished
/// by <see cref="Type"/>), the same trust primitive under two names. Mirrors
/// RefreshToken's shape deliberately (hash-at-rest, single-use, expiring) —
/// see AuthController for the issue/redeem flows.
///
/// Old tokens aren't deleted when a new one is issued for the same purpose;
/// they're just never valid again once superseded (AuthController invalidates
/// prior unused tokens of the same type when issuing a new one, so only the
/// most recent link in an email ever works — no confusion from an old
/// "reset your password" email still being clickable after a newer one was sent).
/// </summary>
public class AgentSecurityToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AgentId { get; set; }
    public Agent? Agent { get; set; }

    public AgentSecurityTokenType Type { get; set; }

    /// <summary>SHA-256 hash of the token — the plaintext is only ever in the email link, never persisted.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAtUtc { get; set; }

    /// <summary>Best-effort audit trail — the IP that requested this token (not the IP that redeemed it).</summary>
    public string? RequestedFromIp { get; set; }

    public bool IsActive => UsedAtUtc is null && DateTime.UtcNow < ExpiresAtUtc;
}
