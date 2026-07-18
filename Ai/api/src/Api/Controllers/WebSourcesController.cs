using Api.Application.Abstractions;
using Api.Contracts.Knowledge;
using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.AI;
using Api.Infrastructure.BackgroundServices;
using Api.Infrastructure.Crawling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

/// <summary>
/// Sprint 4 web crawling addition: lets a company connect its live website as
/// a knowledge source. Crawling itself runs off-thread via IWebCrawlQueue
/// (see WebCrawlQueueService) — every mutating action here returns as soon as
/// the intent is persisted; the dashboard polls GET .../status for progress.
///
/// Web-sourced chunks land in the exact same KnowledgeChunks table as manual
/// entries (KnowledgeIngestionService), so RagService needs zero changes.
/// </summary>
[ApiController]
[Authorize]
[Route("api/knowledge/web-sources")]
public class WebSourcesController : ControllerBase
{
    private const int HardMaxCrawlDepth = 5;
    private const int HardMaxPagesCeiling = 1000;

    private readonly IAppDbContext _db;
    private readonly ICurrentTenantProvider _tenant;
    private readonly IWebCrawlQueue _crawlQueue;
    private readonly KnowledgeIngestionService _ingestion;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<WebSourcesController> _logger;

    public WebSourcesController(
        IAppDbContext db,
        ICurrentTenantProvider tenant,
        IWebCrawlQueue crawlQueue,
        KnowledgeIngestionService ingestion,
        IHttpClientFactory httpFactory,
        ILogger<WebSourcesController> logger)
    {
        _db          = db;
        _tenant      = tenant;
        _crawlQueue  = crawlQueue;
        _ingestion   = ingestion;
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    // -------------------------------------------------------------------
    // 1. GET /api/knowledge/web-sources
    // -------------------------------------------------------------------
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WebSourceDto>>> List(CancellationToken ct)
    {
        var sources = await _db.WebSources
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        return Ok(sources.Select(ToDto));
    }

    // -------------------------------------------------------------------
    // 2. POST /api/knowledge/web-sources
    // -------------------------------------------------------------------
    [HttpPost]
    [Authorize(Roles = "Owner,Admin,Agent")]
    public async Task<ActionResult<WebSourceDto>> Create(
        [FromBody] CreateWebSourceRequest request, CancellationToken ct)
    {
        if (_tenant.CompanyId is not { } companyId)
            return Unauthorized(new { message = "No company context." });

        if (string.IsNullOrWhiteSpace(request.Url) || !Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var parsedUrl)
            || (parsedUrl.Scheme != Uri.UriSchemeHttp && parsedUrl.Scheme != Uri.UriSchemeHttps))
            return BadRequest(new { message = "A valid URL starting with http:// or https:// is required." });

        if (!TryParseCrawlMode(request.CrawlMode, out var crawlMode))
            return BadRequest(new { message = "crawlMode must be one of: full_site, single_page, sitemap." });

        if (!TryParseMonitoringMode(request.MonitoringMode, out var monitoringMode))
            return BadRequest(new { message = "monitoringMode must be one of: adaptive, fixed, manual." });

        if (monitoringMode == WebSourceMonitoringMode.Fixed && (request.FixedIntervalHours is null or < 1))
            return BadRequest(new { message = "fixedIntervalHours is required (>= 1) when monitoringMode is 'fixed'." });

        var normalizedUrl = HtmlContentExtractor.NormalizeUrl(parsedUrl);

        var alreadyExists = await _db.WebSources.AnyAsync(
            s => s.CompanyId == companyId && s.Url == normalizedUrl, ct);
        if (alreadyExists)
            return Conflict(new { message = "This website is already connected as a knowledge source. Delete it first or use Re-crawl Now to refresh it." });

        // "HEAD request on input to verify reachable before saving" — dashboard spec 16.11.
        // Best-effort only: a failed HEAD (some servers block HEAD entirely) falls back to
        // a quick GET; total unreachability is what actually blocks saving.
        if (!await IsReachableAsync(parsedUrl, ct))
            return BadRequest(new { message = "Could not reach this website. Check the URL is correct and publicly accessible, then try again." });

        var source = new WebSource
        {
            CompanyId          = companyId,
            Url                = normalizedUrl,
            CrawlMode          = crawlMode,
            CrawlDepth         = Math.Clamp(request.CrawlDepth <= 0 ? 3 : request.CrawlDepth, 1, HardMaxCrawlDepth),
            IncludePattern     = string.IsNullOrWhiteSpace(request.IncludePattern) ? null : request.IncludePattern.Trim(),
            ExcludePattern     = string.IsNullOrWhiteSpace(request.ExcludePattern) ? null : request.ExcludePattern.Trim(),
            MaxPages           = Math.Clamp(request.MaxPages <= 0 ? 200 : request.MaxPages, 1, HardMaxPagesCeiling),
            Status             = WebSourceStatus.Pending,
            MonitoringMode     = monitoringMode,
            FixedIntervalHours = monitoringMode == WebSourceMonitoringMode.Fixed ? request.FixedIntervalHours : null,
            NotifyOnChange     = request.NotifyOnChange,
        };

        if (!TryCompileRegex(source.IncludePattern, out var includeError))
            return BadRequest(new { message = $"Invalid includePattern regex: {includeError}" });
        if (!TryCompileRegex(source.ExcludePattern, out var excludeError))
            return BadRequest(new { message = $"Invalid excludePattern regex: {excludeError}" });

        _db.WebSources.Add(source);
        await _db.SaveChangesAsync(ct);

        _crawlQueue.Enqueue(source.Id);

        _logger.LogInformation(
            "Web source created and crawl enqueued | company={CompanyId} id={Id} url={Url}",
            companyId, source.Id, source.Url);

        return CreatedAtAction(nameof(GetStatus), new { id = source.Id }, ToDto(source));
    }

