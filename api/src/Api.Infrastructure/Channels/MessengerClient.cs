using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Api.Infrastructure.Channels;

public record MessengerCredentials(string PageAccessToken, string PageId);

public record MessengerVerifyResult(bool Success, string? PageName, string? ErrorMessage);

/// <summary>
/// Thin wrapper over Meta's Graph API for Facebook Messenger — structurally
/// identical to WhatsAppClient (same Graph API, same auth style, same
/// verify-then-send shape) since Messenger and WhatsApp Business are both
/// products under the same Meta Graph API.
///
/// Connection deviation from the Phase 1 doc, matching an existing precedent
/// in this codebase: the doc describes a full OAuth redirect flow ("client
/// clicks 'Connect with Facebook'"), but WhatsApp — the other Meta channel —
/// already uses a simpler "paste your access token, we verify it" pattern
/// instead of OAuth in this codebase (no registered Facebook App/OAuth secret
/// exists to redirect through). Messenger follows the same, already-established
/// pattern for consistency: paste a Page Access Token (generated in Meta for
/// Developers → your app → Messenger → Access Tokens), verified against the
/// Graph API before saving.
/// </summary>
public interface IMessengerClient
{
    Task<MessengerVerifyResult> VerifyAsync(MessengerCredentials credentials, CancellationToken ct = default);
    Task SendTextMessageAsync(MessengerCredentials credentials, string recipientPsid, string body, CancellationToken ct = default);
}

public class MessengerClient : IMessengerClient
{
    private const string GraphApiVersion = "v19.0";

    private readonly HttpClient _httpClient;

    public MessengerClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<MessengerVerifyResult> VerifyAsync(MessengerCredentials credentials, CancellationToken ct = default)
    {
        var url = $"https://graph.facebook.com/{GraphApiVersion}/{credentials.PageId}?fields=name&access_token={Uri.EscapeDataString(credentials.PageAccessToken)}";

        using var response = await _httpClient.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await SafeReadErrorAsync(response, ct);
            return new MessengerVerifyResult(false, null, errorBody);
        }

        var payload = await response.Content.ReadFromJsonAsync<PageLookupResponse>(cancellationToken: ct);
        return new MessengerVerifyResult(true, payload?.Name, null);
    }

    public async Task SendTextMessageAsync(
        MessengerCredentials credentials, string recipientPsid, string body, CancellationToken ct = default)
    {
        var url = $"https://graph.facebook.com/{GraphApiVersion}/me/messages?access_token={Uri.EscapeDataString(credentials.PageAccessToken)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new SendMessageRequest(
                Recipient: new RecipientId(recipientPsid),
                Message:   new TextBody(body)))
        };

        using var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await SafeReadErrorAsync(response, ct);
            throw new MessengerApiException($"Messenger send failed ({(int)response.StatusCode}): {errorBody}");
        }
    }

    private static async Task<string> SafeReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadAsStringAsync(ct); }
        catch { return $"HTTP {(int)response.StatusCode}"; }
    }

    private record PageLookupResponse([property: JsonPropertyName("name")] string? Name);
    private record RecipientId([property: JsonPropertyName("id")] string Id);
    private record TextBody([property: JsonPropertyName("text")] string Text);
    private record SendMessageRequest(
        [property: JsonPropertyName("recipient")] RecipientId Recipient,
        [property: JsonPropertyName("message")]   TextBody Message);
}

public class MessengerApiException : Exception
{
    public MessengerApiException(string message) : base(message) { }
}
