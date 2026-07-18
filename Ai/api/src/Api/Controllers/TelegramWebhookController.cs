using System.Text.Json;
using Api.Application.Abstractions;
using Api.Contracts.Webhooks;
using Api.Domain.Enums;
using Api.Infrastructure.Channels;
using Api.Infrastructure.Security;
using Api.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Api.Controllers;

/// <summary>
/// Sprint 6: Telegram bot webhook. No GET verification handshake like Meta's
/// channels — Telegram simply starts POSTing Updates once ConnectTelegram
/// calls setWebhook. Same "always 200, log and move on" resilience pattern as
/// the other webhook controllers, and delegates the AI pipeline to
/// ChatChannelPipelineService.
///
/// Sprint 8 security hardening: inbound requests are now verified via
/// Telegram's secret_token mechanism (set during setWebhook, checked here
/// against the X-Telegram-Bot-Api-Secret-Token header) — see FixedTimeEquals
/// below. Connections made before this hardening (SecretToken == null) are
/// still accepted, but logged, until the client reconnects the bot.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("webhook/telegram")]
public class TelegramWebhookController : ControllerBase
{
    private readonly IAppDbContext             _db;
    private readonly IChannelCredentialProtector _protector;
    private readonly ITelegramClient           _telegram;
    private readonly ChatChannelPipelineService _pipeline;
    private readonly ILogger<TelegramWebhookController> _logger;

    public TelegramWebhookController(
        IAppDbContext             db,
        IChannelCredentialProtector protector,
        ITelegramClient           telegram,
        ChatChannelPipelineService pipeline,
        ILogger<TelegramWebhookController> logger)
    {
        _db        = db;
        _protector = protector;
        _telegram  = telegram;
        _pipeline  = pipeline;
        _logger    = logger;
    }

    [HttpPost("{companyId:guid}")]
    public async Task<IActionResult> Receive(
        Guid companyId,
        [FromBody] TelegramUpdate update,
        CancellationToken ct)
    {
        try
        {
            var chatId = update.Message?.Chat?.Id;
            var text   = update.Message?.Text;

            if (chatId is null || string.IsNullOrWhiteSpace(text))
                return Ok();

            var connection = await _db.ChannelConnections
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c =>
                    c.CompanyId == companyId && c.Channel == ChannelType.Telegram, ct);

            if (connection is null || connection.Status != ChannelConnectionStatus.Active)
            {
                _logger.LogWarning("Telegram webhook: no active connection | company={CompanyId}", companyId);
                return Ok();
            }

            TelegramCredentials? credentials;
            try
            {
                credentials = JsonSerializer.Deserialize<TelegramCredentials>(
                    _protector.Decrypt(connection.CredentialsEncrypted));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt Telegram credentials | company={CompanyId}", companyId);
                return Ok();
            }

            if (credentials is null) return Ok();

            // Sprint 8 security hardening: verify this POST actually came from Telegram.
            // Connections made before this hardening have SecretToken == null — treated as
            // "not yet re-connected", logged, and allowed through rather than breaking them.
            if (credentials.SecretToken is not null)
            {
                var provided = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
                if (!Api.Infrastructure.Security.MetaWebhookSignature.FixedTimeEquals(provided, credentials.SecretToken))
                {
                    _logger.LogWarning(
                        "Telegram webhook: secret_token mismatch — rejecting | company={CompanyId}", companyId);
                    return Unauthorized();
                }
            }
            else
            {
                _logger.LogWarning(
                    "Telegram webhook: connection has no SecretToken (connected before Sprint 8 hardening) — " +
                    "accepting unverified. Reconnect this bot in Settings to enable verification. | company={CompanyId}",
                    companyId);
            }

            var chatIdValue = chatId.Value;
            var customerId  = chatIdValue.ToString();

            try
            {
                await _pipeline.ProcessInboundAsync(
                    companyId, ChannelType.Telegram, customerId, text,
                    sendReplyAsync: (reply, sendCt) => _telegram.SendTextMessageAsync(credentials, chatIdValue, reply, sendCt),
                    ct: ct);
            }
            catch (TelegramApiException ex)
            {
                _logger.LogError(ex, "Telegram send failed | company={CompanyId} chatId={ChatId}", companyId, chatIdValue);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Telegram msg | company={CompanyId} chatId={ChatId}", companyId, chatIdValue);
            }

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in Telegram webhook | company={CompanyId}", companyId);
            return Ok(); // always 200 — Telegram disables the webhook after too many consecutive errors
        }
    }
}
