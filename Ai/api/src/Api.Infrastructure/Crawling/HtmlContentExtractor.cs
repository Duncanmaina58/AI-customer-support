using HtmlAgilityPack;

namespace Api.Infrastructure.Crawling;

/// <summary>Result of extracting a single fetched HTML page.</summary>
public sealed record ExtractedPage(
    string Title,
    string Text,
    IReadOnlyList<string> Links);

public interface IHtmlContentExtractor
{
    /// <summary>
    /// Parses raw HTML, strips navigation/boilerplate noise, isolates the main
    /// content, and resolves every internal &lt;a href&gt; to an absolute URL.
    /// </summary>
    ExtractedPage Extract(string html, Uri pageUrl);
}

/// <summary>
/// Sprint 4 web crawling: turns raw HTML into clean text ready for chunking +
/// embedding, plus the list of internal links used to drive the BFS crawl.
///
/// Deliberately mirrors the spec's noise-removal selector list closely (nav,
/// header, footer, script, style, cookie/sidebar/menu classes, aria-hidden),
/// then falls back through &lt;main&gt; → #content → &lt;article&gt; → &lt;body&gt;
/// for the main-content root, same as the spec's ExtractMainContent.
/// </summary>
public class HtmlContentExtractor : IHtmlContentExtractor
{
    /// <summary>
    /// Below this many extracted characters, WebCrawlerService flags the page
    /// (and ultimately the whole source) as likely JS-rendered — see
    /// WebSource.HasJsRenderedPagesWarning and edge-case table 16.10.
    /// </summary>
    public const int JsRenderedWarningThreshold = 100;

    /// <summary>Cap extracted text at ~50,000 tokens (approximated as words) before chunking — spec 16.10 "Very large pages".</summary>
    public const int MaxExtractedWords = 50_000;

    private static readonly string[] NoiseXPaths =
    [
        "//nav", "//header", "//footer", "//script", "//style", "//noscript",
        "//*[contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'cookie')]",
        "//*[contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'sidebar')]",
        "//*[contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'menu')]",
        "//*[@aria-hidden='true']",
    ];

    public ExtractedPage Extract(string html, Uri pageUrl)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText.Trim() ?? string.Empty;
        title = HtmlEntity.DeEntitize(title);

        // Links are collected from the ORIGINAL document (before noise removal —
        // nav bars are exactly where most internal links live) so the BFS
        // frontier isn't starved by stripping <nav> first.
        var links = ExtractInternalLinks(doc, pageUrl);

        foreach (var xpath in NoiseXPaths)
        {
            var nodes = doc.DocumentNode.SelectNodes(xpath);
            if (nodes is null) continue;
            foreach (var node in nodes.ToList())
                node.Remove();
        }

        var main = doc.DocumentNode.SelectSingleNode("//main")
                ?? doc.DocumentNode.SelectSingleNode("//*[@id='content']")
                ?? doc.DocumentNode.SelectSingleNode("//article")
                ?? doc.DocumentNode.SelectSingleNode("//body");

        var text = main is null ? string.Empty : CleanText(main.InnerText);

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > MaxExtractedWords)
            text = string.Join(' ', words.Take(MaxExtractedWords));

        return new ExtractedPage(title, text, links);
    }

    private static string CleanText(string rawInnerText)
    {
        var decoded = HtmlEntity.DeEntitize(rawInnerText);
        var lines = decoded
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 2);

        return string.Join('\n', lines);
    }

    private static List<string> ExtractInternalLinks(HtmlDocument doc, Uri pageUrl)
    {
        var anchors = doc.DocumentNode.SelectNodes("//a[@href]");
        if (anchors is null) return [];

        var results = new List<string>();
        foreach (var a in anchors)
        {
            var href = a.GetAttributeValue("href", string.Empty);
            if (string.IsNullOrWhiteSpace(href)) continue;

            // Skip in-page anchors, mailto/tel, and javascript: pseudo-links.
            if (href.StartsWith('#')) continue;
            if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) continue;
            if (href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) continue;
            if (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) continue;

            if (!Uri.TryCreate(pageUrl, href, out var resolved)) continue;
            if (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps) continue;

            // Same registrable host only — cross-domain links (partners, socials) are out of scope.
            if (!string.Equals(resolved.Host, pageUrl.Host, StringComparison.OrdinalIgnoreCase)) continue;

            results.Add(NormalizeUrl(resolved));
        }

        return results;
    }

    /// <summary>Strips the fragment and trailing slash so the same logical page never gets crawled twice under two URLs.</summary>
    public static string NormalizeUrl(Uri uri)
    {
        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        var s = builder.Uri.ToString();
        return s.Length > 1 && s.EndsWith('/') ? s[..^1] : s;
    }
}
