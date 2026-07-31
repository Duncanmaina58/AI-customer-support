namespace Api.Contracts.Knowledge;

/// <summary>POST /api/knowledge/web-sources — connect a new website as a knowledge source.</summary>
public record CreateWebSourceRequest(
    string  Url,
    string  CrawlMode        = "full_site",   // "full_site" | "single_page" | "sitemap"
    int     CrawlDepth       = 3,
    string? IncludePattern   = null,
    string? ExcludePattern   = null,
    int     MaxPages         = 200,
    string  MonitoringMode   = "adaptive",     // "adaptive" | "fixed" | "manual"
    int?    FixedIntervalHours = null,
    bool    NotifyOnChange   = true);

/// <summary>PATCH /api/knowledge/web-sources/{id}/monitoring — update monitoring settings only.</summary>
public record UpdateMonitoringRequest(
    string MonitoringMode,
    int?   FixedIntervalHours,
    bool   NotifyOnChange);

public record WebSourceDto(
    Guid     Id,
    string   Url,
    string   CrawlMode,
    int      CrawlDepth,
    string?  IncludePattern,
    string?  ExcludePattern,
    int      MaxPages,
    string   Status,
    int      PagesCrawled,
    int      ChunksCreated,
    string   MonitoringMode,
    int?     FixedIntervalHours,
    bool     NotifyOnChange,
    DateTime? LastCrawledAt,
    string?  ErrorMessage,
    bool     HasJsRenderedPagesWarning,
    bool     MaxPagesReached,
    DateTime CreatedAt);

/// <summary>GET /api/knowledge/web-sources/{id}/status — live crawl progress for dashboard polling.</summary>
public record WebSourceStatusDto(
    Guid    Id,
    string  Status,
    int     PagesCrawled,
    int?    EstimatedTotalPages,
    string? CurrentCrawlUrl,
    string? ErrorMessage);

public record WebPageDto(
    Guid      Id,
    string    Url,
    string?   Title,
    string    Status,
    int       CheckCount,
    int       ChangeCount,
    DateTime? LastCheckedAt,
    DateTime? LastChangedAt,
    DateTime  NextCheckAt,
    int       ContentLength);

/// <summary>A single row in the GET .../changes feed, derived from WebPage's own change fields (see WebSourcesController.GetChanges).</summary>
public record WebPageChangeDto(
    string    Url,
    string    ChangeType,   // "changed" | "removed"
    DateTime  DetectedAt);
