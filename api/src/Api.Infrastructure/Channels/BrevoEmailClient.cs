using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Channels;

// ---------------------------------------------------------------------------
// Credentials stored per company (AES-256 encrypted) in ChannelConnections
// ---------------------------------------------------------------------------

/// <summary>
/// Stored encrypted in ChannelConnection.CredentialsEncrypted for each company
/// that connects the Email channel. Only the display name and sender address are
/// stored here — actual sending uses the PLATFORM's Brevo API key (in appsettings),
/// so companies don't need their own Brevo account.
/// </summary>
public record EmailChannelCredentials(
    string SenderEmail,
    string SenderName);

// ---------------------------------------------------------------------------
// Inbound webhook payload (Brevo → your API)
// ---------------------------------------------------------------------------

/// <summary>
/// Brevo's inbound email parsing service POSTs this structure to
/// POST /webhook/email/{companyId} when a new email arrives.
/// Brevo docs: https://developers.brevo.com/docs/inbound-parsing-webhooks
/// </summary>
public class BrevoInboundPayload
{
    [JsonPropertyName("Uuid")]        public List<string>?            Uuid        { get; set; }
    [JsonPropertyName("MessageId")]   public string?                  MessageId   { get; set; }
    [JsonPropertyName("InReplyTo")]   public string?                  InReplyTo   { get; set; }
    [JsonPropertyName("From")]        public BrevoEmailContact?       From        { get; set; }
    [JsonPropertyName("To")]          public List<BrevoEmailContact>? To          { get; set; }
    [JsonPropertyName("ReplyTo")]     public BrevoEmailContact?       ReplyTo     { get; set; }
    [JsonPropertyName("Subject")]     public string?                  Subject     { get; set; }
    [JsonPropertyName("RawTextBody")] public string?                  RawTextBody { get; set; }
    [JsonPropertyName("RawHtmlBody")] public string?                  RawHtmlBody { get; set; }
    [JsonPropertyName("SentAtDate")]  public string?                  SentAtDate  { get; set; }
    [JsonPropertyName("SpamScore")]   public string?                  SpamScore   { get; set; }

    /// <summary>
    /// Extracts plain text for the AI pipeline.
    /// Prefers RawTextBody; falls back to stripping HTML tags from RawHtmlBody.
    /// </summary>
    public string ExtractText()
    {
        if (!string.IsNullOrWhiteSpace(RawTextBody))
            return RawTextBody.Trim();

        if (!string.IsNullOrWhiteSpace(RawHtmlBody))
        {
            return System.Text.RegularExpressions.Regex
                .Replace(RawHtmlBody, "<[^>]+>", " ")
                .Replace("&nbsp;", " ")
                .Replace("&amp;",  "&")
                .Replace("&lt;",   "<")
                .Replace("&gt;",   ">")
                .Replace("&quot;", "\"")
                .Trim();
        }

        return string.Empty;
    }
}

public class BrevoEmailContact
{
    [JsonPropertyName("Name")]    public string? Name    { get; set; }
    [JsonPropertyName("Address")] public string? Address { get; set; }
}

// ---------------------------------------------------------------------------
// Outbound email
// ---------------------------------------------------------------------------

/// <summary>
/// Parameters for one Brevo transactional email send.
/// Threading headers are optional but critical — without them, every AI reply
/// appears as a separate thread in the customer's mail client.
///
/// Auth hardening: HtmlContent is optional — conversation-reply emails (the
/// original use of this record) stay plain text (matches how a real support
/// reply reads in a mail client), while account emails (verification, reset,
/// welcome, security alerts) supply a branded HtmlContent body via
/// AuthEmailTemplates. Brevo accepts both in the same payload; most clients
/// render the HTML part when present and fall back to TextContent otherwise.
/// </summary>
public record BrevoOutboundEmail(
    string  SenderName,
    string  SenderEmail,
    string  ToEmail,
    string? ToName,
    string  Subject,
    string  TextContent,
    string? InReplyTo    = null,
    string? References   = null,
    string? ReplyToEmail = null,
    string? HtmlContent  = null);

// ---------------------------------------------------------------------------
// Interface + implementation
// ---------------------------------------------------------------------------

public interface IBrevoEmailClient
{
    Task SendAsync(BrevoOutboundEmail email, CancellationToken ct = default);
}

/// <summary>
/// Sends transactional emails via Brevo's v3 REST API (POST /v3/smtp/email).
///
/// Auth: Brevo uses an "api-key" request header, NOT "Authorization: Bearer".
/// One platform-wide key (Brevo:ApiKey in appsettings) is used for all companies.
/// Companies configure their sender email/name when connecting the Email channel
/// through the dashboard — no per-company Brevo account required.
///
/// Brevo API reference: https://developers.brevo.com/reference/sendtransacemail
/// </summary>
public class BrevoEmailClient : IBrevoEmailClient
{
    private const string SendEndpoint = "v3/smtp/email";

    private readonly HttpClient              _http;
    private readonly IConfiguration          _cfg;
    private readonly ILogger<BrevoEmailClient> _logger;

    public BrevoEmailClient(HttpClient http, IConfiguration cfg, ILogger<BrevoEmailClient> logger)
    {
        _http   = http;
        _cfg    = cfg;
        _logger = logger;
    }

    public async Task SendAsync(BrevoOutboundEmail email, CancellationToken ct = default)
    {
        var apiKey = _cfg["Brevo:ApiKey"]
            ?? throw new InvalidOperationException(
                "Brevo:ApiKey is not configured. " +
                "Get a free key at https://app.brevo.com/settings/keys/api and add it to " +
                "appsettings.Development.json or the BREVO__ApiKey environment variable.");

        // Only build the headers dictionary when threading values are actually present.
        Dictionary<string, string>? headers = null;
        if (!string.IsNullOrEmpty(email.InReplyTo) || !string.IsNullOrEmpty(email.References))
        {
            headers = [];
            if (!string.IsNullOrEmpty(email.InReplyTo))
                headers["In-Reply-To"] = email.InReplyTo;
            if (!string.IsNullOrEmpty(email.References))
                headers["References"] = email.References;
        }

        var payload = new
        {
            sender      = new { name = email.SenderName,  email = email.SenderEmail },
            to          = new[] { new { email = email.ToEmail, name = email.ToName ?? string.Empty } },
            replyTo     = string.IsNullOrEmpty(email.ReplyToEmail)
                              ? (object?)null
                              : new { email = email.ReplyToEmail },
            subject     = email.Subject,
            textContent = email.TextContent,
            htmlContent = email.HtmlContent,
            headers,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, SendEndpoint)
        {
            Content = JsonContent.Create(payload),
        };
        // Brevo requires the API key in this exact header name.
        request.Headers.Add("api-key", apiKey);

        using var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Brevo send failed {Status}: {Body} | to={To} subject={Subject}",
                (int)response.StatusCode, body, email.ToEmail, email.Subject);
            throw new BrevoEmailException(
                $"Brevo API error {(int)response.StatusCode}: {body}");
        }

        _logger.LogInformation(
            "Brevo email sent | to={To} subject={Subject}", email.ToEmail, email.Subject);
    }
}

public class BrevoEmailException : Exception
{
    public BrevoEmailException(string message) : base(message) { }
}