    // -------------------------------------------------------------------
    // 3. DELETE /api/knowledge/web-sources/{id}
    // -------------------------------------------------------------------
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (_tenant.CompanyId is not { } companyId)
            return Unauthorized(new { message = "No company context." });

        var source = await _db.WebSources.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (source is null) return NotFound();

        await _ingestion.DeleteChunksForSourceAsync(companyId, id, ct);

        _db.WebSources.Remove(source); // cascades to WebPages via the FK's DeleteBehavior.Cascade

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Web source deleted | company={CompanyId} id={Id} url={Url}", companyId, id, source.Url);
        return NoContent();
    }

    // -------------------------------------------------------------------
    // 4. GET /api/knowledge/web-sources/{id}/status
    // -------------------------------------------------------------------
    [HttpGet("{id:guid}/status")]
    public async Task<ActionResult<WebSourceStatusDto>> GetStatus(Guid id, CancellationToken ct)
    {
        var source = await _db.WebSources.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (source is null) return NotFound();

        return Ok(new WebSourceStatusDto(
            source.Id,
            source.Status.ToString(),
            source.PagesCrawled,
            source.EstimatedTotalPages,
            source.CurrentCrawlUrl,
            source.ErrorMessage));
    }

    // -------------------------------------------------------------------
    // 5. GET /api/knowledge/web-sources/{id}/pages
    // -------------------------------------------------------------------
    [HttpGet("{id:guid}/pages")]
    public async Task<ActionResult<IReadOnlyList<WebPageDto>>> GetPages(Guid id, CancellationToken ct)
    {
        var exists = await _db.WebSources.AnyAsync(s => s.Id == id, ct);
        if (!exists) return NotFound();

        var pages = await _db.WebPages
            .AsNoTracking()
            .Where(p => p.WebSourceId == id)
            .OrderBy(p => p.Url)
            .Select(p => new WebPageDto(
                p.Id, p.Url, p.Title, p.Status.ToString(),
                p.CheckCount, p.ChangeCount, p.LastCheckedAt, p.LastChangedAt,
                p.NextCheckAt, p.ContentLength))
            .ToListAsync(ct);

        return Ok(pages);
    }

    // -------------------------------------------------------------------
    // 6. GET /api/knowledge/web-sources/{id}/changes?days=30
    // -------------------------------------------------------------------
    [HttpGet("{id:guid}/changes")]
    public async Task<ActionResult<IReadOnlyList<WebPageChangeDto>>> GetChanges(
        Guid id, [FromQuery] int days, CancellationToken ct)
    {
        var exists = await _db.WebSources.AnyAsync(s => s.Id == id, ct);
        if (!exists) return NotFound();

        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days <= 0 ? 30 : days, 1, 365));

        // Change history is derived from each WebPage's own change fields rather than a
        // separate event-log table: LastChangedAt/ChangeCount already capture "did this
        // page change and when" for the adaptive scheduler, and re-using them here keeps
        // the schema minimal. This gives the most-recent event per page, which is what
        // the dashboard's "N pages updated" summary needs.
        var changed = await _db.WebPages
            .AsNoTracking()
            .Where(p => p.WebSourceId == id && p.LastChangedAt != null && p.LastChangedAt >= since)
            .Select(p => new WebPageChangeDto(p.Url, "changed", p.LastChangedAt!.Value))
            .ToListAsync(ct);

        var removed = await _db.WebPages
            .AsNoTracking()
            .Where(p => p.WebSourceId == id && p.Status == WebPageStatus.Removed && p.LastCheckedAt >= since)
            .Select(p => new WebPageChangeDto(p.Url, "removed", p.LastCheckedAt!.Value))
            .ToListAsync(ct);

        var all = changed.Concat(removed).OrderByDescending(c => c.DetectedAt).ToList();
        return Ok(all);
    }

    // -------------------------------------------------------------------
    // 7. POST /api/knowledge/web-sources/{id}/check-now
    // -------------------------------------------------------------------
    [HttpPost("{id:guid}/check-now")]
    [Authorize(Roles = "Owner,Admin,Agent")]
    public async Task<IActionResult> CheckNow(Guid id, CancellationToken ct)
    {
        var source = await _db.WebSources.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (source is null) return NotFound();

        // Force every Active page's NextCheckAt into the past so the very next
        // ContentFreshnessBackgroundService sweep (within IntervalMinutes) picks
        // them all up immediately, bypassing the adaptive schedule.
        var pages = await _db.WebPages
            .Where(p => p.WebSourceId == id && p.Status == WebPageStatus.Active)
            .ToListAsync(ct);

        foreach (var page in pages)
            page.NextCheckAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Check-now requested | source={Id} pages={Count}", id, pages.Count);
        return Accepted(new { message = $"{pages.Count} page(s) queued for an immediate check.", pagesQueued = pages.Count });
    }

    // -------------------------------------------------------------------
    // 8. POST /api/knowledge/web-sources/{id}/recrawl
    // -------------------------------------------------------------------
    [HttpPost("{id:guid}/recrawl")]
    [Authorize(Roles = "Owner,Admin,Agent")]
    public async Task<IActionResult> Recrawl(Guid id, CancellationToken ct)
    {
        if (_tenant.CompanyId is not { } companyId)
            return Unauthorized(new { message = "No company context." });

        var source = await _db.WebSources.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (source is null) return NotFound();

        if (source.Status == WebSourceStatus.Crawling)
            return Conflict(new { message = "This source is already being crawled." });

        // Full re-crawl: wipe every chunk and page for this source, then re-run the
        // pipeline from scratch — spec 16.9's "delete all chunks, crawl everything again".
        await _ingestion.DeleteChunksForSourceAsync(companyId, id, ct);
        var pages = await _db.WebPages.Where(p => p.WebSourceId == id).ToListAsync(ct);
        _db.WebPages.RemoveRange(pages);

        source.Status = WebSourceStatus.Pending;
        source.PagesCrawled = 0;
        source.ChunksCreated = 0;
        source.ErrorMessage = null;

        await _db.SaveChangesAsync(ct);

        _crawlQueue.Enqueue(id);

        return Accepted(new { message = "Full re-crawl started." });
    }

    // -------------------------------------------------------------------
    // 9. PATCH /api/knowledge/web-sources/{id}/monitoring
    // -------------------------------------------------------------------
    [HttpPatch("{id:guid}/monitoring")]
    [Authorize(Roles = "Owner,Admin,Agent")]
    public async Task<ActionResult<WebSourceDto>> UpdateMonitoring(
        Guid id, [FromBody] UpdateMonitoringRequest request, CancellationToken ct)
    {
        var source = await _db.WebSources.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (source is null) return NotFound();

        if (!TryParseMonitoringMode(request.MonitoringMode, out var mode))
            return BadRequest(new { message = "monitoringMode must be one of: adaptive, fixed, manual." });

        if (mode == WebSourceMonitoringMode.Fixed && (request.FixedIntervalHours is null or < 1))
            return BadRequest(new { message = "fixedIntervalHours is required (>= 1) when monitoringMode is 'fixed'." });

        source.MonitoringMode     = mode;
        source.FixedIntervalHours = mode == WebSourceMonitoringMode.Fixed ? request.FixedIntervalHours : null;
        source.NotifyOnChange     = request.NotifyOnChange;

        // Unpausing via this endpoint isn't implied — Paused is only cleared by
        // the dedicated Resume action below (pause/resume share the same field).
        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(source));
    }

    // -------------------------------------------------------------------
    // 10. POST /api/knowledge/web-sources/{id}/pause
    //     (a matching /resume is included as the natural inverse action —
    //     not separately enumerated in the spec's endpoint table, but required
    //     for "Pause monitoring" to be reversible from the dashboard.)
    // -------------------------------------------------------------------
    [HttpPost("{id:guid}/pause")]
    [Authorize(Roles = "Owner,Admin,Agent")]
    public async Task<ActionResult<WebSourceDto>> Pause(Guid id, CancellationToken ct)
    {
        var source = await _db.WebSources.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (source is null) return NotFound();

        if (source.Status is WebSourceStatus.Pending or WebSourceStatus.Crawling)
            return Conflict(new { message = "Can't pause a source that hasn't finished its first crawl yet." });

        source.Status = WebSourceStatus.Paused;
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(source));
    }

    [HttpPost("{id:guid}/resume")]
    [Authorize(Roles = "Owner,Admin,Agent")]
    public async Task<ActionResult<WebSourceDto>> Resume(Guid id, CancellationToken ct)
    {
        var source = await _db.WebSources.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (source is null) return NotFound();

        if (source.Status != WebSourceStatus.Paused)
            return Conflict(new { message = "This source isn't paused." });

        source.Status = WebSourceStatus.Indexed;
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(source));
    }

    // -------------------------------------------------------------------
    // Extra: POST /api/knowledge/web-sources/{id}/cancel
    //     Not one of the 10 REST endpoints in the spec's own endpoint table,
    //     but the Dashboard UI spec (16.11) explicitly calls for a "Cancel
    //     button" on the live crawl-progress view, so it needs a home somewhere.
    // -------------------------------------------------------------------
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Owner,Admin,Agent")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var source = await _db.WebSources.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (source is null) return NotFound();

        if (source.Status != WebSourceStatus.Crawling)
            return Conflict(new { message = "This source isn't currently crawling." });

        var cancelled = _crawlQueue.Cancel(id);
        if (!cancelled)
            return Conflict(new { message = "No active crawl found for this source — it may have just finished." });

        return Accepted(new { message = "Cancelling…" });
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private async Task<bool> IsReachableAsync(Uri url, CancellationToken ct)
    {
        try
        {
            var http = _httpFactory.CreateClient(WebCrawlHttpClients.CrawlerClientName);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

            using var headResponse = await http.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, url), timeoutCts.Token);
            return true; // any HTTP response at all (even 4xx/5xx) means the host is reachable
        }
        catch
        {
            try
            {
                var http = _httpFactory.CreateClient(WebCrawlHttpClients.CrawlerClientName);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));
                using var getResponse = await http.SendAsync(
                    new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private static bool TryCompileRegex(string? pattern, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrEmpty(pattern)) return true;
        try
        {
            _ = new System.Text.RegularExpressions.Regex(pattern);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryParseCrawlMode(string? value, out WebCrawlMode mode)
    {
        mode = WebCrawlMode.FullSite;
        var normalized = (value ?? "full_site").Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        foreach (var candidate in Enum.GetValues<WebCrawlMode>())
        {
            if (string.Equals(candidate.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                mode = candidate;
                return true;
            }
        }
        return false;
    }

    private static bool TryParseMonitoringMode(string? value, out WebSourceMonitoringMode mode)
    {
        mode = WebSourceMonitoringMode.Adaptive;
        var normalized = (value ?? "adaptive").Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        foreach (var candidate in Enum.GetValues<WebSourceMonitoringMode>())
        {
            if (string.Equals(candidate.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                mode = candidate;
                return true;
            }
        }
        return false;
    }

    private static WebSourceDto ToDto(WebSource s) => new(
        s.Id, s.Url, s.CrawlMode.ToString(), s.CrawlDepth, s.IncludePattern, s.ExcludePattern,
        s.MaxPages, s.Status.ToString(), s.PagesCrawled, s.ChunksCreated,
        s.MonitoringMode.ToString(), s.FixedIntervalHours, s.NotifyOnChange,
        s.LastCrawledAt, s.ErrorMessage, s.HasJsRenderedPagesWarning, s.MaxPagesReached, s.CreatedAt);
}
