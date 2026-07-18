using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Api.Infrastructure.Channels;

public record WhatsAppCredentials(string AccessToken, string PhoneNumberId);

/// <summary>
/// Thin wrapper over Meta's WhatsApp Cloud API (Graph API). Scope is deliberately
/// minimal for Sprint 2: verify a connection works, and send a plain text message
/// (used both for the onboarding "send yourself a test message" step and for the
/// webhook's echo-reply). Templates, media, interactive messages etc. are
/// out of scope until a later sprint actually needs them.
/// </summary>
public interface IWhatsAppClient
{
    /// <summary>Calls Graph API for the phone number itself - cheapest possible call that proves the token + phone number id actually work together.</summary>
    Task<WhatsAppVerifyResult> VerifyAsync(WhatsAppCredentials credentials, CancellationToken ct = default);

    Task SendTextMessageAsync(WhatsAppCredentials credentials, string toPhoneNumber, string body, CancellationToken ct = default);
}

public record WhatsAppVerifyResult(bool Success, string? DisplayPhoneNumber, string? ErrorMessage);

public class WhatsAppClient : IWhatsAppClient
{
    private const string GraphApiVersion = "v19.0";

    private readonly HttpClient _httpClient;

    public WhatsAppClient(HttpClient httpClient)
    {
        // Base address left unset deliberately - phone_number_id is part of the
        // path and varies per call, so every method builds its own absolute URL
        // against https://graph.facebook.com rather than relying on BaseAddress.
        _httpClient = httpClient;
    }

    public async Task<WhatsAppVerifyResult> VerifyAsync(WhatsAppCredentials credentials, CancellationToken ct = default)
    {
        var url = $"https://graph.facebook.com/{GraphApiVersion}/{credentials.PhoneNumberId}?fields=display_phone_number,verified_name";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credentials.AccessToken);

        using var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await SafeReadErrorAsync(response, ct);
            return new WhatsAppVerifyResult(false, null, errorBody);
        }

        var payload = await response.Content.ReadFromJsonAsync<PhoneNumberLookupResponse>(cancellationToken: ct);
        return new WhatsAppVerifyResult(true, payload?.DisplayPhoneNumber, null);
    }

    public async Task SendTextMessageAsync(WhatsAppCredentials credentials, string toPhoneNumber, string body, CancellationToken ct = default)
    {
        var url = $"https://graph.facebook.com/{GraphApiVersion}/{credentials.PhoneNumberId}/messages";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new SendTextMessageRequest(
                MessagingProduct: "whatsapp",
                To: toPhoneNumber,
                Type: "text",
                Text: new TextBody(body)))
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credentials.AccessToken);

        using var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await SafeReadErrorAsync(response, ct);
            throw new WhatsAppApiException($"WhatsApp send failed ({(int)response.StatusCode}): {errorBody}");
        }
    }

    private static async Task<string> SafeReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return $"HTTP {(int)response.StatusCode}";
        }
    }

    private record PhoneNumberLookupResponse(
        [property: JsonPropertyName("display_phone_number")] string? DisplayPhoneNumber,
        [property: JsonPropertyName("verified_name")] string? VerifiedName);

    private record TextBody([property: JsonPropertyName("body")] string Body);

    private record SendTextMessageRequest(
        [property: JsonPropertyName("messaging_product")] string MessagingProduct,
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] TextBody Text);
}

public class WhatsAppApiException : Exception
{
    public WhatsAppApiException(string message) : base(message) { }
}
