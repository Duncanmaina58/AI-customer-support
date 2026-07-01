using Api.Application.Abstractions;
using Api.Contracts.Analytics;
using Api.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly ICurrentTenantProvider _tenant;

    public AnalyticsController(IAppDbContext db, ICurrentTenantProvider tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>
    /// Returns aggregate conversation counts and token-usage metrics for the
    /// current tenant. The global EF Core query filter guarantees all counts
    /// are scoped to this company only — no explicit CompanyId filter is needed
    /// on Conversations or Messages.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<AnalyticsSummaryDto>> GetSummary(CancellationToken ct)
    {
        // Aggregate by status in a single DB round-trip.
        var statusCounts = await _db.Conversations
            .AsNoTracking()
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var total = statusCounts.Sum(g => g.Count);
        var open = statusCounts.FirstOrDefault(g => g.Status == ConversationStatus.Open)?.Count ?? 0;
        var resolved = statusCounts.FirstOrDefault(g => g.Status == ConversationStatus.Resolved)?.Count ?? 0;
        var escalated = statusCounts.FirstOrDefault(g => g.Status == ConversationStatus.Escalated)?.Count ?? 0;
        var resolutionRate = total > 0 ? Math.Round((double)resolved / total * 100, 1) : 0.0;

        // Company is not tenant-scoped (it IS the tenant), so we filter explicitly.
        var companyId = _tenant.CompanyId;
        var company = companyId.HasValue
            ? await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == companyId.Value, ct)
            : null;

        return Ok(new AnalyticsSummaryDto(
            TotalConversations: total,
            OpenConversations: open,
            ResolvedConversations: resolved,
            EscalatedConversations: escalated,
            ResolutionRate: resolutionRate,
            TokensUsedThisMonth: company?.TokensUsedThisMonth ?? 0,
            MonthlyTokenBudget: company?.MonthlyTokenBudget ?? 0
        ));
    }
}
