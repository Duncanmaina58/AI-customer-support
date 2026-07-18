using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Crawling;

public interface ISitemapService
{
    /// <summary>
    /// Tries robots.txt-declared sitemap URLs first, then falls back to
    /// {siteRoot}/sitemap.xml. Transparently expands sitemap INDEX files (a
    /// sitemap of sitemaps) up to one level deep. Returns an empty list if no
    /// sitemap is found or reachable — callers should fall back to BFS.
    /// </summary>
    Task<IReadOnlyList<string>> DiscoverUrlsAsync(
        Uri siteRoot,
        IReadOnlyList<string> robotsDeclaredSitemaps,
        CancellationToken ct = default);
}

/// <summary>
/// Sprint 4 web crawling: spec 16.6 step 3 — "Sitemap detection — fast path.
/// Try fetching /sitemap.xml. If found, extract all URLs directly — faster
/// and more complete than link-following. If not found, fall back to BFS."
/// </summary>
public class SitemapService : ISitemapService
{
    /// <summary>Hard cap on how many URLs we'll pull out of a sitemap, mirroring MaxPages' spirit even before per-source limits apply.</summary>
    private const int MaxUrlsFromSitemap = 5_000;

    private readonly HttpClient _http;
    private readonly ILogger<SitemapService>? _logger;

    public SitemapService(HttpClient http, ILogger<SitemapService>? logger = null)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> DiscoverUrlsAsync(
        Uri siteRoot,
        IReadOnlyList<string> robotsDeclaredSitemaps,
        CancellationToken ct = default)
    {
        var candidates = robotsDeclaredSitemaps.Count > 0
            ? robotsDeclaredSitemaps
            : [new Uri(siteRoot, "/sitemap.xml").ToString()];

        var urls = new List<string>();
        var visitedSitemaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (urls.Count >= MaxUrlsFromSitemap) break;
            await FetchAndExpandAsync(candidate, urls, visitedSitemaps, depth: 0, ct);
        }

        return urls;
    }

    private async Task FetchAndExpandAsync(
        string sitemapUrl,
        List<string> urls,
        HashSet<string> visited,
        int depth,
        CancellationToken ct)
    {
        // Sitemap index files reference child sitemaps — expand one level deep only,
        // which covers every real-world site while guaranteeing termination.
        if (depth > 1 || !visited.Add(sitemapUrl) || urls.Count >= MaxUrlsFromSitemap)
            return;

        string xml;
        try
        {
            using var response = await _http.GetAsync(sitemapUrl, ct);
            if (!response.IsSuccessStatusCode) return;
            xml = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Sitemap fetch failed for {Url}", sitemapUrl);
            return;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Sitemap XML parse failed for {Url}", sitemapUrl);
            return;
        }

        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        // sitemapindex -> <sitemap><loc>child.xml</loc></sitemap>
        var childSitemaps = doc.Descendants(ns + "sitemap")
            .Select(s => s.Element(ns + "loc")?.Value.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (childSitemaps.Count > 0)
        {
            foreach (var child in childSitemaps)
            {
                if (urls.Count >= MaxUrlsFromSitemap) break;
                await FetchAndExpandAsync(child!, urls, visited, depth + 1, ct);
            }
            return;
        }

        // urlset -> <url><loc>page.html</loc></url>
        var pageUrls = doc.Descendants(ns + "url")
            .Select(u => u.Element(ns + "loc")?.Value.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l));

        foreach (var url in pageUrls)
        {
            if (urls.Count >= MaxUrlsFromSitemap) break;
            urls.Add(url!);
        }
    }
}
