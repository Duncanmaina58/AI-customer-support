using System.Security.Cryptography;
using System.Text;
using Api.Application.Abstractions;
using Api.Contracts.Auth;
using Api.Contracts.Companies;
using Api.Domain.Entities;
using Api.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ICurrentTenantProvider _currentTenant;
    private readonly PasswordHasher<Agent> _passwordHasher = new();

    public AuthController(IAppDbContext db, IJwtTokenService jwtTokenService, ICurrentTenantProvider currentTenant)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
        _currentTenant = currentTenant;
    }

    /// <summary>
    /// Public sign-up: creates a new Company (tenant) plus its first Agent (the
    /// owner). This is the only endpoint that creates a Company without an existing
    /// authenticated tenant — by definition there's no tenant yet at sign-up time.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<RegisterCompanyResponse>> Register(RegisterCompanyRequest request, CancellationToken ct)
    {
        var normalizedEmail = request.OwnerEmail.Trim().ToLowerInvariant();

        var emailTaken = await _db.Agents
            .IgnoreQueryFilters()
            .AnyAsync(a => a.Email == normalizedEmail, ct);

        if (emailTaken)
        {
            return Conflict(new { message = "An account with that email already exists." });
        }

        var secretApiKey = GenerateApiKey("sk");

        var company = new Company
        {
            Name = request.CompanyName.Trim(),
            PublicApiKey = GenerateApiKey("pub"),
            SecretApiKeyHash = HashSecret(secretApiKey),
        };

        var owner = new Agent
        {
            CompanyId = company.Id,
            Company = company,
            Name = request.OwnerName.Trim(),
            Email = normalizedEmail,
            Role = Api.Domain.Enums.AgentRole.Owner,
        };
        owner.PasswordHash = _passwordHasher.HashPassword(owner, request.OwnerPassword);

        _db.Companies.Add(company);
        _db.Agents.Add(owner);
        await _db.SaveChangesAsync(ct);

        var companyDto = new CompanyDto(company.Id, company.Name, company.Plan.ToString(), company.PublicApiKey);

        // secretApiKey is returned here ONCE — only its hash is persisted (see
        // HashSecret below), so this is the only moment it can ever be shown.
        return CreatedAtAction(nameof(Login), new RegisterCompanyResponse(companyDto, secretApiKey));
    }

    /// <summary>
    /// Authenticates an Agent and returns a JWT carrying their company_id + agent_id
    /// claims, plus a refresh token. Every subsequent authenticated request is scoped
    /// to that company via the global EF Core query filter — see
    /// AppDbContext.OnModelCreating.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        // Bypasses the tenant filter on purpose - there is no authenticated tenant
        // yet, that's the whole point of login.
        var agent = await _db.Agents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Email == normalizedEmail && a.IsActive, ct);

        if (agent is null)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var verifyResult = _passwordHasher.VerifyHashedPassword(agent, agent.PasswordHash, request.Password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        agent.LastActiveAt = DateTime.UtcNow;

        var tokens = IssueTokenPair(agent);
        await _db.SaveChangesAsync(ct);

        return Ok(new LoginResponse(
            AccessToken: tokens.AccessToken,
            ExpiresAtUtc: tokens.ExpiresAtUtc,
            RefreshToken: tokens.RefreshToken,
            Agent: new AgentDto(agent.Id, agent.Name, agent.Email, agent.Role.ToString(), agent.CompanyId)));
    }

    /// <summary>
    /// Exchanges a still-valid refresh token for a brand new access + refresh token
    /// pair, and revokes the one just redeemed (rotation - each refresh token can
    /// only ever be used once). If a token that's already revoked is presented
    /// again, every other active refresh token for that agent is revoked too: that
    /// pattern only happens if a stolen token is replayed after the legitimate
    /// client already rotated past it, so the safest response is to force the whole
    /// account to re-authenticate.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var tokenHash = HashToken(request.RefreshToken);

        var storedToken = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (storedToken is null)
        {
            return Unauthorized(new { message = "Invalid refresh token." });
        }

        if (storedToken.RevokedAtUtc is not null)
        {
            // Reuse of an already-rotated token - possible theft. Revoke every other
            // active token for this agent so a stolen token can't keep working.
            var otherActiveTokens = await _db.RefreshTokens
                .Where(t => t.AgentId == storedToken.AgentId && t.RevokedAtUtc == null)
                .ToListAsync(ct);

            foreach (var t in otherActiveTokens)
            {
                t.RevokedAtUtc = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(ct);

            return Unauthorized(new { message = "This refresh token has already been used. Please log in again." });
        }

        if (DateTime.UtcNow >= storedToken.ExpiresAtUtc)
        {
            return Unauthorized(new { message = "Refresh token has expired. Please log in again." });
        }

        // No tenant context yet (this is effectively a second login), so bypass the
        // filter exactly like Login does.
        var agent = await _db.Agents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == storedToken.AgentId && a.IsActive, ct);

        if (agent is null)
        {
            return Unauthorized(new { message = "Account is no longer active." });
        }

        var tokens = IssueTokenPair(agent);
        storedToken.RevokedAtUtc = DateTime.UtcNow;
        storedToken.ReplacedByTokenId = tokens.NewTokenId;

        await _db.SaveChangesAsync(ct);

        return Ok(new LoginResponse(
            AccessToken: tokens.AccessToken,
            ExpiresAtUtc: tokens.ExpiresAtUtc,
            RefreshToken: tokens.RefreshToken,
            Agent: new AgentDto(agent.Id, agent.Name, agent.Email, agent.Role.ToString(), agent.CompanyId)));
    }

    /// <summary>Revokes one specific refresh token (i.e. signs out the current device/session, not every session).</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken ct)
    {
        var tokenHash = HashToken(request.RefreshToken);

        // Scoped to the current agent so you can only ever revoke your own tokens,
        // even if you somehow guessed someone else's hash.
        var storedToken = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.AgentId == _currentTenant.AgentId, ct);

        if (storedToken is not null && storedToken.RevokedAtUtc is null)
        {
            storedToken.RevokedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        // Idempotent either way - a logout call for an already-revoked or unknown
        // token still "succeeds" from the client's point of view.
        return NoContent();
    }

    /// <summary>
    /// Builds a new access + refresh token pair and stages the refresh token for
    /// insertion (added to the change tracker, not yet saved - the caller decides
    /// when to call SaveChangesAsync, often alongside another update in the same
    /// transaction, e.g. revoking the token being rotated away from in Refresh).
    /// Purely in-memory work, no I/O, so deliberately not async.
    /// </summary>
    private (string AccessToken, DateTime ExpiresAtUtc, string RefreshToken, Guid NewTokenId) IssueTokenPair(Agent agent)
    {
        var accessToken = _jwtTokenService.CreateAccessToken(agent);
        var (refreshTokenRaw, refreshExpiresAt) = _jwtTokenService.CreateRefreshToken();

        var refreshTokenEntity = new RefreshToken
        {
            AgentId = agent.Id,
            TokenHash = HashToken(refreshTokenRaw),
            ExpiresAtUtc = refreshExpiresAt,
        };

        _db.RefreshTokens.Add(refreshTokenEntity);

        return (accessToken.Token, accessToken.ExpiresAtUtc, refreshTokenRaw, refreshTokenEntity.Id);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private static string GenerateApiKey(string prefix)
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        var token = Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "");
        return $"{prefix}_{token}";
    }

    private static string HashSecret(string secret)
    {
        // Secret API key is hashed at rest the same way a password would be —
        // it's never stored or logged in plaintext after this point.
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }
}
