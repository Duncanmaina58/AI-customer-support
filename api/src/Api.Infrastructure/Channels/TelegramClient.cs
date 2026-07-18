using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Api.Infrastructure.Channels;

/// <summary>
/// SecretToken (Sprint 8 security hardening): a random value we generate and
/// register with Telegram's setWebhook secret_token param. Telegram echoes it
/// back on every webhook POST as X-Telegram-Bot-Api-Secret-Token, so we can
/// verify a request actually came from Telegram and not a forged POST to a
/// guessed/leaked webhook URL. Nullable/optional so existing connections made
/// before this hardening still decrypt fine — TelegramWebhookController treats
/// a missing SecretToken as "not yet re-connected under the new scheme" and
/// logs a warning rather than hard-failing.
/// </summary>
public record TelegramCredentials(string BotToken, string? SecretToken = null);

public record TelegramVerifyResult(bool Success, string? BotUsername, string? ErrorMessage);

/// <summary>
/// Thin wrapper over the Telegram Bot API — pure REST, no library needed (per
/// the Phase 1 doc). Setup is simpler than the other channels: the client
/// creates a bot via @BotFather (free, instant), pastes the token in, and this
/// platform auto-calls setWebhook to point Telegram at our endpoint — no
/// manual webhook configuration on the client's end at all, unlike WhatsApp/
/// Messenger/Email which all require pasting a URL into someone else's dashboard.
/// </summary>
public interface ITelegramClient
{
    /// <summary>Calls getMe — cheapest possible call that proves the token is real.</summary>
    Task<TelegramVerifyResult> VerifyAsync(TelegramCredentials credentials, CancellationToken ct = default);

    /// <summary>Registers our webhook URL (and secret_token, for signature verification) with Telegram so inbound updates start arriving.</summary>
    Task<bool> SetWebhookAsync(TelegramCredentials credentials, string webhookUrl, string secretToken, CancellationToken ct = default);

    Task SendTextMessageAsync(TelegramCredentials credentials, long chatId, string text, CancellationToken ct = default);
}

public class TelegramClient : ITelegramClient
{
    private readonly HttpClient _httpClient;

    public TelegramClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<TelegramVerifyResult> VerifyAsync(TelegramCredentials credentials, CancellationToken ct = default)
    {
        var url = $"https://api.telegram.org/bot{credentials.BotToken}/getMe";

        using var response = await _httpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            return new TelegramVerifyResult(false, null, await SafeReadErrorAsync(response, ct));
        }

        var payload = await response.Content.ReadFromJsonAsync<TelegramApiResponse<TelegramUser>>(cancellationToken: ct);
        if (payload is null || !payload.Ok || payload.Result is null)
        {
            return new TelegramVerifyResult(false, null, payload?.Description ?? "Telegram rejected this token.");
        }

        return new TelegramVerifyResult(true, payload.Result.Username, null);
    }

    public async Task<bool> SetWebhookAsync(TelegramCredentials credentials, string webhookUrl, string secretToken, CancellationToken ct = default)
    {
        var url = $"https://api.telegram.org/bot{credentials.BotToken}/setWebhook";

        using var response = await _httpClient.PostAsJsonAsync(url, new SetWebhookRequest(webhookUrl, secretToken), ct);
        if (!response.IsSuccessStatusCode) return false;

        var payload = await response.Content.ReadFromJsonAsync<TelegramApiResponse<bool>>(cancellationToken: ct);
        return payload?.Ok == true;
    }

    public async Task SendTextMessageAsync(TelegramCredentials credentials, long chatId, string text, CancellationToken ct = default)
    {
        var url = $"https://api.telegram.org/bot{credentials.BotToken}/sendMessage";

        using var response = await _httpClient.PostAsJsonAsync(url, new SendMessageRequest(chatId, text), ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await SafeReadErrorAsync(response, ct);
            throw new TelegramApiException($"Telegram send failed ({(int)response.StatusCode}): {errorBody}");
        }
    }

    private static async Task<string> SafeReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadAsStringAsync(ct); }
        catch { return $"HTTP {(int)response.StatusCode}"; }
    }

    private record TelegramApiResponse<T>(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("result")] T? Result,
        [property: JsonPropertyName("description")] string? Description);

    private record TelegramUser(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("username")] string? Username);

    private record SetWebhookRequest(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("secret_token")] string SecretToken);

    private record SendMessageRequest(
        [property: JsonPropertyName("chat_id")] long ChatId,
        [property: JsonPropertyName("text")] string Text);
}

public class TelegramApiException : Exception
{
    public TelegramApiException(string message) : base(message) { }
}
