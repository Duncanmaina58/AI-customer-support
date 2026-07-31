using System.Text.Json;
using Api.Application.Abstractions;
using Api.Contracts.Channels;
using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.Channels;
using Api.Infrastructure.Security;
using Api.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/channels")]
public class ChannelsController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly ICurrentTenantProvider _currentTenant;
    private readonly IChannelCredentialProtector _protector;
    private readonly IWhatsAppClient _whatsAppClient;
    private readonly IImapSmtpEmailClient _imapSmtp;
    private readonly IMessengerClient _messenger;
    private readonly ITelegramClient _telegram;
    private readonly IConfiguration _configuration;

    public ChannelsController(
        IAppDbContext db,
        ICurrentTenantProvider currentTenant,
        IChannelCredentialProtector protector,
        IWhatsAppClient whatsAppClient,
        IImapSmtpEmailClient imapSmtp,
        IMessengerClient messenger,
        ITelegramClient telegram,
        IConfiguration configuration)
    {
        _db = db;
        _currentTenant = currentTenant;
        _protector = protector;
        _whatsAppClient = whatsAppClient;
        _imapSmtp = imapSmtp;
        _messenger = messenger;
        _telegram = telegram;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ChannelConnectionDto>>> List(CancellationToken ct)
    {
        var connections = await _db.ChannelConnections
            .AsNoTracking()
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        return Ok(connections.Select(ToDto).ToList());
    }

    /// <summary>
    /// Onboarding wizard step 4 (WhatsApp option): verifies the token + phone number
    /// id actually work against Meta's Graph API before saving anything, then stores
    /// them AES-encrypted. Reconnecting (e.g. to rotate a token) overwrites the
    /// existing WhatsApp connection rather than creating a second one - see the
    /// unique (CompanyId, Channel) index in ChannelConnectionConfiguration.
    /// </summary>
    [HttpPost("whatsapp")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<ChannelConnectionDto>> ConnectWhatsApp(ConnectWhatsAppRequest request, CancellationToken ct)
    {
        if (_currentTenant.CompanyId is not { } companyId)
        {
            return Unauthorized(new { message = "No company context on this request." });
        }

        if (string.IsNullOrWhiteSpace(request.AccessToken) || string.IsNullOrWhiteSpace(request.PhoneNumberId))
        {
            return BadRequest(new { message = "Access token and phone number id are both required." });
        }

        var credentials = new WhatsAppCredentials(request.AccessToken.Trim(), request.PhoneNumberId.Trim());
        var verifyResult = await _whatsAppClient.VerifyAsync(credentials, ct);

        if (!verifyResult.Success)
        {
            return BadRequest(new
            {
                message = "Couldn't verify those WhatsApp credentials with Meta.",
                detail = verifyResult.ErrorMessage,
            });
        }

        var existing = await _db.ChannelConnections
            .FirstOrDefaultAsync(c => c.Channel == ChannelType.WhatsApp, ct);

        var encrypted = _protector.Encrypt(JsonSerializer.Serialize(credentials));
        var metadata = JsonSerializer.Serialize(new { displayPhoneNumber = verifyResult.DisplayPhoneNumber });

        if (existing is null)
        {
            existing = new ChannelConnection
            {
                CompanyId = companyId,
                Channel = ChannelType.WhatsApp,
            };
            _db.ChannelConnections.Add(existing);
        }

        existing.CredentialsEncrypted = encrypted;
        existing.MetadataJson = metadata;
        existing.Status = ChannelConnectionStatus.Active;
        existing.LastVerifiedAt = DateTime.UtcNow;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(existing));
    }

    /// <summary>Sends a real WhatsApp message via the connected number - proves the outbound half of the integration actually works end-to-end.</summary>
    [HttpPost("whatsapp/test-message")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<IActionResult> SendWhatsAppTestMessage(SendWhatsAppTestMessageRequest request, CancellationToken ct)
    {
        var connection = await _db.ChannelConnections
            .FirstOrDefaultAsync(c => c.Channel == ChannelType.WhatsApp, ct);

        if (connection is null || connection.Status != ChannelConnectionStatus.Active)
        {
            return BadRequest(new { message = "Connect WhatsApp before sending a test message." });
        }

        var credentials = JsonSerializer.Deserialize<WhatsAppCredentials>(_protector.Decrypt(connection.CredentialsEncrypted));
        if (credentials is null)
        {
            return Problem("Stored WhatsApp credentials could not be read.", statusCode: 500);
        }

        var messageBody = string.IsNullOrWhiteSpace(request.Message)
            ? "This is a test message from your AI Support Platform setup. If you received this, WhatsApp is connected!"
            : request.Message;

        try
        {
            await _whatsAppClient.SendTextMessageAsync(credentials, request.ToPhoneNumber, messageBody, ct);
        }
        catch (WhatsAppApiException ex)
        {
            return BadRequest(new { message = "WhatsApp rejected the message.", detail = ex.Message });
        }

        return NoContent();
    }

    /// <summary>
    /// Sprint 6: connects Facebook Messenger. Same verify-before-save pattern as
    /// WhatsApp — see MessengerClient's doc comment for why this is a
    /// paste-a-token flow rather than a full OAuth redirect.
    /// </summary>
    [HttpPost("messenger")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<ChannelConnectionDto>> ConnectMessenger(ConnectMessengerRequest request, CancellationToken ct)
    {
        if (_currentTenant.CompanyId is not { } companyId)
            return Unauthorized(new { message = "No company context on this request." });

        if (string.IsNullOrWhiteSpace(request.PageAccessToken) || string.IsNullOrWhiteSpace(request.PageId))
            return BadRequest(new { message = "Page access token and page id are both required." });

        var credentials = new MessengerCredentials(request.PageAccessToken.Trim(), request.PageId.Trim());
        var verifyResult = await _messenger.VerifyAsync(credentials, ct);

        if (!verifyResult.Success)
        {
            return BadRequest(new
            {
                message = "Couldn't verify those Messenger credentials with Meta.",
                detail = verifyResult.ErrorMessage,
            });
        }

        var existing = await _db.ChannelConnections
            .FirstOrDefaultAsync(c => c.Channel == ChannelType.Messenger, ct);

        var encrypted = _protector.Encrypt(JsonSerializer.Serialize(credentials));
        var metadata = JsonSerializer.Serialize(new { displayPageName = verifyResult.PageName });

        if (existing is null)
        {
            existing = new ChannelConnection { CompanyId = companyId, Channel = ChannelType.Messenger };
            _db.ChannelConnections.Add(existing);
        }

        existing.CredentialsEncrypted = encrypted;
        existing.MetadataJson = metadata;
        existing.Status = ChannelConnectionStatus.Active;
        existing.LastVerifiedAt = DateTime.UtcNow;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(existing));
    }

    /// <summary>
    /// Sprint 6: connects a Telegram bot. Unlike every other channel, there's
    /// nothing to paste into a third-party dashboard — this endpoint calls
    /// Telegram's setWebhook itself right after verifying the token, so the bot
    /// starts working the instant this call succeeds.
    /// </summary>
    [HttpPost("telegram")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<ChannelConnectionDto>> ConnectTelegram(ConnectTelegramRequest request, CancellationToken ct)
    {
        if (_currentTenant.CompanyId is not { } companyId)
            return Unauthorized(new { message = "No company context on this request." });

        if (string.IsNullOrWhiteSpace(request.BotToken))
            return BadRequest(new { message = "Bot token is required." });

        // Sprint 8 security hardening: a per-connection random secret that Telegram
        // will echo back on every webhook POST, letting us verify authenticity.
        var secretToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var credentials = new TelegramCredentials(request.BotToken.Trim(), secretToken);
        var verifyResult = await _telegram.VerifyAsync(credentials, ct);

        if (!verifyResult.Success)
        {
            return BadRequest(new
            {
                message = "Couldn't verify that bot token with Telegram.",
                detail = verifyResult.ErrorMessage,
            });
        }

        var webhookUrl = $"{Request.Scheme}://{Request.Host}/webhook/telegram/{companyId}";
        var webhookRegistered = await _telegram.SetWebhookAsync(credentials, webhookUrl, secretToken, ct);
        if (!webhookRegistered)
        {
            return BadRequest(new
            {
                message = "Bot token is valid, but Telegram rejected the webhook registration. Try again in a moment.",
            });
        }

        var existing = await _db.ChannelConnections
            .FirstOrDefaultAsync(c => c.Channel == ChannelType.Telegram, ct);

        var encrypted = _protector.Encrypt(JsonSerializer.Serialize(credentials));
        var metadata = JsonSerializer.Serialize(new { displayBotUsername = verifyResult.BotUsername });

        if (existing is null)
        {
            existing = new ChannelConnection { CompanyId = companyId, Channel = ChannelType.Telegram };
            _db.ChannelConnections.Add(existing);
        }

        existing.CredentialsEncrypted = encrypted;
        existing.MetadataJson = metadata;
        existing.Status = ChannelConnectionStatus.Active;
        existing.LastVerifiedAt = DateTime.UtcNow;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(existing));
    }
    ///
    /// The company provides the email address and display name they want as the
    /// sender (e.g. "support@acme.com" / "Acme Support"). Actual sending uses the
    /// PLATFORM's Brevo API key — no per-company Brevo account required.
    ///
    /// After connecting, the agent must configure Brevo inbound parsing to POST to
    /// the webhook URL returned in this response.
    /// </summary>
    /// <summary>
    /// Sprint 5 follow-up: two inbound modes, client's choice —
    ///   Webhook: Brevo inbound parsing (simple, but requires a paid Brevo plan)
    ///   Imap:    MailKit polls any regular mailbox (free, works with Gmail,
    ///            Outlook, Zoho, cPanel hosting, etc.) — see ImapPollingService.
    /// Imap mode is verified (real IMAP + SMTP connect/auth) before saving,
    /// mirroring ConnectWhatsApp's verify-before-save pattern, so a typo'd
    /// password fails immediately with a clear message instead of silently
    /// breaking the background poller later. Webhook mode has nothing to verify
    /// synchronously — Brevo will start POSTing once the agent finishes wiring
    /// up the webhook URL this endpoint returns.
    /// </summary>
    [HttpPost("email")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<ConnectEmailResponse>> ConnectEmail(
        [FromBody] ConnectEmailRequest request,
        CancellationToken ct)
    {
        if (_currentTenant.CompanyId is not { } companyId)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.SenderEmail))
            return BadRequest(new { message = "SenderEmail is required." });

        if (string.IsNullOrWhiteSpace(request.SenderName))
            return BadRequest(new { message = "SenderName is required." });

        var mode = request.Mode?.Trim();
        if (mode != EmailChannelMetadata.ModeImap && mode != EmailChannelMetadata.ModeWebhook)
            return BadRequest(new { message = "Mode must be 'Webhook' or 'Imap'." });

        var senderEmail = request.SenderEmail.Trim();
        var senderName  = request.SenderName.Trim();
        string credJson;
        string? webhookUrl = null;

        if (mode == EmailChannelMetadata.ModeImap)
        {
            if (string.IsNullOrWhiteSpace(request.ImapHost) || request.ImapPort is null
                || string.IsNullOrWhiteSpace(request.SmtpHost) || request.SmtpPort is null
                || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new
                {
                    message = "IMAP host/port, SMTP host/port, username, and password are all required for IMAP mode.",
                });
            }

            var imapCredentials = new ImapChannelCredentials(
                request.ImapHost.Trim(), request.ImapPort.Value,
                request.SmtpHost.Trim(), request.SmtpPort.Value,
                request.Username.Trim(), request.Password,
                senderName);

            var verifyResult = await _imapSmtp.VerifyAsync(imapCredentials, ct);
            if (!verifyResult.Success)
            {
                return BadRequest(new
                {
                    message = "Couldn't verify those IMAP/SMTP credentials.",
                    detail  = verifyResult.ErrorMessage,
                });
            }

            credJson = JsonSerializer.Serialize(imapCredentials);
        }
        else
        {
            var credentials = new EmailChannelCredentials(senderEmail, senderName);
            credJson   = JsonSerializer.Serialize(credentials);
            webhookUrl = $"{Request.Scheme}://{Request.Host}/webhook/email/{companyId}";
        }

        var metadataJson = JsonSerializer.Serialize(new
        {
            inboundMode = mode,
            displayEmail = senderEmail,
            senderEmail,
            senderName,
            // Sprint 5 follow-up fix: only meaningful for Imap mode — mail
            // received before this moment is never processed, so a mailbox's
            // pre-existing unread backlog doesn't flood in as new
            // conversations. Reset on every (re)connect on purpose: if a
            // channel is disconnected and reconnected later, whatever arrived
            // during the gap is backlog too, not "new".
            processingStartedAtUtc = mode == EmailChannelMetadata.ModeImap
                ? DateTime.UtcNow.ToString("O")
                : null,
        });

        var existing = await _db.ChannelConnections
            .FirstOrDefaultAsync(c => c.Channel == ChannelType.Email, ct);

        if (existing is null)
        {
            existing = new ChannelConnection
            {
                CompanyId = companyId,
                Channel   = ChannelType.Email,
            };
            _db.ChannelConnections.Add(existing);
        }

        existing.CredentialsEncrypted = _protector.Encrypt(credJson);
        existing.MetadataJson         = metadataJson;
        existing.Status         = ChannelConnectionStatus.Active;
        existing.LastVerifiedAt = DateTime.UtcNow;
        existing.UpdatedAt      = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(new ConnectEmailResponse(ToDto(existing), webhookUrl));
    }

    /// <summary>
    /// Onboarding wizard step 4 (Web Chat option): no external credentials needed -
    /// this just activates the connection and hands back the script tag to paste
    /// into the customer's website. The script tag points at widget-loader.js,
    /// which embeds an iframe running WidgetPage.tsx (web/src/routes/widget) — that
    /// page connects to ChatHub over SignalR and is the full Sprint 3 chat
    /// experience: real-time send, streaming AI tokens, conversation persistence.
    /// </summary>
    [HttpPost("webchat")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<ConnectWebChatResponse>> ConnectWebChat(CancellationToken ct)
    {
        if (_currentTenant.CompanyId is not { } companyId)
        {
            return Unauthorized(new { message = "No company context on this request." });
        }

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null)
        {
            return NotFound();
        }

        var existing = await _db.ChannelConnections
            .FirstOrDefaultAsync(c => c.Channel == ChannelType.WebChat, ct);

        if (existing is null)
        {
            existing = new ChannelConnection
            {
                CompanyId = companyId,
                Channel = ChannelType.WebChat,
                CredentialsEncrypted = _protector.Encrypt("{}"), // no external secret to store yet
            };
            _db.ChannelConnections.Add(existing);
        }

        existing.Status = ChannelConnectionStatus.Active;
        existing.LastVerifiedAt = DateTime.UtcNow;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        // Widget:BaseUrl is the React app's own origin (the same app this API serves
        // alongside) — widget-loader.js and /widget/chat are both served from there.
        // Defaults to the local Vite dev server so the generated tag works out of
        // the box when following the README's local setup instructions.
        var widgetBaseUrl = (_configuration["Widget:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
        var scriptTag = $"<script src=\"{widgetBaseUrl}/widget-loader.js\" data-key=\"{company.PublicApiKey}\" async></script>";

        return Ok(new ConnectWebChatResponse(ToDto(existing), scriptTag));
    }

    [HttpPost("{id:guid}/disconnect")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<ChannelConnectionDto>> Disconnect(Guid id, CancellationToken ct)
    {
        var connection = await _db.ChannelConnections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (connection is null)
        {
            return NotFound();
        }

        connection.Status = ChannelConnectionStatus.Paused;
        connection.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(connection));
    }

    private static ChannelConnectionDto ToDto(ChannelConnection c)
    {
        string? displayInfo = null;
        if (!string.IsNullOrWhiteSpace(c.MetadataJson))
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<JsonElement>(c.MetadataJson);
                if (metadata.TryGetProperty("displayPhoneNumber", out var phone))
                {
                    displayInfo = phone.GetString();
                }
                else if (metadata.TryGetProperty("displayEmail", out var email))
                {
                    // Sprint 5: Email channel connections store { displayEmail } instead
                    // of { displayPhoneNumber } — without this branch, a connected Email
                    // channel always rendered with no display info in the dashboard.
                    displayInfo = email.GetString();
                }
                else if (metadata.TryGetProperty("displayPageName", out var pageName))
                {
                    displayInfo = pageName.GetString();
                }
                else if (metadata.TryGetProperty("displayBotUsername", out var botUsername))
                {
                    var username = botUsername.GetString();
                    displayInfo = string.IsNullOrEmpty(username) ? null : $"@{username}";
                }
            }
            catch (JsonException)
            {
                // Malformed metadata shouldn't ever happen (we control what's written),
                // but it's display-only info, so fail soft rather than 500 the request.
            }
        }

        string? inboundMode = c.Channel == ChannelType.Email
            ? Api.Infrastructure.Services.EmailChannelMetadata.ReadMode(c.MetadataJson)
            : null;

        return new ChannelConnectionDto(
            c.Id, c.Channel.ToString(), c.Status.ToString(), displayInfo, c.LastVerifiedAt, c.CreatedAt, inboundMode);
    }
}
