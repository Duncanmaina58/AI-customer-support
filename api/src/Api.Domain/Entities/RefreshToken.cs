using Api.Domain.Common;

namespace Api.Domain.Entities;

/// <summary>
/// A persisted, single-use refresh token. "Single-use" means redeeming one always
/// revokes it and issues a brand new one (rotation) - if a revoked token is ever
/// presented again, that's a strong signal of token theft (see AuthController.Refresh).
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AgentId { get; set; }
    public Agent? Agent { get; set; }

    /// <summary>SHA-256 hash of the token - the plaintext is only ever returned to the client once.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Set when this token was redeemed and rotated into a new one - lets us trace a chain.</summary>
    public Guid? ReplacedByTokenId { get; set; }

    public bool IsActive => RevokedAtUtc is null && DateTime.UtcNow < ExpiresAtUtc;
}
