using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.AI;
using Api.Infrastructure.Channels;
using Api.Infrastructure.Crawling;
using Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.BackgroundServices;

/// <summary>
/// Sprint 4 web crawling, spec 16.7 Job 2 (ContentFreshnessJob): the direct
/// BackgroundService equivalent of the spec's Hangfire recurring job — this
/// codebase has no Hangfire, so a timed loop (same shape as ImapPollingService)
/// is the idiomatic replacement. Default interval is 5 minutes, matching the
/// spec's "*/5 * * * *" cron; configurable via ContentFreshness:IntervalMinutes.
///
/// Each sweep: pulls up to 100 due, Active pages (oldest NextCheckAt first)
/// whose WebSource isn't Paused, runs the three-tier change cascade on each,
/// applies surgical re-indexing on change, marks 404s Removed, reschedules
/// every checked page via AdaptiveCheckScheduler, and emails Owner/Admin
/// agents a per-company change summary when NotifyOnChange is enabled.
/// </summary>
public class ContentFreshnessService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ContentFreshnessService> _logger;
    private readonly TimeSpan _interval;

    public ContentFreshnessService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ContentFreshnessService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;

        var minutes = configuration.GetValue<int?>("ContentFreshness:IntervalMinutes") ?? 5;
        _interval = TimeSpan.FromMinutes(Math.Max(minutes, 1));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Content freshness sweep failed");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunSweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db         = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var detector   = scope.ServiceProvider.GetRequiredService<IWebPageChangeDetector>();
        var scheduler  = scope.ServiceProvider.GetRequiredService<IAdaptiveCheckScheduler>();
        var ingestion  = scope.ServiceProvider.GetRequiredService<KnowledgeIngestionService>();
        var email      = scope.ServiceProvider.GetRequiredService<IBrevoEmailClient>();

        var now = DateTime.UtcNow;

        var due = await db.WebPages
            .IgnoreQueryFilters()
            .Where(p => p.Status == WebPageStatus.Active
                     && p.NextCheckAt <= now
                     && p.WebSource!.Status != WebSourceStatus.Paused)
            .OrderBy(p => p.NextCheckAt)
            .Take(100)
            .ToListAsync(ct);

        if (due.Count == 0) return;

        // companyId -> list of (url, changeType) for the end-of-sweep notification email.
        var changesByCompany = new Dictionary<Guid, List<(string Url, string ChangeType)>>();
        var sourcesTouched = new HashSet<Guid>();

        foreach (var page in due)
        {
            ChangeCheckResult result;
            try
            {
                result = await detector.CheckPageAsync(page, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Change check failed for {Url} — rescheduling, no state change", page.Url);
                page.LastCheckedAt = now;
                page.CheckCount++;
                page.NextCheckAt = now.Add(TimeSpan.FromHours(12));
                continue;
            }

            var source = await db.WebSources.IgnoreQueryFilters().FirstAsync(s => s.Id == page.WebSourceId, ct);
            sourcesTouched.Add(source.Id);

            switch (result.Outcome)
            {
                case ChangeCheckOutcome.Removed:
                    await ingestion.DeleteChunksForUrlAsync(page.CompanyId, page.Url, ct);
                    page.Status = WebPageStatus.Removed;
                    page.LastCheckedAt = now;
                    page.CheckCount++;
                    RecordChange(changesByCompany, source, page.Url, "removed");
                    break;

                case ChangeCheckOutcome.Changed:
                    await ingestion.IngestWebPageAsync(
                        page.CompanyId, page.WebSourceId, page.Url,
                        result.NewTitle ?? page.Title ?? page.Url, result.NewText!, ct);

                    page.Title         = result.NewTitle ?? page.Title;
                    page.ContentHash   = result.NewHash!;
                    page.ContentLength = result.NewText!.Length;
                    page.HttpETag      = result.NewETag ?? page.HttpETag;
                    page.HttpLastModified = result.NewLastModified ?? page.HttpLastModified;
                    page.ChangeCount++;
                    page.LastChangedAt = now;
                    page.LastCheckedAt = now;
                    page.CheckCount++;
                    page.NextCheckAt = now.Add(scheduler.CalculateNextCheckInterval(page, source));
                    RecordChange(changesByCompany, source, page.Url, "updated");
                    break;

                case ChangeCheckOutcome.Unchanged:
                default:
                    page.LastCheckedAt = now;
                    page.CheckCount++;
                    page.NextCheckAt = now.Add(scheduler.CalculateNextCheckInterval(page, source));
                    break;
            }
        }

        // Refresh each touched source's ChunksCreated counter now that the sweep is done.
        foreach (var sourceId in sourcesTouched)
        {
            var source = await db.WebSources.IgnoreQueryFilters().FirstAsync(s => s.Id == sourceId, ct);
            source.ChunksCreated = await db.KnowledgeChunks.IgnoreQueryFilters()
                .CountAsync(c => c.WebSourceId == sourceId, ct);
        }

        await db.SaveChangesAsync(ct);

        if (changesByCompany.Count > 0)
            await NotifyCompaniesAsync(db, email, changesByCompany, ct);

        _logger.LogInformation(
            "Content freshness sweep: checked {Count} pages, {Changed} companies had changes",
            due.Count, changesByCompany.Count);
    }

    private static void RecordChange(
        Dictionary<Guid, List<(string Url, string ChangeType)>> changesByCompany,
        WebSource source, string url, string changeType)
    {
        if (!source.NotifyOnChange) return;

        if (!changesByCompany.TryGetValue(source.CompanyId, out var list))
        {
            list = [];
            changesByCompany[source.CompanyId] = list;
        }
        list.Add((url, changeType));
    }

    private async Task NotifyCompaniesAsync(
        AppDbContext db,
        IBrevoEmailClient email,
        Dictionary<Guid, List<(string Url, string ChangeType)>> changesByCompany,
        CancellationToken ct)
    {
        foreach (var (companyId, changes) in changesByCompany)
        {
            try
            {
                var recipients = await db.Agents.IgnoreQueryFilters()
                    .Where(a => a.CompanyId == companyId && a.IsActive
                             && (a.Role == AgentRole.Owner || a.Role == AgentRole.Admin))
                    .ToListAsync(ct);

                if (recipients.Count == 0) continue;

                var body = BuildChangeSummaryEmail(changes);

                foreach (var agent in recipients)
                {
                    await email.SendAsync(new BrevoOutboundEmail(
                        SenderName:  "AI Support Platform",
                        SenderEmail: "globaljobhubplatform@gmail.com",
                        ToEmail:     agent.Email,
                        ToName:      agent.Name,
                        Subject:     $"Your knowledge base website content changed ({changes.Count} page{(changes.Count == 1 ? "" : "s")})",
                        TextContent: body), ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send content-freshness notification for company {CompanyId}", companyId);
            }
        }
    }

    private static string BuildChangeSummaryEmail(List<(string Url, string ChangeType)> changes)
    {
        var lines = changes.Take(20).Select(c => $"  - [{c.ChangeType}] {c.Url}");
        var more = changes.Count > 20 ? $"\n  ...and {changes.Count - 20} more." : "";

        return $"""
            Your connected website's content was just updated and the AI has automatically re-indexed it.

            Changes detected:
            {string.Join('\n', lines)}{more}

            No action is needed — your AI is already answering from the latest content.
            View the full change log in Knowledge Base > Web Sources on your dashboard.

            ---
            AI Support Platform
            """;
    }
}
