using Api.Application.Abstractions;
using Api.Domain.Enums;
using Api.Infrastructure.Channels;
using Api.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Api.Controllers;

/// <summary>
/// Sprint 5: receives inbound emails from Brevo's inbound email parsing service.
/// Only relevant to companies using the "Webhook" email inbound mode — see
/// EmailChannelMetadata and ChannelsController.ConnectEmail. Companies on the
/// "Imap" mode instead get their mail polled by ImapPollingService; both modes
/// funnel into the same EmailPipelineService so behaviour is identical either way.
///
/// Setup in Brevo dashboard (Webhook mode only, requires a paid Brevo plan):
///   Transactional → Inbound Parsing → Webhooks → Add
///     URL: https://your-api.com/webhook/email/{companyId}
///
/// Always returns HTTP 200 — Brevo retries on 4xx/5xx.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("webhook/email")]
public class EmailWebhookController : ControllerBase
{
    private readonly IAppDbContext         _db;
    private readonly EmailPipelineService  _pipeline;
    private readonly ILogger<EmailWebhookController> _logger;

    public EmailWebhookController(
        IAppDbContext          db,
        EmailPipelineService   pipeline,
        ILogger<EmailWebhookController> logger)
    {
        _db       = db;
        _pipeline = pipeline;
        _logger   = logger;
    }

    [HttpPost("{companyId:guid}")]
    public async Task<IActionResult> Receive(
        Guid companyId,
        [FromBody] BrevoInboundPayload payload,
        CancellationToken ct)
    {
        try
        {
            await ProcessEmailAsync(companyId, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in email webhook | company={CompanyId}", companyId);
        }

        return Ok(); // always 200 to prevent Brevo retries
    }

    private async Task ProcessEmailAsync(
        Guid companyId, BrevoInboundPayload payload, CancellationToken ct)
    {
        var fromEmail = payload.From?.Address;
        if (string.IsNullOrWhiteSpace(fromEmail))
        {
            _logger.LogWarning("Email webhook: missing From address | company={CompanyId}", companyId);
            return;
        }

        var messageText = payload.ExtractText();
        if (string.IsNullOrWhiteSpace(messageText))
        {
            _logger.LogDebug("Email webhook: empty body, skipping | company={CompanyId}", companyId);
            return;
        }

        var connection = await _db.ChannelConnections
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c =>
                c.CompanyId == companyId && c.Channel == ChannelType.Email, ct);

        if (connection is null || connection.Status != ChannelConnectionStatus.Active)
        {
            _logger.LogWarning("Email webhook: no active connection | company={CompanyId}", companyId);
            return;
        }

        if (EmailChannelMetadata.ReadMode(connection.MetadataJson) != EmailChannelMetadata.ModeWebhook)
        {
            // This company is on Imap mode — ImapPollingService owns their mail.
            // A Brevo webhook hit here would only happen from a stale/leftover
            // Brevo configuration; ignore rather than double-processing.
            _logger.LogWarning(
                "Email webhook: hit for a non-Webhook-mode connection, ignoring | company={CompanyId}", companyId);
            return;
        }

        await _pipeline.ProcessInboundEmailAsync(
            companyId:  companyId,
            connection: connection,
            fromEmail:  fromEmail,
            fromName:   payload.From?.Name,
            subject:    payload.Subject ?? "(no subject)",
            messageId:  payload.MessageId ?? $"generated-{Guid.NewGuid():N}@platform",
            inReplyTo:  payload.InReplyTo,
            bodyText:   messageText,
            ct:         ct);
    }
}
