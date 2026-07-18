using System.Threading.Channels;
using Api.Infrastructure.Crawling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.BackgroundServices;

public interface IWebCrawlQueue
{
    /// <summary>Enqueues a WebSource for crawling. Never blocks the calling HTTP request.</summary>
    void Enqueue(Guid webSourceId);

    /// <summary>
    /// Cancels an in-flight crawl (dashboard spec 16.11's "Cancel button" on the
    /// live-progress view). Returns false if this source isn't currently crawling
    /// (already finished, or never started) — the caller decides what to do with that.
    /// </summary>
    bool Cancel(Guid webSourceId);
}

/// <summary>
/// Sprint 4 web crawling: the direct equivalent of the spec's Hangfire
/// "[Queue("crawl")] WebCrawlJob" — this codebase has no Hangfire (its
/// background work is done via BackgroundService, see ImapPollingService /
/// BillingPeriodResetService), so a Channel-backed queue + a single
/// BackgroundService consumer is the idiomatic equivalent: POST handlers
/// enqueue and return immediately (202-style), the crawl itself runs off the
/// request thread with its own DI scope.
///
/// Concurrency is capped at <see cref="MaxConcurrentCrawls"/> via a semaphore
/// so a burst of "Add Web Source" submissions from many companies at once
/// can't spin up unbounded parallel crawls against unrelated third-party sites.
/// </summary>
public class WebCrawlQueueService : BackgroundService, IWebCrawlQueue
{
    private const int MaxConcurrentCrawls = 3;
    private const int AutomaticRetryAttempts = 2;
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(300)];

    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebCrawlQueueService> _logger;
    private readonly SemaphoreSlim _concurrencyLimiter = new(MaxConcurrentCrawls);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CancellationTokenSource> _inFlight = new();

    public WebCrawlQueueService(IServiceScopeFactory scopeFactory, ILogger<WebCrawlQueueService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public void Enqueue(Guid webSourceId)
    {
        if (!_channel.Writer.TryWrite(webSourceId))
            _logger.LogError("Failed to enqueue crawl job for WebSource {Id} — queue writer closed", webSourceId);
    }

    public bool Cancel(Guid webSourceId)
    {
        if (!_inFlight.TryGetValue(webSourceId, out var cts)) return false;
        cts.Cancel();
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var webSourceId in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            await _concurrencyLimiter.WaitAsync(stoppingToken);

            // Fire-and-forget per job so a slow crawl doesn't block the next
            // one from starting (up to MaxConcurrentCrawls in flight at once) —
            // same pattern as TicketService.SendNotificationInNewScopeAsync.
            _ = RunWithRetryAsync(webSourceId, stoppingToken)
                .ContinueWith(_ => _concurrencyLimiter.Release(), TaskScheduler.Default);
        }
    }

    private async Task RunWithRetryAsync(Guid webSourceId, CancellationToken hostToken)
    {
        for (var attempt = 0; attempt <= AutomaticRetryAttempts; attempt++)
        {
            using var scope = _scopeFactory.CreateScope();
            var crawler = scope.ServiceProvider.GetRequiredService<IWebCrawlerService>();

            // A fresh, cancellable-independently-of-shutdown token per attempt, registered
            // under this WebSource's id so a dashboard "Cancel" click can reach exactly this run.
            using var perCrawlCts = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
            _inFlight[webSourceId] = perCrawlCts;

            try
            {
                await crawler.CrawlAsync(webSourceId, perCrawlCts.Token);
                return;
            }
            catch (OperationCanceledException) when (perCrawlCts.IsCancellationRequested && !hostToken.IsCancellationRequested)
            {
                // User-initiated cancel, not app shutdown — WebCrawlerService's own
                // try/catch won't have persisted an Error status for an OperationCanceledException,
                // so do it here so the dashboard doesn't show a stuck "Crawling" forever.
                _logger.LogInformation("Crawl for WebSource {Id} was cancelled by the user", webSourceId);
                await MarkCancelledAsync(webSourceId);
                return;
            }
            catch (Exception ex) when (attempt < AutomaticRetryAttempts)
            {
                _logger.LogWarning(ex,
                    "Crawl job for WebSource {Id} failed (attempt {Attempt}/{Max}) — retrying in {Delay}",
                    webSourceId, attempt + 1, AutomaticRetryAttempts + 1, RetryDelays[attempt]);
                try { await Task.Delay(RetryDelays[attempt], hostToken); }
                catch (OperationCanceledException) { return; }
            }
            catch (Exception ex)
            {
                // WebCrawlerService.CrawlAsync already persists Status=Error/ErrorMessage
                // on failure — this final log is just for operator visibility.
                _logger.LogError(ex, "Crawl job for WebSource {Id} failed permanently after {Attempts} attempts",
                    webSourceId, AutomaticRetryAttempts + 1);
                return;
            }
            finally
            {
                _inFlight.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(webSourceId, perCrawlCts));
            }
        }
    }

    private async Task MarkCancelledAsync(Guid webSourceId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Api.Infrastructure.Persistence.AppDbContext>();
        var source = await db.WebSources.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == webSourceId);
        if (source is null) return;

        source.Status = Api.Domain.Enums.WebSourceStatus.Error;
        source.ErrorMessage = "Crawl was cancelled.";
        source.CurrentCrawlUrl = null;
        await db.SaveChangesAsync();
    }
}
