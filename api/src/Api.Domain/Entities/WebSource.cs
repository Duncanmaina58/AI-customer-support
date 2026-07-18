using Api.Domain.Common;
using Api.Domain.Enums;

namespace Api.Domain.Entities;

/// <summary>
/// Sprint 4 web crawling addition: tracks each website URL a company connects
/// as a knowledge source. One WebSource can produce many WebPage rows (one per
/// crawled page), whose extracted text is chunked and embedded into the exact
/// same KnowledgeChunks table used by document/manual entries — see
/// KnowledgeIngestionService and WebCrawlerService.
/// </summary>
public class WebSource : AuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    /// <summary>Root URL, e.g. https://acme.co.ke — HEAD-checked for reachability before saving.</summary>
    public string Url { get; set; } = string.Empty;

    public WebCrawlMode CrawlMode { get; set; } = WebCrawlMode.FullSite;

    /// <summary>Link levels to follow when no sitemap is found. Default 3, hard max 5 (enforced in the controller).</summary>
    public int CrawlDepth { get; set; } = 3;

    /// <summary>Regex — only crawl URLs matching this. Null = all URLs on the same host.</summary>
    public string? IncludePattern { get; set; }

    /// <summary>Regex — skip matching URLs, e.g. "/blog/|/careers/".</summary>
    public string? ExcludePattern { get; set; }

    /// <summary>Hard cap on pages crawled per run. Default 200 — prevents runaway embedding costs on huge sites.</summary>
    public int MaxPages { get; set; } = 200;

    public WebSourceStatus Status { get; set; } = WebSourceStatus.Pending;

    /// <summary>Count of pages successfully indexed on the most recent crawl.</summary>
    public int PagesCrawled { get; set; }

    /// <summary>Total vector chunks currently stored across all of this source's pages.</summary>
    public int ChunksCreated { get; set; }

    public WebSourceMonitoringMode MonitoringMode { get; set; } = WebSourceMonitoringMode.Adaptive;

    /// <summary>Used only when MonitoringMode == Fixed. Null otherwise.</summary>
    public int? FixedIntervalHours { get; set; }

    /// <summary>Send a dashboard/email notification whenever ContentFreshnessService detects a page changed or was removed.</summary>
    public bool NotifyOnChange { get; set; } = true;

    public DateTime? LastCrawledAt { get; set; }

    /// <summary>Populated when Status == Error (robots.txt block, unreachable host, etc).</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Set by WebCrawlerService when at least one crawled page rendered to
    /// under 100 characters of extracted text — a strong signal the site is a
    /// JS-rendered SPA that HtmlAgilityPack (raw-HTML only) can't read.
    /// Headless-browser rendering is out of scope for Phase 1 (see edge cases).
    /// </summary>
    public bool HasJsRenderedPagesWarning { get; set; }

    /// <summary>True if MaxPages was hit and the crawl stopped before covering the whole site.</summary>
    public bool MaxPagesReached { get; set; }

    /// <summary>
    /// Live progress fields for the "current URL being crawled" dashboard view
    /// (GET .../status, polled by the frontend while Status == Crawling). Set
    /// by WebCrawlerService as it works; cleared back to null once the crawl finishes.
    /// </summary>
    public string? CurrentCrawlUrl { get; set; }

    /// <summary>Best-effort total page count for a progress bar — set once a sitemap is found, left null during BFS (unknown until the crawl finishes).</summary>
    public int? EstimatedTotalPages { get; set; }

    public ICollection<WebPage> Pages { get; set; } = new List<WebPage>();
}
