using System.Security.Cryptography;
using System.Text;
using Api.Domain.Entities;
using Api.Domain.Enums;

namespace Api.Infrastructure.Identity;

/// <summary>
/// Auth hardening: the one place that mints AgentSecurityToken rows (email
/// verification / password reset) and their matching hash-check helper for
/// redeeming a raw refresh/security token — shared by AuthController (self
/// sign-up, forgot/reset password) and AgentsController (inviting a
/// teammate also needs to send them a verification email for their new
/// account) so the token-generation logic exists in exactly one place.
/// </summary>
public static class SecurityTokenFactory
{
    public static (AgentSecurityToken Token, string RawToken) Create(
        Guid agentId, AgentSecurityTokenType type, TimeSpan lifetime, string? requestedFromIp = null)
    {
        var rawBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(rawBytes).Replace("+", "").Replace("/", "").Replace("=", "");

        var token = new AgentSecurityToken
        {
            AgentId = agentId,
            Type = type,
            TokenHash = HashToken(rawToken),
            ExpiresAtUtc = DateTime.UtcNow.Add(lifetime),
            RequestedFromIp = requestedFromIp,
        };

        return (token, rawToken);
    }

    /// <summary>Same hashing scheme used for refresh tokens (RefreshToken.TokenHash) — SHA-256, hex-encoded.</summary>
    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
