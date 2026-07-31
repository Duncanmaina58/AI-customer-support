using Api.Domain.Common;
using Api.Domain.Enums;

namespace Api.Domain.Entities;

/// <summary>
/// One row per crawled page. This is the core of the content-freshness system:
/// ContentHash + HttpETag/HttpLastModified let WebPageChangeDetector run its
/// three-tier cascade (HEAD → hash → re-index) as cheaply as possible, and
/// NextCheckAt (set by AdaptiveCheckScheduler) drives which pages
/// ContentFreshnessService picks up on each 5-minute sweep.
///
/// CompanyId is denormalized here (also reachable via WebSource.CompanyId) so
/// the global EF Core tenant query filter can apply directly to this entity
/// too, and so per-tenant queries/analytics don't need a join.
/// </summary>
public class WebPage : AuditableEntity, ITenantScoped
{
    public Guid WebSourceId { get; set; }
    public WebSource? WebSource { get; set; }

    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    /// <summary>Full page URL, normalised (trailing slash stripped).</summary>
    public string Url { get; set; } = string.Empty;

    public string? Title { get; set; }

    /// <summary>SHA-256 (lowercase hex) of the extracted clean text. The core change-detection key.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public int ContentLength { get; set; }

    /// <summary>ETag response header, if the server sent one. Used for the free Tier-1 HEAD check.</summary>
    public string? HttpETag { get; set; }

    /// <summary>Last-Modified response header, if the server sent one. Also used in the Tier-1 check.</summary>
    public DateTime? HttpLastModified { get; set; }

    public int CheckCount { get; set; }
    public int ChangeCount { get; set; }

    public DateTime? LastCheckedAt { get; set; }
    public DateTime? LastChangedAt { get; set; }

    /// <summary>Scheduled next check time, set by AdaptiveCheckScheduler after every check.</summary>
    public DateTime NextCheckAt { get; set; } = DateTime.UtcNow.AddHours(12);

    public WebPageStatus Status { get; set; } = WebPageStatus.Active;
}
