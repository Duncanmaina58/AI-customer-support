using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Api.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Api.Infrastructure.Identity;

public class JwtOptions
{
    public string SigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "ai-support-platform";
    public string Audience { get; set; } = "ai-support-platform-users";

    /// <summary>Bound from Jwt:ExpiryMinutes in appsettings — keep this name in sync with config.</summary>
    public int ExpiryMinutes { get; set; } = 60;

    /// <summary>No matching config key yet (refresh-token rotation lands in Sprint 2); code default only.</summary>
    public int RefreshTokenDays { get; set; } = 14;
}

public interface IJwtTokenService
{
    AccessTokenResult CreateAccessToken(Agent agent);
    (string token, DateTime expiresAt) CreateRefreshToken();
}

public record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

/// <summary>
/// Issues access tokens carrying the company_id / agent_id claims that
/// HttpCurrentTenantProvider reads back out on every subsequent request.
/// Refresh tokens are opaque random strings — store the hash + expiry against
/// the Agent (or a dedicated RefreshTokens table) and rotate on use.
/// </summary>
public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IConfiguration configuration)
    {
        _options = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is not configured. Set it via appsettings, an environment " +
                "variable, or `dotnet user-secrets` — never commit a real signing key.");
        }
    }

    public AccessTokenResult CreateAccessToken(Agent agent)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, agent.Id.ToString()),
            new(HttpCurrentTenantProvider.AgentIdClaimType, agent.Id.ToString()),
            new(HttpCurrentTenantProvider.CompanyIdClaimType, agent.CompanyId.ToString()),
            new(ClaimTypes.Email, agent.Email),
            new(ClaimTypes.Role, agent.Role.ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessTokenResult(tokenString, expiresAtUtc);
    }

    public (string token, DateTime expiresAt) CreateRefreshToken()
    {
        var bytes = new byte[64];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var token = Convert.ToBase64String(bytes);
        return (token, DateTime.UtcNow.AddDays(_options.RefreshTokenDays));
    }
}
