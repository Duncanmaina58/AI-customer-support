using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.AI;
using Api.Infrastructure.Crawling;

namespace Api.Tests;

/// <summary>
/// Unit tests for the Sprint 4 web crawling addition's pure-logic pieces —
/// robots.txt parsing, text chunking, adaptive scheduling, HTML extraction,
/// and change-detection hashing — none of which need a real DB, HTTP call, or
/// API key. WebCrawlerService/ContentFreshnessService/WebSourcesController
/// themselves are integration-level (real HTTP + DB) and aren't covered here,
/// same rationale as Sprint4Tests' note about RagService/CohereEmbeddingProvider.
/// </summary>
public class Sprint4WebCrawlingTests
{
    // -------------------------------------------------------------------------
    // RobotsTxtChecker
    // -------------------------------------------------------------------------

    [Fact]
    public void RobotsTxt_bare_disallow_all_blocks_entire_site()
    {
        var result = RobotsTxtChecker.Parse("User-agent: *\nDisallow: /");

        Assert.True(result.EntireSiteDisallowed);
        Assert.False(result.IsAllowed("/anything"));
    }

    [Fact]
    public void RobotsTxt_disallow_specific_path_only_blocks_that_path()
    {
        var result = RobotsTxtChecker.Parse("User-agent: *\nDisallow: /admin/");

        Assert.False(result.EntireSiteDisallowed);
        Assert.False(result.IsAllowed("/admin/dashboard"));
        Assert.True(result.IsAllowed("/products"));
    }

    [Fact]
    public void RobotsTxt_longest_match_wins_allow_over_disallow()
    {
        // A more specific Allow should beat a broader Disallow, per Google's algorithm.
        var result = RobotsTxtChecker.Parse("User-agent: *\nDisallow: /private/\nAllow: /private/public-page.html");

        Assert.True(result.IsAllowed("/private/public-page.html"));
        Assert.False(result.IsAllowed("/private/secret.html"));
    }

    [Fact]
    public void RobotsTxt_missing_file_treated_as_allow_all()
    {
        var result = RobotsTxtChecker.Parse("");

        Assert.False(result.EntireSiteDisallowed);
        Assert.True(result.IsAllowed("/anything"));
    }

    [Fact]
    public void RobotsTxt_extracts_sitemap_urls()
    {
        var result = RobotsTxtChecker.Parse(
            "User-agent: *\nDisallow: /admin/\nSitemap: https://example.com/sitemap.xml");

        Assert.Single(result.SitemapUrls);
        Assert.Equal("https://example.com/sitemap.xml", result.SitemapUrls[0]);
    }

    [Fact]
    public void RobotsTxt_wildcard_pattern_matches_correctly()
    {
        var result = RobotsTxtChecker.Parse("User-agent: *\nDisallow: /*.pdf$");

        Assert.False(result.IsAllowed("/downloads/brochure.pdf"));
        Assert.True(result.IsAllowed("/downloads/brochure.pdf.html"));
    }

    [Fact]
    public void RobotsTxt_group_addressed_to_our_bot_takes_priority_over_wildcard()
    {
        var result = RobotsTxtChecker.Parse(
            "User-agent: *\nDisallow: /\n\nUser-agent: AiSupportPlatformBot\nAllow: /");

        Assert.True(result.IsAllowed("/products"));
    }

    // -------------------------------------------------------------------------
    // TextChunker (already exercised indirectly elsewhere, but web crawling
    // leans on it for every page — worth a direct check here too).
    // -------------------------------------------------------------------------

    [Fact]
    public void TextChunker_short_text_returns_single_chunk()
    {
        var chunks = TextChunker.Chunk("Hello world, this is a short page.");
        Assert.Single(chunks);
    }

    [Fact]
    public void TextChunker_long_text_splits_with_overlap()
    {
        var text = string.Join(' ', Enumerable.Range(1, 1000).Select(i => $"word{i}"));
        var chunks = TextChunker.Chunk(text, wordsPerChunk: 400, wordOverlap: 80);

        Assert.True(chunks.Count > 1);
        // The overlap words at the end of chunk 1 should reappear near the start of chunk 2:
        // chunk1 = words[0..399], chunk2 = words[320..719] (step = 400-80 = 320), so
        // chunk1's very last word (index 399) is chunk2's word at offset 399-320 = 79.
        var chunk1Words = chunks[0].Split(' ');
        var chunk2Words = chunks[1].Split(' ');
        Assert.Equal(chunk1Words[^1], chunk2Words[79]);
    }

    [Fact]
    public void TextChunker_empty_text_returns_no_chunks()
    {
        Assert.Empty(TextChunker.Chunk(""));
        Assert.Empty(TextChunker.Chunk("   "));
    }

    // -------------------------------------------------------------------------
    // AdaptiveCheckScheduler
    // -------------------------------------------------------------------------

