using System.Text.Json.Serialization;

namespace Api.Contracts.Webhooks;

// Minimal slice of Telegram's Update object — just enough to pull out inbound
// text messages. Telegram's real payload has many more update types (edited
// messages, callback queries, inline queries, etc.) ignored until needed.

public record TelegramUpdate(
    [property: JsonPropertyName("update_id")] long UpdateId,
    [property: JsonPropertyName("message")]   TelegramMessage? Message);

public record TelegramMessage(
    [property: JsonPropertyName("chat")] TelegramChat? Chat,
    [property: JsonPropertyName("text")] string? Text);

public record TelegramChat(
    [property: JsonPropertyName("id")] long Id);
