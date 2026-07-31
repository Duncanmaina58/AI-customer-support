using System.Net;
using System.Text.RegularExpressions;
using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.AI;
using Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Crawling;

public interface IWebCrawlerService
{
    /// <summary>
    /// Runs the complete crawl pipeline for a WebSource: robots.txt compliance,
    /// sitemap detection (fast path) with BFS link-following fallback, per-page
    /// extraction/hashing/ingestion, and WebPage/WebSource bookkeeping.
    ///
    /// Idempotent-ish: re-running it (e.g. via the "Re-crawl Now" action) is safe —
    /// pages that still exist are surgically re-indexed only if their content
    /// actually changed, and pages that no longer appear anywhere in this run are
    /// marked Removed with their chunks deleted.
    /// </summary>
    Task CrawlAsync(Guid webSourceId, CancellationToken ct = default);
}

/// <summary>
/// Sprint 4 web crawling, spec 16.6 — the full crawl pipeline, step by step.
/// Runs off the request thread via WebCrawlQueueService (a Channel-backed
/// background task queue — this codebase has no Hangfire, so a hosted-service
/// queue is the direct equivalent for "enqueue a job, run it off-thread").
/// </summary>
public class WebCrawlerService : IWebCrawlerService
{
    /// <summary>Polite delay between page fetches, per spec 16.6 step 4.</summary>
    private static readonly TimeSpan RequestDelay = TimeSpan.FromMilliseconds(500);

    private const int HardMaxCrawlDepth = 5;

    private readonly AppDbContext _db;
    private readonly HttpClient _http;
    private readonly IRobotsTxtChecker _robots;
    private readonly ISitemapService _sitemap;
    private readonly IHtmlContentExtractor _extractor;
    private readonly KnowledgeIngestionService _ingestion;
    private readonly IAdaptiveCheckScheduler _scheduler;
    private readonly ILogger<WebCrawlerService> _logger;

    public WebCrawlerService(
        AppDbContext db,
        IHttpClientFactory httpFactory,
        IRobotsTxtChecker robots,
        ISitemapService sitemap,
        IHtmlContentExtractor extractor,
        KnowledgeIngestionService ingestion,
        IAdaptiveCheckScheduler scheduler,
        ILogger<WebCrawlerService> logger)
    {
        _db         = db;
        _http       = httpFactory.CreateClient(WebCrawlHttpClients.CrawlerClientName);
        _robots     = robots;
        _sitemap    = sitemap;
        _extractor  = extractor;
        _ingestion  = ingestion;
        _scheduler  = scheduler;
        _logger     = logger;
    }

    public async Task CrawlAsync(Guid webSourceId, CancellationToken ct = default)
    {
        var source = await _db.WebSources.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == webSourceId, ct);

        if (source is null)
        {
            _logger.LogWarning("WebCrawlerService: WebSource {Id} not found — job skipped", webSourceId);
            return;
        }

        source.Status          = WebSourceStatus.Crawling;
        source.ErrorMessage    = null;
        source.CurrentCrawlUrl = source.Url;
        source.PagesCrawled    = 0;
        source.ChunksCreated   = 0;
        source.MaxPagesReached = false;
        source.HasJsRenderedPagesWarning = false;
        await _db.SaveChangesAsync(ct);

        Uri siteRoot;
        try
        {
            siteRoot = new Uri(source.Url);
        }
        catch (Exception ex)
        {
            await FailAsync(source, $"Invalid URL: {ex.Message}", ct);
            return;
        }

        // ---- Step 2: robots.txt compliance — before touching a single page ----
        var robots = await _robots.FetchAsync(siteRoot, ct);
        if (robots.EntireSiteDisallowed)
        {
            await FailAsync(source,
                "robots.txt disallows crawling this site (Disallow: /). The site owner must permit crawling before it can be added as a knowledge source.",
                ct);
            return;
        }

        var includeRegex = CompileOrNull(source.IncludePattern);
        var excludeRegex = CompileOrNull(source.ExcludePattern);

        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashesThisRun = new HashSet<string>(StringComparer.Ordinal);
        var maxPages = Math.Max(1, source.MaxPages);
        var consecutive429s = 0;
        var rateLimited = false;