    private static WebSource MakeSource(WebSourceMonitoringMode mode = WebSourceMonitoringMode.Adaptive, int? fixedHours = null) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        Url = "https://example.com",
        MonitoringMode = mode,
        FixedIntervalHours = fixedHours,
    };

    private static WebPage MakePage(int checkCount, int changeCount) => new()
    {
        Id = Guid.NewGuid(),
        WebSourceId = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        Url = "https://example.com/page",
        CheckCount = checkCount,
        ChangeCount = changeCount,
    };

    [Fact]
    public void AdaptiveScheduler_new_page_with_few_checks_uses_12_hour_default()
    {
        var scheduler = new AdaptiveCheckScheduler();
        var interval = scheduler.CalculateNextCheckInterval(MakePage(checkCount: 2, changeCount: 0), MakeSource());

        Assert.Equal(TimeSpan.FromHours(12), interval);
    }

    [Fact]
    public void AdaptiveScheduler_frequently_changing_page_gets_short_interval()
    {
        var scheduler = new AdaptiveCheckScheduler();
        // 60% change rate over enough checks to leave the "new page" band.
        var interval = scheduler.CalculateNextCheckInterval(MakePage(checkCount: 10, changeCount: 6), MakeSource());

        Assert.Equal(TimeSpan.FromHours(6), interval);
    }

    [Fact]
    public void AdaptiveScheduler_rarely_changing_page_gets_long_interval()
    {
        var scheduler = new AdaptiveCheckScheduler();
        // 1% change rate — a static page.
        var interval = scheduler.CalculateNextCheckInterval(MakePage(checkCount: 100, changeCount: 1), MakeSource());

        Assert.Equal(TimeSpan.FromDays(7), interval);
    }

    [Fact]
    public void AdaptiveScheduler_fixed_mode_ignores_history_and_uses_configured_hours()
    {
        var scheduler = new AdaptiveCheckScheduler();
        var interval = scheduler.CalculateNextCheckInterval(
            MakePage(checkCount: 50, changeCount: 40), MakeSource(WebSourceMonitoringMode.Fixed, fixedHours: 6));

        Assert.Equal(TimeSpan.FromHours(6), interval);
    }

    [Fact]
    public void AdaptiveScheduler_manual_mode_returns_a_very_long_interval()
    {
        var scheduler = new AdaptiveCheckScheduler();
        var interval = scheduler.CalculateNextCheckInterval(MakePage(1, 0), MakeSource(WebSourceMonitoringMode.Manual));

        Assert.True(interval > TimeSpan.FromDays(365));
    }

    // -------------------------------------------------------------------------
    // HtmlContentExtractor
    // -------------------------------------------------------------------------

    [Fact]
    public void HtmlExtractor_strips_nav_header_footer_and_scripts()
    {
        const string html = """
            <html><head><title>Acme</title></head>
            <body>
                <nav>Home | About | Contact</nav>
                <header>Welcome banner</header>
                <main><h1>Our Refund Policy</h1><p>Refunds within 30 days.</p></main>
                <footer>Copyright 2026</footer>
                <script>console.log('noise')</script>
            </body></html>
            """;

        var extractor = new HtmlContentExtractor();
        var result = extractor.Extract(html, new Uri("https://example.com/refunds"));

        Assert.Equal("Acme", result.Title);
        Assert.Contains("Refund Policy", result.Text);
        Assert.Contains("Refunds within 30 days", result.Text);
        Assert.DoesNotContain("Home | About | Contact", result.Text);
        Assert.DoesNotContain("Welcome banner", result.Text);
        Assert.DoesNotContain("Copyright 2026", result.Text);
        Assert.DoesNotContain("console.log", result.Text);
    }

    [Fact]
    public void HtmlExtractor_resolves_relative_links_to_absolute_same_host_only()
    {
        const string html = """
            <html><body>
                <a href="/pricing">Pricing</a>
                <a href="https://example.com/about">About</a>
                <a href="https://otherdomain.com/partner">Partner</a>
                <a href="mailto:hi@example.com">Email us</a>
                <a href="#section">Jump</a>
            </body></html>
            """;

        var extractor = new HtmlContentExtractor();
        var result = extractor.Extract(html, new Uri("https://example.com/home"));

        Assert.Contains("https://example.com/pricing", result.Links);
        Assert.Contains("https://example.com/about", result.Links);
        Assert.DoesNotContain(result.Links, l => l.Contains("otherdomain.com"));
        Assert.DoesNotContain(result.Links, l => l.StartsWith("mailto:"));
        Assert.DoesNotContain(result.Links, l => l.Contains('#'));
    }

    [Fact]
    public void HtmlExtractor_normalizes_url_strips_fragment_and_trailing_slash()
    {
        var normalized = HtmlContentExtractor.NormalizeUrl(new Uri("https://example.com/products/#reviews"));
        Assert.Equal("https://example.com/products", normalized);
    }

    // -------------------------------------------------------------------------
    // WebPageChangeDetector.ComputeHash
    // -------------------------------------------------------------------------

    [Fact]
    public void ChangeDetector_identical_text_produces_identical_hash()
    {
        var hash1 = WebPageChangeDetector.ComputeHash("Our store hours are 9am-5pm.");
        var hash2 = WebPageChangeDetector.ComputeHash("Our store hours are 9am-5pm.");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ChangeDetector_different_text_produces_different_hash()
    {
        var hash1 = WebPageChangeDetector.ComputeHash("Our store hours are 9am-5pm.");
        var hash2 = WebPageChangeDetector.ComputeHash("Our store hours are 9am-6pm.");

        Assert.NotEqual(hash1, hash2);
    }
}
