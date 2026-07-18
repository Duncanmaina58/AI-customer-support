using System.Security.Cryptography;
using Api.Application.Abstractions;
using Api.Contracts.Agents;
using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/agents")]
public class AgentsController : ControllerBase
{
    private static readonly TimeSpan EmailVerificationTokenLifetime = TimeSpan.FromHours(24);

    private readonly IAppDbContext _db;
    private readonly ICurrentTenantProvider _currentTenant;
    private readonly IAuthEmailService _authEmail;
    private readonly PasswordHasher<Agent> _passwordHasher = new();

    public AgentsController(IAppDbContext db, ICurrentTenantProvider currentTenant, IAuthEmailService authEmail)
    {
        _db = db;
        _currentTenant = currentTenant;
        _authEmail = authEmail;
    }

    /// <summary>Everyone on the team can see their teammates.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AgentListItemDto>>> List(CancellationToken ct)
    {
        var agents = await _db.Agents
            .AsNoTracking()
            .OrderBy(a => a.CreatedAt)
            .Select(a => new AgentListItemDto(a.Id, a.Name, a.Email, a.Role.ToString(), a.IsActive, a.LastActiveAt, a.CreatedAt))
            .ToListAsync(ct);

        return Ok(agents);
    }

    [HttpGet("me")]
    public async Task<ActionResult<AgentListItemDto>> Me(CancellationToken ct)
    {
        var agent = await _db.Agents
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == _currentTenant.AgentId, ct);

        if (agent is null)
        {
            return NotFound();
        }

        return Ok(new AgentListItemDto(agent.Id, agent.Name, agent.Email, agent.Role.ToString(), agent.IsActive, agent.LastActiveAt, agent.CreatedAt));
    }

    /// <summary>
    /// Creates a new Agent under the current tenant and returns a one-time temporary
    /// password. There's no email/invite-link service wired up yet (that's a Sprint 5
    /// item), so the inviting Owner/Admin is responsible for sharing this password
    /// with the new teammate out-of-band — it cannot be retrieved again afterwards.
    /// </summary>
    [HttpPost("invite")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<InviteAgentResponse>> Invite(InviteAgentRequest request, CancellationToken ct)
    {
        if (_currentTenant.CompanyId is not { } companyId)
        {
            // Should be unreachable given [Authorize] + the JWT always carrying a
            // company_id claim, but failing loudly here is far better than a
            // NullReferenceException three lines into building the new Agent.
            return Unauthorized(new { message = "No company context on this request." });
        }

        if (!Enum.TryParse<AgentRole>(request.Role, true, out var role) || role == AgentRole.Owner)
        {
            return BadRequest(new { message = "Role must be 'Admin' or 'Agent'. Ownership can't be granted via invite." });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return BadRequest(new { message = "Name and email are required." });
        }

        // Email is globally unique across the platform (see AgentConfiguration), so this
        // check intentionally bypasses the tenant filter — a duplicate in ANY company
        // must be rejected, not just within this one.
        var emailTaken = await _db.Agents.IgnoreQueryFilters().AnyAsync(a => a.Email == normalizedEmail, ct);
        if (emailTaken)
        {
            return Conflict(new { message = "An account with that email already exists." });
        }

        var temporaryPassword = GenerateTemporaryPassword();

        var agent = new Agent
        {
            CompanyId = companyId,
            Name = request.Name.Trim(),
            Email = normalizedEmail,
            Role = role,
        };
        agent.PasswordHash = _passwordHasher.HashPassword(agent, temporaryPassword);
        agent.PasswordChangedAt = DateTime.UtcNow;

        _db.Agents.Add(agent);

        // Same soft-gate verification flow as self-registration (AuthController.Register)
        // — an invited teammate is a brand-new Agent row too, so they need the same
        // "prove you control this inbox" nudge, not just the person who signed up first.
        var (verificationToken, rawVerificationToken) = SecurityTokenFactory.Create(
            agent.Id, AgentSecurityTokenType.EmailVerification, EmailVerificationTokenLifetime);
        _db.AgentSecurityTokens.Add(verificationToken);

        await _db.SaveChangesAsync(ct);

        await _authEmail.SendVerificationEmailAsync(agent, rawVerificationToken, ct);

        var dto = new AgentListItemDto(agent.Id, agent.Name, agent.Email, agent.Role.ToString(), agent.IsActive, agent.LastActiveAt, agent.CreatedAt);
        return CreatedAtAction(nameof(List), new InviteAgentResponse(dto, temporaryPassword));
    }

    /// <summary>
    /// Updates an existing teammate's role and/or active status. Deliberately cannot
    /// be used to modify your own account (avoids accidentally locking yourself out)
    /// or to remove the company's last active Owner (every company must always have
    /// at least one).
    /// </summary>
    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<AgentListItemDto>> Update(Guid id, UpdateAgentRequest request, CancellationToken ct)
    {
        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agent is null)
        {
            return NotFound();
        }

        if (agent.Id == _currentTenant.AgentId)
        {
            return BadRequest(new { message = "You can't change your own role or active status from team management." });
        }

        AgentRole? newRole = null;
        if (request.Role is not null)
        {
            if (!Enum.TryParse<AgentRole>(request.Role, true, out var parsedRole) || parsedRole == AgentRole.Owner)
            {
                return BadRequest(new { message = "Role must be 'Admin' or 'Agent'." });
            }
            newRole = parsedRole;
        }

        var wouldRemoveOwnerStatus = agent.Role == AgentRole.Owner && newRole is not null && newRole != AgentRole.Owner;
        var wouldDeactivateOwner = agent.Role == AgentRole.Owner && request.IsActive == false;

        if (wouldRemoveOwnerStatus || wouldDeactivateOwner)
        {
            var activeOwnerCount = await _db.Agents.CountAsync(a => a.Role == AgentRole.Owner && a.IsActive, ct);
            if (activeOwnerCount <= 1)
            {
                return BadRequest(new { message = "A company must always have at least one active Owner." });
            }
        }

        if (newRole is not null)
        {
            agent.Role = newRole.Value;
        }
        if (request.IsActive is not null)
        {
            agent.IsActive = request.IsActive.Value;
        }

        await _db.SaveChangesAsync(ct);

        return Ok(new AgentListItemDto(agent.Id, agent.Name, agent.Email, agent.Role.ToString(), agent.IsActive, agent.LastActiveAt, agent.CreatedAt));
    }

    private static string GenerateTemporaryPassword()
    {
        // Avoids visually-ambiguous characters (0/O, 1/l/I) since a human has to
        // read this off a screen and retype/share it.
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        const int length = 16;

        // 256 isn't evenly divisible by 54, so a plain `randomByte % alphabet.Length`
        // would be very slightly biased toward the first few characters. Rejection
        // sampling (discard bytes that don't map evenly) keeps it uniform.
        var maxValidByte = (256 / alphabet.Length) * alphabet.Length;

        var chars = new char[length];
        var buffer = new byte[1];
        for (var i = 0; i < length; i++)
        {
            byte b;
            do
            {
                RandomNumberGenerator.Fill(buffer);
                b = buffer[0];
            } while (b >= maxValidByte);

            chars[i] = alphabet[b % alphabet.Length];
        }
        return new string(chars);
    }
}
