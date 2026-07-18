using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Api.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Crawling;

public enum ChangeCheckOutcome
{
    Unchanged,
    Changed,
    Removed,
}

public sealed record ChangeCheckResult(
    ChangeCheckOutcome Outcome,
    string Reason,
    string? NewText = null,
    string? NewHash = null,
    string? NewTitle = null,
    string? NewETag = null,
    DateTime? NewLastModified = null)
{
    public static ChangeCheckResult Unchanged(string reason) => new(ChangeCheckOutcome.Unchanged, reason);
    public static ChangeCheckResult Removed(string reason) => new(ChangeCheckOutcome.Removed, reason);
    public static ChangeCheckResult Changed(
        string newText, string newHash, string? newTitle, string? newETag, DateTime? newLastModified) =>
        new(ChangeCheckOutcome.Changed, "Content changed", newText, newHash, newTitle, newETag, newLastModified);
}

public interface IWebPageChangeDetector
{
    Task<ChangeCheckResult> CheckPageAsync(WebPage page, CancellationToken ct = default);
}

/// <summary>
/// Sprint 4 web crawling, spec 16.3: a cascade of increasingly expensive
/// checks that stops as early as possible, so a 50-page site checked daily
/// spends embedding tokens on only the 2-3 pages that actually changed.
///
///   Tier 1 — HTTP HEAD, ETag/If-Modified-Since. ~50ms, zero tokens, no download.
///   Tier 2 — full fetch + SHA-256 hash of extracted text. ~200ms, zero tokens.
///   Tier 3 — hash differs -> caller re-indexes (embedding tokens spent here only).
/// </summary>
public class WebPageChangeDetector : IWebPageChangeDetector
{
    private readonly HttpClient _http;
    private readonly IHtmlContentExtractor _extractor;
    private readonly ILogger<WebPageChangeDetector> _logger;

    public WebPageChangeDetector(
        HttpClient http,
        IHtmlContentExtractor extractor,
        ILogger<WebPageChangeDetector> logger)
    {
        _http      = http;
        _extractor = extractor;
        _logger    = logger;
    }

    public async Task<ChangeCheckResult> CheckPageAsync(WebPage page, CancellationToken ct = default)
    {
        // ---- TIER 1: HTTP HEAD — zero cost check --------------------------
        try
        {
            var headRequest = new HttpRequestMessage(HttpMethod.Head, page.Url);
            if (!string.IsNullOrEmpty(page.HttpETag))
                headRequest.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(QuoteIfNeeded(page.HttpETag)));
            if (page.HttpLastModified.HasValue)
                headRequest.Headers.IfModifiedSince = page.HttpLastModified;

            using var headResponse = await _http.SendAsync(headRequest, ct);

            if (headResponse.StatusCode == HttpStatusCode.NotModified)
                return ChangeCheckResult.Unchanged("304 Not Modified");

            if (headResponse.StatusCode == HttpStatusCode.NotFound || headResponse.StatusCode == HttpStatusCode.Gone)
                return ChangeCheckResult.Removed($"HEAD returned {(int)headResponse.StatusCode}");

            var newETagTag = headResponse.Headers.ETag?.Tag;
            if (newETagTag != null && !string.IsNullOrEmpty(page.HttpETag)
                && string.Equals(Unquote(newETagTag), Unquote(page.HttpETag), StringComparison.Ordinal))
                return ChangeCheckResult.Unchanged("ETag match");
        }
        catch (Exception ex)
        {
            // HEAD not supported by this server, or a transient network error —
            // fall through to the full-fetch tier rather than failing the check.
            _logger.LogDebug(ex, "HEAD check failed for {Url} — falling through to full fetch", page.Url);
        }

        // ---- TIER 2: full fetch + SHA-256 hash -----------------------------
        HttpResponseMessage getResponse;
        try
        {
            getResponse = await _http.GetAsync(page.Url, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET failed for {Url} during change check", page.Url);
            return ChangeCheckResult.Unchanged("Fetch failed — will retry next cycle");
        }

        using (getResponse)
        {
            if (getResponse.StatusCode == HttpStatusCode.NotFound || getResponse.StatusCode == HttpStatusCode.Gone)
                return ChangeCheckResult.Removed($"GET returned {(int)getResponse.StatusCode}");

            if (!getResponse.IsSuccessStatusCode)
                return ChangeCheckResult.Unchanged($"GET returned {(int)getResponse.StatusCode} — will retry next cycle");

            var html = await getResponse.Content.ReadAsStringAsync(ct);
            var extracted = _extractor.Extract(html, new Uri(page.Url));
            var newHash = ComputeHash(extracted.Text);

            var newETag = getResponse.Headers.ETag?.Tag;
            var newLastModified = getResponse.Content.Headers.LastModified?.UtcDateTime;

            if (string.Equals(newHash, page.ContentHash, StringComparison.Ordinal))
                return ChangeCheckResult.Unchanged("Hash match");

            // ---- TIER 3: content changed — hand back to caller to re-index ----
            return ChangeCheckResult.Changed(extracted.Text, newHash, extracted.Title, newETag, newLastModified);
        }
    }

    public static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string QuoteIfNeeded(string etag) =>
        etag.StartsWith('"') ? etag : $"\"{etag}\"";

    private static string Unquote(string etag) =>
        etag.Trim('"');
}
