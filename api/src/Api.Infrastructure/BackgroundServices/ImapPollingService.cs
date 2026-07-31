using Api.Domain.Enums;
using Api.Infrastructure.Channels;
using Api.Infrastructure.Persistence;
using Api.Infrastructure.Security;
using Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.BackgroundServices;

/// <summary>
/// Sprint 5 follow-up: the free alternative to Brevo's inbound parsing webhook
/// (which requires a paid Brevo plan — see ImapChannelCredentials' doc comment).
/// Every `Email:PollingIntervalSeconds` (default 30s), checks every company's
/// Email channel connection that's set to Imap mode for unread mail, and feeds
/// each one through EmailPipelineService — the exact same pipeline
/// EmailWebhookController uses for Brevo, so behaviour (RAG, escalation,
/// ticketing) is identical regardless of which inbound mode a company picked.
///
/// A fresh DbContext + service scope is created per poll cycle (standard
/// BackgroundService pattern — see TicketService.SendNotificationInNewScopeAsync
/// for the same reasoning: a singleton-lifetime background service can't hold a
/// scoped AppDbContext directly).
/// </summary>
public class ImapPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImapPollingService> _logger;
    private readonly TimeSpan _interval;

    public ImapPollingService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ImapPollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;

        var seconds = configuration.GetValue<int?>("Email:PollingIntervalSeconds") ?? 30;
        _interval = TimeSpan.FromSeconds(Math.Max(seconds, 10)); // 10s floor — avoid hammering mail servers on a bad config value
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small startup delay so this doesn't compete with the rest of the app
        // for resources during boot.
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAllCompaniesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // A whole-cycle failure (e.g. DB unreachable) shouldn't kill the
                // service permanently — log and retry next interval.
                _logger.LogError(ex, "IMAP polling cycle failed");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PollAllCompaniesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db        = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IChannelCredentialProtector>();
        var fetcher   = scope.ServiceProvider.GetRequiredService<ImapMailFetcher>();
        var pipeline  = scope.ServiceProvider.GetRequiredService<EmailPipelineService>();

        var connections = await db.ChannelConnections
            .IgnoreQueryFilters() // background service has no HTTP tenant context
            .Where(c => c.Channel == ChannelType.Email && c.Status == ChannelConnectionStatus.Active)
            .ToListAsync(ct);

        foreach (var connection in connections)
        {
            if (EmailChannelMetadata.ReadMode(connection.MetadataJson) != EmailChannelMetadata.ModeImap)
            {
                continue; // Webhook-mode connections are handled by EmailWebhookController instead
            }

            try
            {
                var credentials = EmailChannelMetadata.DecryptImapCredentials(connection, protector);

                var sinceUtc = EmailChannelMetadata.ReadProcessingStartedAtUtc(connection.MetadataJson);
                if (sinceUtc is null)
                {
                    // Legacy connection made before this fix existed — back-fill
                    // "now" as the cutoff so its pre-existing unread backlog
                    // (which could be months or years old) is never processed,
                    // on this poll or any future one.
                    var now = DateTime.UtcNow;
                    connection.MetadataJson = EmailChannelMetadata.WithProcessingStartedAtUtc(connection.MetadataJson, now);
                    await db.SaveChangesAsync(ct);
                    sinceUtc = now;

                    _logger.LogInformation(
                        "IMAP connection had no processing cutoff set — backfilled to now | company={CompanyId}",
                        connection.CompanyId);
                }

                var emails = await fetcher.FetchUnseenAsync(credentials, sinceUtc.Value, ct);

                foreach (var email in emails)
                {
                    try
                    {
                        await pipeline.ProcessInboundEmailAsync(
                            companyId:  connection.CompanyId,
                            connection: connection,
                            fromEmail:  email.FromEmail,
                            fromName:   email.FromName,
                            subject:    email.Subject,
                            messageId:  email.MessageId,
                            inReplyTo:  email.InReplyTo,
                            bodyText:   email.BodyText,
                            ct:         ct);
                    }
                    catch (Exception ex)
                    {
                        // One malformed/failing email shouldn't stop the rest of
                        // this mailbox's batch from being processed.
                        _logger.LogError(ex,
                            "Failed to process IMAP email | company={CompanyId} from={From}",
                            connection.CompanyId, email.FromEmail);
                    }
                }

                if (emails.Count > 0)
                {
                    _logger.LogInformation(
                        "IMAP poll processed {Count} email(s) | company={CompanyId}",
                        emails.Count, connection.CompanyId);
                }
            }
            catch (Exception ex)
            {
                // One company's bad password / unreachable server shouldn't stop
                // polling for everyone else.
                _logger.LogError(ex,
                    "IMAP poll failed | company={CompanyId} connection={ConnectionId}",
                    connection.CompanyId, connection.Id);
            }
        }
    }
}
