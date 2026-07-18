using Api.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

/// <summary>
/// Sprint 6: sandbox mode. Every company gets a private test link
/// (platform.com/test/{sandboxToken}) that behaves exactly like their real web
/// chat widget, except conversations started through it are flagged
/// Conversation.IsSandbox — see ChatHub.ResolveCompanyAsync and
/// ChatHub.SendMessage for where that actually changes behavior (no token-budget
/// charge, no real tickets created).
/// </summary>
[ApiController]
[Route("api/sandbox")]
public class SandboxController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly ICurrentTenantProvider _tenant;

    public SandboxController(IAppDbContext db, ICurrentTenantProvider tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>Dashboard: the current company's sandbox test link, for the Sandbox page / onboarding Step 6.</summary>
    [HttpGet("info")]
    [Authorize]
    public async Task<ActionResult<SandboxInfoDto>> GetInfo(CancellationToken ct)
    {
        if (_tenant.CompanyId is not { } companyId)
            return Unauthorized();

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null) return NotFound();

        return Ok(new SandboxInfoDto(company.SandboxToken, $"/test/{company.SandboxToken}"));
    }

    /// <summary>
    /// Dashboard: rotates the sandbox token (e.g. if it was shared too widely).
    /// Old test links stop working immediately.
    /// </summary>
    [HttpPost("regenerate")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<SandboxInfoDto>> Regenerate(CancellationToken ct)
    {
        if (_tenant.CompanyId is not { } companyId)
            return Unauthorized();

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null) return NotFound();

        company.SandboxToken = $"sbx_{Guid.NewGuid():N}";
        await _db.SaveChangesAsync(ct);

        return Ok(new SandboxInfoDto(company.SandboxToken, $"/test/{company.SandboxToken}"));
    }

    /// <summary>
    /// Public (no auth) — the sandbox chat page (/test/{token}) uses this to
    /// show which company it's testing, and to fail fast with a clear "this
    /// test link isn't valid" message rather than a blank/broken chat if the
    /// token was rotated or mistyped. No sensitive data returned — just a name.
    /// </summary>
    [HttpGet("{token}/company")]
    [AllowAnonymous]
    public async Task<ActionResult<SandboxCompanyDto>> GetCompanyByToken(string token, CancellationToken ct)
    {
        var company = await _db.Companies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.SandboxToken == token, ct);

        if (company is null) return NotFound();

        return Ok(new SandboxCompanyDto(company.Name));
    }
}

public record SandboxInfoDto(string SandboxToken, string TestLinkPath);

public record SandboxCompanyDto(string CompanyName);
