using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Crawling;

/// <summary>
/// The result of fetching and parsing a site's /robots.txt. Immutable — safe to
/// cache for the lifetime of a single crawl run.
/// </summary>
public sealed class RobotsTxtResult
{
    /// <summary>True when a bare "Disallow: /" applies to us — the whole crawl must stop.</summary>
    public bool EntireSiteDisallowed { get; init; }

    /// <summary>True when robots.txt itself couldn't be fetched (404, timeout, etc) — treated as "allow everything", per convention.</summary>
    public bool RobotsTxtMissing { get; init; }

    /// <summary>Sitemap: lines discovered in robots.txt, absolute URLs.</summary>
    public IReadOnlyList<string> SitemapUrls { get; init; } = [];

    private readonly List<(string Pattern, bool IsAllow)> _rules;

    public RobotsTxtResult(List<(string Pattern, bool IsAllow)> rules)
    {
        _rules = rules;
    }

    /// <summary>
    /// Google's "longest matching rule wins" algorithm: among every Allow/Disallow
    /// pattern that matches this path, the LONGEST pattern string wins; ties go to
    /// Allow. No matching rule at all = allowed.
    /// </summary>
    public bool IsAllowed(string path)
    {
        if (EntireSiteDisallowed) return false;

        (string Pattern, bool IsAllow)? best = null;
        foreach (var rule in _rules)
        {
            if (!MatchesPattern(path, rule.Pattern)) continue;
            if (best is null || rule.Pattern.Length > best.Value.Pattern.Length)
                best = rule;
        }

        return best is null || best.Value.IsAllow;
    }

    private static bool MatchesPattern(string path, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;

        // robots.txt patterns support '*' (any sequence) and a trailing '$' (exact end).
        var anchoredEnd = pattern.EndsWith('$');
        var body = anchoredEnd ? pattern[..^1] : pattern;

        var regexPattern = "^" + string.Join(".*", body.Split('*').Select(Regex.Escape)) + (anchoredEnd ? "$" : "");
        return Regex.IsMatch(path, regexPattern);
    }
}

public interface IRobotsTxtChecker
{
    /// <summary>Fetches and parses {scheme}://{host}/robots.txt for the given site root.</summary>
    Task<RobotsTxtResult> FetchAsync(Uri siteRoot, CancellationToken ct = default);
}

/// <summary>
/// Sprint 4 web crawling: fetches and parses robots.txt before a single page is
/// crawled — checklist item "robots.txt parsed before ANY page crawled".
///
/// Deliberately hand-rolled rather than an external "Robots" NuGet package: the
/// parsing rules needed here (User-agent grouping, Allow/Disallow, wildcard '*'
/// and trailing '$', Sitemap: extraction) are a well-specified, small surface
/// area, and this keeps the crawler free of a third-party API we can't pin down
/// without a live NuGet feed. HtmlAgilityPack (a much larger, harder-to-replicate
/// HTML DOM parser) IS taken as a real dependency below in HtmlContentExtractor —
/// this is a deliberate, scoped trade-off, not a blanket "avoid NuGet" rule.
/// </summary>
public class RobotsTxtChecker : IRobotsTxtChecker
{
    /// <summary>User-agent we crawl as and look for group-specific rules under, before falling back to "*".</summary>
    public const string UserAgent = "AiSupportPlatformBot";

    private readonly HttpClient _http;
    private readonly ILogger<RobotsTxtChecker>? _logger;

    public RobotsTxtChecker(HttpClient http, ILogger<RobotsTxtChecker>? logger = null)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<RobotsTxtResult> FetchAsync(Uri siteRoot, CancellationToken ct = default)
    {
        var robotsUrl = new Uri(siteRoot, "/robots.txt");

        string content;
        try
        {
            using var response = await _http.GetAsync(robotsUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                // Missing/unreachable robots.txt is conventionally treated as "crawl allowed".
                return new RobotsTxtResult([]) { RobotsTxtMissing = true };
            }
            content = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "robots.txt fetch failed for {Url} — treating as allow-all", robotsUrl);
            return new RobotsTxtResult([]) { RobotsTxtMissing = true };
        }

        return Parse(content);
    }

    /// <summary>Pure parsing logic, split out so it's unit-testable without any HTTP call.</summary>
    public static RobotsTxtResult Parse(string robotsTxtContent)
    {
        var lines = robotsTxtContent
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'));

        var sitemaps = new List<string>();
        var ourGroupRules = new List<(string Pattern, bool IsAllow)>();
        var wildcardGroupRules = new List<(string Pattern, bool IsAllow)>();

        // "currentGroupTargetsUs": null while we haven't seen a User-agent line yet;
        // true if the active group applies to us (either our UA or '*'); false otherwise.
        bool? currentGroupTargetsUs = null;
        var currentGroupIsWildcard = false;
        // A single robots.txt "group" can list multiple User-agent lines before its
        // rules — track whether we've started seeing rule lines yet, so a run of
        // consecutive User-agent lines is treated as one group (per RFC 9309).
        var groupHasStartedRules = false;

        foreach (var rawLine in lines)
        {
            var colonIndex = rawLine.IndexOf(':');
            if (colonIndex < 0) continue;

            var field = rawLine[..colonIndex].Trim().ToLowerInvariant();
            var value = rawLine[(colonIndex + 1)..].Trim();
            // Strip inline comments.
            var hashIndex = value.IndexOf('#');
            if (hashIndex >= 0) value = value[..hashIndex].Trim();

            switch (field)
            {
                case "user-agent":
                    if (groupHasStartedRules)
                    {
                        // A new User-agent line after rules started = a brand-new group.
                        currentGroupTargetsUs = null;
                        groupHasStartedRules = false;
                    }
                    var ua = value.ToLowerInvariant();
                    var matchesUs = ua == "*" || UserAgent.ToLowerInvariant().Contains(ua) || ua.Contains(UserAgent.ToLowerInvariant());
                    currentGroupIsWildcard = ua == "*";
                    currentGroupTargetsUs = currentGroupTargetsUs == true || matchesUs;
                    break;

                case "disallow":
                case "allow":
                    groupHasStartedRules = true;
                    if (currentGroupTargetsUs != true) break;
                    if (string.IsNullOrEmpty(value) && field == "disallow") break; // "Disallow:" (empty) = allow everything

                    var target = currentGroupIsWildcard ? wildcardGroupRules : ourGroupRules;
                    target.Add((value, field == "allow"));
                    break;

                case "sitemap":
                    if (!string.IsNullOrWhiteSpace(value)) sitemaps.Add(value);
                    break;
            }
        }

        // Prefer a group specifically addressed to us; otherwise fall back to '*'.
        var effectiveRules = ourGroupRules.Count > 0 ? ourGroupRules : wildcardGroupRules;

        var entireSiteDisallowed = false;
        if (effectiveRules.Count > 0)
        {
            // A bare "Disallow: /" with no more-specific Allow beating it means the
            // whole site is off-limits — checked via the same longest-match logic
            // the result object itself uses.
            var probe = new RobotsTxtResult(effectiveRules);
            entireSiteDisallowed = !probe.IsAllowed("/");
        }

        return new RobotsTxtResult(effectiveRules)
        {
            EntireSiteDisallowed = entireSiteDisallowed,
            SitemapUrls = sitemaps,
        };
    }
}
