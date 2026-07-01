using Api.Application.Abstractions;
using Api.Contracts.Companies;
using Api.Domain.Entities;
using Api.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

[ApiController]
[Route("api/companies")]
[Authorize]
public class CompaniesController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly ICurrentTenantProvider _currentTenant;

    public CompaniesController(IAppDbContext db, ICurrentTenantProvider currentTenant)
    {
        _db = db;
        _currentTenant = currentTenant;
    }

    /// <summary>Company details for the Settings page and the onboarding wizard.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<CompanyDetailsDto>> Me(CancellationToken ct)
    {
        // Company is the tenant ROOT, not an ITenantScoped entity (see
        // AppDbContext.OnModelCreating) — the global query filter does not apply to
        // it, so we filter by the current tenant's id explicitly here.
        var company = await _db.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == _currentTenant.CompanyId, ct);

        if (company is null)
        {
            return NotFound();
        }

        return Ok(ToDto(company));
    }

    /// <summary>
    /// Partial update of company settings. Doubles as the save action for onboarding
    /// wizard steps 1-3 (details, brand voice, business hours) — see
    /// UpdateCompanyRequest's doc comment for why this is one endpoint, not four.
    /// </summary>
    [HttpPatch("me")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<CompanyDetailsDto>> UpdateMe(UpdateCompanyRequest request, CancellationToken ct)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == _currentTenant.CompanyId, ct);
        if (company is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            company.Name = request.Name.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.TimeZone))
        {
            company.TimeZone = request.TimeZone.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.DefaultCurrency))
        {
            company.DefaultCurrency = request.DefaultCurrency.Trim().ToUpperInvariant();
        }
        if (!string.IsNullOrWhiteSpace(request.Industry))
        {
            company.Industry = request.Industry.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.LogoUrl))
        {
            company.LogoUrl = request.LogoUrl.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.PrimaryLanguage))
        {
            company.PrimaryLanguage = request.PrimaryLanguage.Trim().ToLowerInvariant();
        }
        if (request.BusinessHoursJson is not null)
        {
            company.BusinessHoursJson = request.BusinessHoursJson;
        }
        if (!string.IsNullOrWhiteSpace(request.BrandVoice))
        {
            if (!Enum.TryParse<BrandVoice>(request.BrandVoice, true, out var brandVoice))
            {
                return BadRequest(new { message = "BrandVoice must be 'Formal', 'Friendly', or 'Neutral'." });
            }
            company.BrandVoice = brandVoice;
        }

        company.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(company));
    }

    /// <summary>
    /// Onboarding wizard step 4 (final step): marks onboarding complete. Requires at
    /// least one active channel connection to exist first — that's the whole point
    /// of step 4 ("connect your first channel"), so this is where that's enforced
    /// rather than trusting the frontend alone.
    /// </summary>
    [HttpPost("me/onboarding/complete")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<CompanyDetailsDto>> CompleteOnboarding(CancellationToken ct)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == _currentTenant.CompanyId, ct);
        if (company is null)
        {
            return NotFound();
        }

        var hasActiveChannel = await _db.ChannelConnections
            .AnyAsync(cc => cc.Status == ChannelConnectionStatus.Active, ct);

        if (!hasActiveChannel)
        {
            return BadRequest(new { message = "Connect at least one channel before finishing onboarding." });
        }

        company.OnboardingCompletedAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(company));
    }

    private static CompanyDetailsDto ToDto(Company company) => new(
        company.Id,
        company.Name,
        company.Plan.ToString(),
        company.PublicApiKey,
        company.DefaultCurrency,
        company.TimeZone,
        company.Industry,
        company.LogoUrl,
        company.BrandVoice.ToString(),
        company.PrimaryLanguage,
        company.BusinessHoursJson,
        company.OnboardingCompletedAt,
        company.CreatedAt);
}