        try
        {
            if (source.CrawlMode == WebCrawlMode.SinglePage)
            {
                var url = HtmlContentExtractor.NormalizeUrl(siteRoot);
                if (IsAllowedByPatterns(new Uri(url).AbsolutePath, includeRegex, excludeRegex)
                    && robots.IsAllowed(new Uri(url).PathAndQuery))
                {
                    await ProcessPageAsync(source, url, seenUrls, hashesThisRun, ct);
                }
            }
            else
            {
                // ---- Step 3: sitemap detection — fast path ----
                var sitemapUrls = source.CrawlMode == WebCrawlMode.Sitemap || source.CrawlMode == WebCrawlMode.FullSite
                    ? await _sitemap.DiscoverUrlsAsync(siteRoot, robots.SitemapUrls, ct)
                    : Array.Empty<string>();

                source.EstimatedTotalPages = sitemapUrls.Count > 0
                    ? Math.Min(sitemapUrls.Count, maxPages)
                    : null;
                await _db.SaveChangesAsync(ct);

                if (sitemapUrls.Count > 0)
                {
                    foreach (var rawUrl in sitemapUrls)
                    {
                        if (seenUrls.Count >= maxPages) { source.MaxPagesReached = true; break; }
                        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var pageUri)) continue;
                        if (!string.Equals(pageUri.Host, siteRoot.Host, StringComparison.OrdinalIgnoreCase)) continue;

                        var normalized = HtmlContentExtractor.NormalizeUrl(pageUri);
                        if (!IsAllowedByPatterns(pageUri.AbsolutePath, includeRegex, excludeRegex)) continue;
                        if (!robots.IsAllowed(pageUri.PathAndQuery)) continue;

                        await ProcessPageAsync(source, normalized, seenUrls, hashesThisRun, ct,
                            onRateLimited: () => consecutive429s++, onSuccess: () => consecutive429s = 0);
                        if (consecutive429s >= 3) { rateLimited = true; break; }
                    }
                }
                else
                {
                    // ---- Step 4: BFS link crawl (no sitemap found) ----
                    var maxDepth = Math.Min(Math.Max(1, source.CrawlDepth), HardMaxCrawlDepth);
                    var frontier = new Queue<(string Url, int Depth)>();
                    frontier.Enqueue((HtmlContentExtractor.NormalizeUrl(siteRoot), 0));
                    var queued = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { HtmlContentExtractor.NormalizeUrl(siteRoot) };

                    while (frontier.Count > 0 && seenUrls.Count < maxPages)
                    {
                        var (url, depth) = frontier.Dequeue();

                        if (!Uri.TryCreate(url, UriKind.Absolute, out var pageUri)) continue;
                        if (!IsAllowedByPatterns(pageUri.AbsolutePath, includeRegex, excludeRegex)) continue;
                        if (!robots.IsAllowed(pageUri.PathAndQuery)) continue;

                        var links = await ProcessPageAsync(
                            source, url, seenUrls, hashesThisRun, ct,
                            onRateLimited: () => consecutive429s++, onSuccess: () => consecutive429s = 0,
                            returnLinks: true);

                        if (consecutive429s >= 3) { rateLimited = true; break; }

                        if (links is null || depth >= maxDepth) continue;

                        foreach (var link in links)
                        {
                            if (queued.Count >= maxPages * 4) break; // frontier safety valve on very link-dense sites
                            if (queued.Add(link)) frontier.Enqueue((link, depth + 1));
                        }
                    }

                    if (frontier.Count > 0) source.MaxPagesReached = true;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await FailAsync(source, "Crawl was cancelled.", CancellationToken.None);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Crawl failed unexpectedly for WebSource {Id} ({Url})", source.Id, source.Url);
            await FailAsync(source, $"Crawl failed: {ex.Message}", ct);
            return;
        }

        // ---- Pages that used to exist but weren't seen on this run are gone ----
        var vanished = await _db.WebPages.IgnoreQueryFilters()
            .Where(p => p.WebSourceId == source.Id && p.Status == WebPageStatus.Active)
            .ToListAsync(ct);

        foreach (var page in vanished.Where(p => !seenUrls.Contains(p.Url)))
        {
            await _ingestion.DeleteChunksForUrlAsync(source.CompanyId, page.Url, ct);
            page.Status = WebPageStatus.Removed;
            page.LastCheckedAt = DateTime.UtcNow;
        }

        source.PagesCrawled    = seenUrls.Count;
        source.ChunksCreated   = await _db.KnowledgeChunks.IgnoreQueryFilters()
            .CountAsync(c => c.CompanyId == source.CompanyId && c.WebSourceId == source.Id, ct);
        source.LastCrawledAt   = DateTime.UtcNow;
        source.CurrentCrawlUrl = null;

