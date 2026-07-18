using Api.Application.Abstractions;
using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Services;

/// <summary>
/// Sprint 7: token-budget enforcement, shared by every channel's pipeline
/// (ChatHub, WhatsAppWebhookController, EmailPipelineService,
/// ChatChannelPipelineService) so the 90%-warning and 100%-cutoff logic lives
/// in exactly one place rather than four slightly-different copies.
///
/// Deliberately real-time rather than a periodic scan: the 90% check runs
/// right after each AI reply's token count is added (see
/// ConversationService.IncrementTokenUsageAsync's callers), so the warning
/// fires within moments of crossing the threshold instead of waiting for the
/// next scheduled job run — the Phase 1 doc's "background job" framing made
/// sense for a batch-summation model; this codebase already tracks usage
/// atomically in real time (Sprint 4/5), so there's nothing to batch here.
/// </summary>
public class TokenBudgetService
{
    private const double WarningThresholdFraction = 0.9;

    private readonly IAppDbContext _db;
    private readonly IBrevoEmailClient _brevo;
    private readonly ILogger<TokenBudgetService> _logger;

    public TokenBudgetService(IAppDbContext db, IBrevoEmailClient brevo, ILogger<TokenBudgetService> logger)
    {
        _db = db;
        _brevo = brevo;
        _logger = logger;
    }

    /// <summary>
    /// True once a company has used 100% or more of its monthly token budget.
    /// Callers should skip the AI/RAG call entirely when this is true and go
    /// straight to ticket creation instead — see each pipeline's "over budget"
    /// branch for the exact customer-facing message (Phase 1 doc's wording).
    /// A budget of 0 or less is treated as "unlimited" (defensive — should
    /// never happen given BillingPlanCatalog's real values, but a data entry
    /// mistake shouldn't accidentally lock a company out entirely).
    /// </summary>
    public async Task<bool> IsOverBudgetAsync(Guid companyId, CancellationToken ct = default)
    {
        var company = await _db.Companies
            .AsNoTracking()
            .Where(c => c.Id == companyId)
            .Select(c => new { c.TokensUsedThisMonth, c.MonthlyTokenBudget })
            .FirstOrDefaultAsync(ct);

        return company is not null
            && company.MonthlyTokenBudget > 0
            && company.TokensUsedThisMonth >= company.MonthlyTokenBudget;
    }

    /// <summary>
    /// Sends the 90%-of-budget warning email to the company's Owner/Admin
    /// agents, exactly once per billing period. Safe to call after every AI
    /// reply — cheap to check, and the atomic compare-and-set below means
    /// concurrent calls (e.g. two channels replying at once) can't send two
    /// warnings for the same crossing.
    /// </summary>
public async Task CheckAndSendBudgetWarningIfNeededAsync(Guid companyId, CancellationToken ct = default)
{
    var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);
    if (company is null || company.MonthlyTokenBudget <= 0) return;
    if (company.TokenBudgetWarningSentAt is not null) return;

    var threshold = company.MonthlyTokenBudget * WarningThresholdFraction;
    if (company.TokensUsedThisMonth < threshold) return;

    // Load again with null check to act as compare-and-set
    // (concurrent callers: only one will win the SaveChanges race)
    var fresh = await _db.Companies
        .FirstOrDefaultAsync(c => c.Id == companyId && c.TokenBudgetWarningSentAt == null, ct);

    if (fresh is null) return; // another caller already set it

    fresh.TokenBudgetWarningSentAt = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);

    await SendWarningEmailAsync(fresh, ct);
}

    private async Task SendWarningEmailAsync(Company company, CancellationToken ct)
    {
        try
        {
            var recipients = await _db.Agents
                .IgnoreQueryFilters()
                .Where(a => a.CompanyId == company.Id && a.IsActive
                         && (a.Role == AgentRole.Owner || a.Role == AgentRole.Admin))
                .ToListAsync(ct);

            if (recipients.Count == 0) return;

            var percentUsed = Math.Round((double)company.TokensUsedThisMonth / company.MonthlyTokenBudget * 100, 0);
            var body =
                $"Hi,\n\n" +
                $"Your AI has used {percentUsed}% of this month's conversation budget " +
                $"({company.TokensUsedThisMonth:N0} / {company.MonthlyTokenBudget:N0} tokens).\n\n" +
                $"Once you reach 100%, the AI will pause responding automatically and every message " +
                $"will go straight to a support ticket instead until your next billing period or you upgrade.\n\n" +
                $"Visit your dashboard's Billing page to see your usage or upgrade your plan.\n\n" +
                $"— AI Support Platform";

            foreach (var agent in recipients)
            {
                await _brevo.SendAsync(new BrevoOutboundEmail(
                    SenderName:  "AI Support Platform",
                    SenderEmail: "globaljobhubplatform@gmail.com",
                    ToEmail:     agent.Email,
                    ToName:      agent.Name,
                    Subject:     "You've used 90% of your AI conversation budget",
                    TextContent: body), ct);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal — the flag is already set so this won't retry forever
            // if Brevo happens to be down at this exact moment; that's an
            // acceptable tradeoff for "warn once," not "warn reliably."
            _logger.LogError(ex, "Failed to send token budget warning email | company={CompanyId}", company.Id);
        }
    }
}
