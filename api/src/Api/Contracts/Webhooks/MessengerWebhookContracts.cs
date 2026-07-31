using System.Text.Json.Serialization;

namespace Api.Contracts.Webhooks;

// Minimal slice of Meta's Messenger webhook payload — same Meta Graph API
// family as WhatsApp, but a different object structure (per the Phase 1 doc).
// Only text messages are handled; attachments, quick replies, postbacks, read
// receipts etc. are ignored until a later sprint needs them.

public record MessengerWebhookPayload(
    [property: JsonPropertyName("entry")] List<MessengerWebhookEntry>? Entry);

public record MessengerWebhookEntry(
    [property: JsonPropertyName("messaging")] List<MessengerMessagingEvent>? Messaging);

public record MessengerMessagingEvent(
    [property: JsonPropertyName("sender")]  MessengerSender? Sender,
    [property: JsonPropertyName("message")] MessengerInboundMessage? Message);

public record MessengerSender(
    [property: JsonPropertyName("id")] string? Id); // PSID — Page-Scoped ID

public record MessengerInboundMessage(
    [property: JsonPropertyName("mid")]  string? Mid,
    [property: JsonPropertyName("text")] string? Text);