        if (rateLimited)
        {
            source.Status = WebSourceStatus.Error;
            source.ErrorMessage = "The site returned repeated 429 (rate limited) responses. Crawling was paused — try 'Re-crawl Now' again shortly.";
        }
        else
        {
            source.Status = WebSourceStatus.Indexed;
            source.ErrorMessage = null;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Crawl finished | source={Id} url={Url} status={Status} pages={Pages} chunks={Chunks}",
            source.Id, source.Url, source.Status, source.PagesCrawled, source.ChunksCreated);
    }

    /// <summary>
    /// Fetches, extracts, hashes, ingests, and upserts the WebPage row for a
    /// single URL. Returns the page's internal links when <paramref name="returnLinks"/>
    /// is true (BFS mode) so the caller can grow the frontier, or null otherwise/on failure.
    /// </summary>
    private async Task<IReadOnlyList<string>?> ProcessPageAsync(
        WebSource source,
        string url,
        HashSet<string> seenUrls,
        HashSet<string> hashesThisRun,
        CancellationToken ct,
        Action? onRateLimited = null,
        Action? onSuccess = null,
        bool returnLinks = false)
    {
        if (!seenUrls.Add(url)) return null;

        source.CurrentCrawlUrl = url;
        source.PagesCrawled = seenUrls.Count;
        await _db.SaveChangesAsync(ct);

        await Task.Delay(RequestDelay, ct);

        HttpResponseMessage response;
        var attempt = 0;
        var backoffs = new[] { 5, 10, 20 }; // seconds — spec 16.10 "Server rate limiting (429)"

        while (true)
        {
            try
            {
                response = await _http.GetAsync(url, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Fetch failed for {Url}", url);
                return null;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < backoffs.Length)
            {
                response.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(backoffs[attempt]), ct);
                attempt++;
                continue;
            }

            break;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                onRateLimited?.Invoke();
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Non-success {Status} fetching {Url}", response.StatusCode, url);
                return null;
            }

            onSuccess?.Invoke();

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is not null && !contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
                return null; // Step 4: "Skip non-HTML"

            var html = await response.Content.ReadAsStringAsync(ct);
            var extracted = _extractor.Extract(html, new Uri(url));

            if (extracted.Text.Length < HtmlContentExtractor.JsRenderedWarningThreshold)
                source.HasJsRenderedPagesWarning = true;

            var hash = WebPageChangeDetector.ComputeHash(extracted.Text);

            var existing = await _db.WebPages.IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.WebSourceId == source.Id && p.Url == url, ct);

            var isDuplicateContent = extracted.Text.Length > 0 && !hashesThisRun.Add(hash)
                || (existing is not null && existing.ContentHash == hash);

            var chunksCreated = 0;
            if (!isDuplicateContent && extracted.Text.Length > 0)
            {
                chunksCreated = await _ingestion.IngestWebPageAsync(
                    source.CompanyId, source.Id, url, extracted.Title, extracted.Text, ct);
            }

            var etag = response.Headers.ETag?.Tag;
            var lastModified = response.Content.Headers.LastModified?.UtcDateTime;
            var now = DateTime.UtcNow;

            if (existing is null)
            {
                existing = new WebPage
                {
                    WebSourceId = source.Id,
                    CompanyId   = source.CompanyId,
                    Url         = url,
                    Title       = extracted.Title,
                    ContentHash = hash,
                    ContentLength = extracted.Text.Length,
                    HttpETag    = etag,
                    HttpLastModified = lastModified,
                    CheckCount  = 1,
                    ChangeCount = 0,
                    LastCheckedAt = now,
                    Status      = WebPageStatus.Active,
                };
                existing.NextCheckAt = now.Add(_scheduler.CalculateNextCheckInterval(existing, source));
                _db.WebPages.Add(existing);
            }
            else
            {
                var changed = existing.ContentHash != hash;
                existing.Title       = extracted.Title;
                existing.ContentHash = hash;
                existing.ContentLength = extracted.Text.Length;
                existing.HttpETag    = etag;
                existing.HttpLastModified = lastModified;
                existing.CheckCount++;
                existing.LastCheckedAt = now;
                existing.Status      = WebPageStatus.Active;
                if (changed)
                {
                    existing.ChangeCount++;
                    existing.LastChangedAt = now;
                }
                existing.NextCheckAt = now.Add(_scheduler.CalculateNextCheckInterval(existing, source));
            }

            await _db.SaveChangesAsync(ct);

            return returnLinks ? extracted.Links : null;
        }
    }

    private async Task FailAsync(WebSource source, string message, CancellationToken ct)
    {
        source.Status = WebSourceStatus.Error;
        source.ErrorMessage = message;
        source.CurrentCrawlUrl = null;
        source.LastCrawledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("Crawl for WebSource {Id} ({Url}) failed: {Message}", source.Id, source.Url, message);
    }

    private static Regex? CompileOrNull(string? pattern) =>
        string.IsNullOrWhiteSpace(pattern) ? null : new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool IsAllowedByPatterns(string path, Regex? include, Regex? exclude)
    {
        if (exclude is not null && exclude.IsMatch(path)) return false;
        if (include is not null && !include.IsMatch(path)) return false;
        return true;
    }
}

/// <summary>Named HttpClient constants shared by the crawling infrastructure.</summary>
public static class WebCrawlHttpClients
{
    public const string CrawlerClientName = "web-crawler";
}
