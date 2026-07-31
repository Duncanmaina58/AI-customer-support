using System.Security.Cryptography;
using System.Text;
using Api.Application.Abstractions;
using Api.Contracts.Auth;
using Api.Contracts.Companies;
using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.Identity;
using Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Api.Controllers;

/// <summary>
/// Full authentication lifecycle: register, login (with lockout), refresh,
/// logout, email verification, forgot/reset password, and change password —
/// every flow a real SaaS product needs, not just the login/register happy
/// path. Rate-limited via the "auth" policy in Program.cs on every action in
/// this controller (tighter than the global default — these are exactly the
/// endpoints a credential-stuffing/brute-force/token-guessing bot would hit).
///
/// Design notes that apply across every endpoint below:
///   - Never reveal whether an email exists in the system (Register is the
///     one necessary exception — you have to know if a sign-up succeeded).
///     ForgotPassword and ResendVerification always return the same generic
///     success response either way.
///   - Password reset / change always revokes every refresh token for that
///     agent — a password change should end every session, not just the one
///     that changed it, full stop.
///   - Every security-relevant event (locked out, password changed, new
///     sign-in, verified) sends an email — silently changing security state
///     with no notification is how account takeovers go unnoticed.
/// </summary>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    /// <summary>Consecutive failed attempts before a temporary lockout kicks in.</summary>
    private const int MaxFailedLoginAttempts = 5;

    /// <summary>How long a lockout lasts once triggered.</summary>
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan EmailVerificationTokenLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan PasswordResetTokenLifetime     = TimeSpan.FromHours(1);

    private readonly IAppDbContext _db;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ICurrentTenantProvider _currentTenant;
    private readonly IAuthEmailService _authEmail;
    private readonly ILogger<AuthController> _logger;
    private readonly PasswordHasher<Agent> _passwordHasher = new();

    public AuthController(
        IAppDbContext db,
        IJwtTokenService jwtTokenService,
        ICurrentTenantProvider currentTenant,
        IAuthEmailService authEmail,
        ILogger<AuthController> logger)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
        _currentTenant = currentTenant;
        _authEmail = authEmail;
        _logger = logger;
    }

    // =====================================================================
    // Register
    // =====================================================================

    /// <summary>
    /// Public sign-up: creates a new Company (tenant) plus its first Agent (the
    /// owner). This is the only endpoint that creates a Company without an existing
    /// authenticated tenant — by definition there's no tenant yet at sign-up time.
    /// Sends a verification email, but does NOT block using the product on
    /// verifying it first (soft gate) — the dashboard shows a banner instead;
    /// see the frontend's VerifyEmailBanner.
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

        var policyResult = PasswordPolicy.Validate(request.OwnerPassword, normalizedEmail, request.OwnerName);
        if (!policyResult.IsValid)
        {
            return BadRequest(new { message = "Password doesn't meet the minimum requirements.", errors = policyResult.Errors });
        }

        var secretApiKey = GenerateApiKey("sk");

        var company = new Company
        {
            Name = request.CompanyName.Trim(),
            PublicApiKey = GenerateApiKey("pub"),
            SecretApiKeyHash = HashSecret(secretApiKey),
            // Sprint 6: private sandbox test link (platform.com/test/{token}) —
            // separate from PublicApiKey so it can be shared with a team for
            // testing without exposing the production widget key.
            SandboxToken = GenerateApiKey("sbx"),
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
        owner.PasswordChangedAt = DateTime.UtcNow;

        _db.Companies.Add(company);
        _db.Agents.Add(owner);

        var (verificationToken, rawVerificationToken) = SecurityTokenFactory.Create(
            owner.Id, AgentSecurityTokenType.EmailVerification, EmailVerificationTokenLifetime);
        _db.AgentSecurityTokens.Add(verificationToken);

        await _db.SaveChangesAsync(ct);

        await _authEmail.SendVerificationEmailAsync(owner, rawVerificationToken, ct);

        var companyDto = new CompanyDto(company.Id, company.Name, company.Plan.ToString(), company.PublicApiKey);

        // secretApiKey is returned here ONCE — only its hash is persisted (see
        // HashSecret below), so this is the only moment it can ever be shown.
        return CreatedAtAction(nameof(Login), new RegisterCompanyResponse(companyDto, secretApiKey));
    }

    // =====================================================================
    // Login / session management
    // =====================================================================

    /// <summary>
    /// Authenticates an Agent and returns a JWT carrying their company_id + agent_id
    /// claims, plus a refresh token. Enforces a temporary lockout after
    /// <see cref="MaxFailedLoginAttempts"/> consecutive failures, and emails a
    /// "new sign-in" notice when the login IP differs from the last known one.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var callerIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Bypasses the tenant filter on purpose - there is no authenticated tenant
        // yet, that's the whole point of login.
        var agent = await _db.Agents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Email == normalizedEmail && a.IsActive, ct);

        if (agent is null)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        if (agent.LockedOutUntil is { } lockedUntil && lockedUntil > DateTime.UtcNow)
        {
            var minutesLeft = Math.Ceiling((lockedUntil - DateTime.UtcNow).TotalMinutes);
            return StatusCode(StatusCodes.Status423Locked, new
            {
                message = $"Too many failed sign-in attempts. Try again in about {minutesLeft:0} minute(s).",
            });
        }

        var verifyResult = _passwordHasher.VerifyHashedPassword(agent, agent.PasswordHash, request.Password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            agent.FailedLoginAttempts++;

            if (agent.FailedLoginAttempts >= MaxFailedLoginAttempts)
            {
                agent.LockedOutUntil = DateTime.UtcNow.Add(LockoutDuration);
                await _db.SaveChangesAsync(ct);

                _logger.LogWarning("Account locked after {Attempts} failed attempts | agent={AgentId} ip={Ip}",
                    agent.FailedLoginAttempts, agent.Id, callerIp);
                await _authEmail.SendAccountLockedEmailAsync(agent, LockoutDuration, ct);

                return StatusCode(StatusCodes.Status423Locked, new
                {
                    message = $"Too many failed sign-in attempts. Try again in about {LockoutDuration.TotalMinutes:0} minute(s).",
                });
            }

            await _db.SaveChangesAsync(ct);
            return Unauthorized(new { message = "Invalid email or password." });
        }

        // Success — reset the failure counter and issue tokens.
        var isNewLocation = agent.LastLoginIp is not null && agent.LastLoginIp != callerIp;

        agent.FailedLoginAttempts = 0;
        agent.LockedOutUntil = null;
        agent.LastActiveAt = DateTime.UtcNow;
        agent.LastLoginAtUtc = DateTime.UtcNow;
        agent.LastLoginIp = callerIp;

        var tokens = IssueTokenPair(agent);
        await _db.SaveChangesAsync(ct);

        if (isNewLocation)
        {
            await _authEmail.SendNewSignInEmailAsync(agent, callerIp, ct);
        }

        return Ok(new LoginResponse(
            AccessToken: tokens.AccessToken,
            ExpiresAtUtc: tokens.ExpiresAtUtc,
            RefreshToken: tokens.RefreshToken,
            Agent: ToAgentDto(agent)));
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
        var tokenHash = SecurityTokenFactory.HashToken(request.RefreshToken);

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
            Agent: ToAgentDto(agent)));
    }

    /// <summary>Revokes one specific refresh token (i.e. signs out the current device/session, not every session).</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken ct)
    {
        var tokenHash = SecurityTokenFactory.HashToken(request.RefreshToken);

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

    // =====================================================================
    // Email verification
    // =====================================================================

    /// <summary>Redeems a verification token from the emailed link. Idempotent — verifying an already-verified account just succeeds again.</summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthActionResult>> VerifyEmail(VerifyEmailRequest request, CancellationToken ct)
    {
        var tokenHash = SecurityTokenFactory.HashToken(request.Token);

        var token = await _db.AgentSecurityTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.Type == AgentSecurityTokenType.EmailVerification, ct);

        if (token is null || !token.IsActive)
        {
            return BadRequest(new AuthActionResult(false, "This verification link is invalid or has expired. Request a new one from your account settings."));
        }

        var agent = await _db.Agents.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == token.AgentId, ct);
        if (agent is null)
        {
            return BadRequest(new AuthActionResult(false, "This verification link is no longer valid."));
        }

        token.UsedAtUtc = DateTime.UtcNow;

        var alreadyVerified = agent.EmailVerifiedAt is not null;
        agent.EmailVerifiedAt ??= DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        if (!alreadyVerified)
        {
            await _authEmail.SendWelcomeEmailAsync(agent, ct);
        }

        return Ok(new AuthActionResult(true, "Your email is verified."));
    }

    /// <summary>Re-sends a verification email to the currently authenticated agent. Invalidates any earlier unused verification link first, so only the newest one ever works.</summary>
    [HttpPost("resend-verification")]
    [Authorize]
    public async Task<ActionResult<AuthActionResult>> ResendVerification(CancellationToken ct)
    {
        if (_currentTenant.AgentId is not { } agentId)
            return Unauthorized();

        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null) return Unauthorized();

        if (agent.EmailVerifiedAt is not null)
        {
            return Ok(new AuthActionResult(true, "Your email is already verified."));
        }

        await InvalidateActiveTokensAsync(agent.Id, AgentSecurityTokenType.EmailVerification, ct);

        var (token, rawToken) = SecurityTokenFactory.Create(agent.Id, AgentSecurityTokenType.EmailVerification, EmailVerificationTokenLifetime);
        _db.AgentSecurityTokens.Add(token);
        await _db.SaveChangesAsync(ct);

        await _authEmail.SendVerificationEmailAsync(agent, rawToken, ct);

        return Ok(new AuthActionResult(true, "Verification email sent — check your inbox."));
    }

    // =====================================================================
    // Forgot / reset password
    // =====================================================================

    /// <summary>
    /// Always returns the same generic success response whether or not the
    /// email exists — revealing that would let anyone enumerate real accounts.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthActionResult>> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var callerIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        var agent = await _db.Agents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Email == normalizedEmail && a.IsActive, ct);

        if (agent is not null)
        {
            await InvalidateActiveTokensAsync(agent.Id, AgentSecurityTokenType.PasswordReset, ct);

            var (token, rawToken) = SecurityTokenFactory.Create(agent.Id, AgentSecurityTokenType.PasswordReset, PasswordResetTokenLifetime, callerIp);
            _db.AgentSecurityTokens.Add(token);
            await _db.SaveChangesAsync(ct);

            await _authEmail.SendPasswordResetEmailAsync(agent, rawToken, ct);
        }
        else
        {
            _logger.LogInformation("Password reset requested for unknown email | ip={Ip}", callerIp);
        }

        return Ok(new AuthActionResult(true, "If an account exists with that email, a password reset link has been sent."));
    }

    /// <summary>Redeems a password-reset token, sets the new password, and revokes every session (all refresh tokens) for that agent.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthActionResult>> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        var tokenHash = SecurityTokenFactory.HashToken(request.Token);

        var token = await _db.AgentSecurityTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.Type == AgentSecurityTokenType.PasswordReset, ct);

        if (token is null || !token.IsActive)
        {
            return BadRequest(new AuthActionResult(false, "This reset link is invalid or has expired. Request a new one."));
        }

        var agent = await _db.Agents.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == token.AgentId, ct);
        if (agent is null || !agent.IsActive)
        {
            return BadRequest(new AuthActionResult(false, "This reset link is no longer valid."));
        }

        var policyResult = PasswordPolicy.Validate(request.NewPassword, agent.Email, agent.Name);
        if (!policyResult.IsValid)
        {
            return BadRequest(new { success = false, message = "Password doesn't meet the minimum requirements.", errors = policyResult.Errors });
        }

        agent.PasswordHash = _passwordHasher.HashPassword(agent, request.NewPassword);
        agent.PasswordChangedAt = DateTime.UtcNow;
        agent.FailedLoginAttempts = 0;
        agent.LockedOutUntil = null;
        token.UsedAtUtc = DateTime.UtcNow;

        await RevokeAllRefreshTokensAsync(agent.Id, ct);
        await _db.SaveChangesAsync(ct);

        await _authEmail.SendPasswordChangedEmailAsync(agent, ct);

        return Ok(new AuthActionResult(true, "Your password has been reset. Sign in with your new password."));
    }

    /// <summary>Changes the current agent's password (requires the current one), and revokes every session including this one — the client must log in again.</summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult<AuthActionResult>> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        if (_currentTenant.AgentId is not { } agentId)
            return Unauthorized();

        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null) return Unauthorized();

        var verifyResult = _passwordHasher.VerifyHashedPassword(agent, agent.PasswordHash, request.CurrentPassword);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            return BadRequest(new AuthActionResult(false, "Current password is incorrect."));
        }

        var policyResult = PasswordPolicy.Validate(request.NewPassword, agent.Email, agent.Name);
        if (!policyResult.IsValid)
        {
            return BadRequest(new { success = false, message = "Password doesn't meet the minimum requirements.", errors = policyResult.Errors });
        }

        agent.PasswordHash = _passwordHasher.HashPassword(agent, request.NewPassword);
        agent.PasswordChangedAt = DateTime.UtcNow;

        await RevokeAllRefreshTokensAsync(agent.Id, ct);
        await _db.SaveChangesAsync(ct);

        await _authEmail.SendPasswordChangedEmailAsync(agent, ct);

        return Ok(new AuthActionResult(true, "Password changed. Please sign in again."));
    }

    // =====================================================================
    // Helpers
    // =====================================================================

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
            TokenHash = SecurityTokenFactory.HashToken(refreshTokenRaw),
            ExpiresAtUtc = refreshExpiresAt,
        };

        _db.RefreshTokens.Add(refreshTokenEntity);

        return (accessToken.Token, accessToken.ExpiresAtUtc, refreshTokenRaw, refreshTokenEntity.Id);
    }

    private async Task RevokeAllRefreshTokensAsync(Guid agentId, CancellationToken ct)
    {
        var activeTokens = await _db.RefreshTokens
            .Where(t => t.AgentId == agentId && t.RevokedAtUtc == null)
            .ToListAsync(ct);

        foreach (var t in activeTokens)
        {
            t.RevokedAtUtc = DateTime.UtcNow;
        }
    }

    /// <summary>Marks every still-active token of a given type as used, so a stale earlier email link stops working the moment a newer one is issued.</summary>
    private async Task InvalidateActiveTokensAsync(Guid agentId, AgentSecurityTokenType type, CancellationToken ct)
    {
        var activeTokens = await _db.AgentSecurityTokens
            .Where(t => t.AgentId == agentId && t.Type == type && t.UsedAtUtc == null)
            .ToListAsync(ct);

        foreach (var t in activeTokens)
        {
            t.UsedAtUtc = DateTime.UtcNow;
        }
    }

    private static AgentDto ToAgentDto(Agent agent) =>
        new(agent.Id, agent.Name, agent.Email, agent.Role.ToString(), agent.CompanyId, agent.EmailVerifiedAt is not null);

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
