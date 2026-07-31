using System.Text.Json;
using Api.Application.Abstractions;
using Api.Contracts.Webhooks;
using Api.Domain.Enums;
using Api.Filters;
using Api.Infrastructure.Channels;
using Api.Infrastructure.Security;
using Api.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Api.Controllers;

/// <summary>
/// Sprint 6: Facebook Messenger webhook. Structurally the same as
/// WhatsAppWebhookController (GET verification challenge, POST inbound
/// messages, always return 200 to Meta) but reading Messenger's different
/// payload shape and delegating the actual AI pipeline to the shared
/// ChatChannelPipelineService instead of duplicating it inline.
///
/// Sprint 8 security hardening: the POST endpoint is now protected by
/// VerifyMetaSignatureAttribute (X-Hub-Signature-256), same as WhatsApp — both
/// are Meta products verified against the same platform-wide Meta:AppSecret.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("webhook/messenger")]
public class MessengerWebhookController : ControllerBase
{
    private readonly IAppDbContext             _db;
    private readonly IChannelCredentialProtector _protector;
    private readonly IMessengerClient          _messenger;
    private readonly ChatChannelPipelineService _pipeline;
    private readonly IConfiguration            _configuration;
    private readonly ILogger<MessengerWebhookController> _logger;

    public MessengerWebhookController(
        IAppDbContext             db,
        IChannelCredentialProtector protector,
        IMessengerClient          messenger,
        ChatChannelPipelineService pipeline,
        IConfiguration            configuration,
        ILogger<MessengerWebhookController> logger)
    {
        _db            = db;
        _protector     = protector;
        _messenger     = messenger;
        _pipeline      = pipeline;
        _configuration = configuration;
        _logger        = logger;
    }

    [HttpGet("{companyId:guid}")]
    public IActionResult Verify(
        Guid companyId,
        [FromQuery(Name = "hub.mode")]         string? hubMode,
        [FromQuery(Name = "hub.verify_token")] string? hubVerifyToken,
        [FromQuery(Name = "hub.challenge")]    string? hubChallenge)
    {
        var expected = _configuration["Messenger:VerifyToken"];
        if (hubMode == "subscribe" && !string.IsNullOrEmpty(expected) && hubVerifyToken == expected)
            return Content(hubChallenge ?? string.Empty, "text/plain");

        _logger.LogWarning("Messenger verification failed | company={CompanyId}", companyId);
        return StatusCode(StatusCodes.Status403Forbidden);
    }

    [HttpPost("{companyId:guid}")]
    [VerifyMetaSignature]
    public async Task<IActionResult> Receive(
        Guid companyId,
        [FromBody] MessengerWebhookPayload payload,
        CancellationToken ct)
    {
        try
        {
            var inbound = (payload.Entry ?? [])
                .SelectMany(e => e.Messaging ?? [])
                .Where(m => !string.IsNullOrWhiteSpace(m.Sender?.Id)
                         && !string.IsNullOrWhiteSpace(m.Message?.Text))
                .ToList();

            if (inbound.Count == 0) return Ok();

            var connection = await _db.ChannelConnections
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c =>
                    c.CompanyId == companyId && c.Channel == ChannelType.Messenger, ct);

            if (connection is null || connection.Status != ChannelConnectionStatus.Active)
            {
                _logger.LogWarning("Messenger webhook: no active connection | company={CompanyId}", companyId);
                return Ok();
            }

            MessengerCredentials? credentials;
            try
            {
                credentials = JsonSerializer.Deserialize<MessengerCredentials>(
                    _protector.Decrypt(connection.CredentialsEncrypted));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt Messenger credentials | company={CompanyId}", companyId);
                return Ok();
            }

            if (credentials is null) return Ok();

            foreach (var msg in inbound)
            {
                var psid = msg.Sender!.Id!;
                var text = msg.Message!.Text!;

                try
                {
                    await _pipeline.ProcessInboundAsync(
                        companyId, ChannelType.Messenger, psid, text,
                        sendReplyAsync: (reply, sendCt) => _messenger.SendTextMessageAsync(credentials, psid, reply, sendCt),
                        ct: ct);
                }
                catch (MessengerApiException ex)
                {
                    _logger.LogError(ex, "Messenger send failed | company={CompanyId} psid={Psid}", companyId, psid);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing Messenger msg | company={CompanyId} psid={Psid}", companyId, psid);
                }
            }

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in Messenger webhook | company={CompanyId}", companyId);
            return Ok(); // always 200 to Meta
        }
    }
}
