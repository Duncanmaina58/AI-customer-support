using System.Text.Json;
using Api.Application.Abstractions;
using Api.Contracts.Channels;
using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.Channels;
using Api.Infrastructure.Security;
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
    private readonly IConfiguration _configuration;

    public ChannelsController(
        IAppDbContext db,
        ICurrentTenantProvider currentTenant,
        IChannelCredentialProtector protector,
        IWhatsAppClient whatsAppClient,
        IConfiguration configuration)
    {
        _db = db;
        _currentTenant = currentTenant;
        _protector = protector;
        _whatsAppClient = whatsAppClient;
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
            }
            catch (JsonException)
            {
                // Malformed metadata shouldn't ever happen (we control what's written),
                // but it's display-only info, so fail soft rather than 500 the request.
            }
        }

        return new ChannelConnectionDto(c.Id, c.Channel.ToString(), c.Status.ToString(), displayInfo, c.LastVerifiedAt, c.CreatedAt);
    }
}
