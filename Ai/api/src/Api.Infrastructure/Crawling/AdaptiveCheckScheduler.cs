using Api.Domain.Entities;
using Api.Domain.Enums;

namespace Api.Infrastructure.Crawling;

public interface IAdaptiveCheckScheduler
{
    /// <summary>
    /// Returns how long until <paramref name="page"/> should be checked again,
    /// honouring its WebSource's MonitoringMode (Adaptive / Fixed / Manual).
    /// Manual sources return a very long interval — ContentFreshnessBackgroundService
    /// won't pick them up automatically; only the "Check Now" action touches them.
    /// </summary>
    TimeSpan CalculateNextCheckInterval(WebPage page, WebSource source);
}

/// <summary>
/// Sprint 4 web crawling, spec 16.4: not all pages change at the same rate.
/// Tracks each page's historical change rate (ChangeCount / CheckCount) and
/// widens or narrows the check interval accordingly — checking volatile pages
/// (flash sales, live stock) far more often than static ones (About Us,
/// privacy policy) without any manual configuration per page.
/// </summary>
public class AdaptiveCheckScheduler : IAdaptiveCheckScheduler
{
    public TimeSpan CalculateNextCheckInterval(WebPage page, WebSource source)
    {
        if (source.MonitoringMode == WebSourceMonitoringMode.Manual)
            return TimeSpan.FromDays(3650); // effectively "never" — surfaced as Paused-like behaviour without changing Status

        if (source.MonitoringMode == WebSourceMonitoringMode.Fixed)
            return TimeSpan.FromHours(Math.Max(1, source.FixedIntervalHours ?? 24));

        // Adaptive (default / recommended):
        if (page.CheckCount < 5) return TimeSpan.FromHours(12);

        var rate = (double)page.ChangeCount / page.CheckCount;

        return rate switch
        {
            > 0.5  => TimeSpan.FromHours(6),
            > 0.3  => TimeSpan.FromHours(12),
            > 0.1  => TimeSpan.FromDays(1),
            > 0.02 => TimeSpan.FromDays(3),
            _      => TimeSpan.FromDays(7),
        };
    }
}
