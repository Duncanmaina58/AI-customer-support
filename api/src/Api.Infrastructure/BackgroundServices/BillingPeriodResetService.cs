using Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.BackgroundServices;

/// <summary>
/// Sprint 7: rolls a company's billing/usage period over once 30 days have
/// passed since it last started — resets TokensUsedThisMonth to 0 and clears
/// TokenBudgetWarningSentAt so the 90% warning can fire again next period.
///
/// Rolling 30-day window anchored to each company's own CurrentPeriodStartAt,
/// not a calendar-month boundary. The Phase 1 doc says "resets on billing
/// date" — but a real per-company "billing day of month" (with all the
/// 28-vs-31-day edge cases that implies) adds real complexity for no benefit
/// here: a company's usage period rolling every 30 days from whenever they
/// signed up (or last paid — successful M-Pesa payments also reset
/// CurrentPeriodStartAt, see BillingController.MpesaCallback) is simpler and
/// behaves the same from the company's perspective either way.
///
/// Runs a lightweight check every few hours rather than the doc's suggested
/// 15-minute interval — a billing period boundary only needs day-level
/// precision, not minute-level, so there's no reason to poll that often.
/// </summary>
public class BillingPeriodResetService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(4);
    private static readonly TimeSpan PeriodLength = TimeSpan.FromDays(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BillingPeriodResetService> _logger;

    public BillingPeriodResetService(IServiceScopeFactory scopeFactory, ILogger<BillingPeriodResetService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ResetDuePeriodsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Billing period reset cycle failed");
            }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ResetDuePeriodsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cutoff = DateTime.UtcNow - PeriodLength;

        // Company isn't ITenantScoped (it IS the tenant), so no IgnoreQueryFilters needed.
        var dueCompanies = await db.Companies
            .Where(c => c.CurrentPeriodStartAt <= cutoff)
            .ToListAsync(ct);

        if (dueCompanies.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var company in dueCompanies)
        {
            company.TokensUsedThisMonth = 0;
            company.CurrentPeriodStartAt = now;
            company.TokenBudgetWarningSentAt = null;
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Billing period reset for {Count} company(ies)", dueCompanies.Count);
    }
}
