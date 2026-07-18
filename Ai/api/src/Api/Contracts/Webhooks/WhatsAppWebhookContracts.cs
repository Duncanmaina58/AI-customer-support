using System.Text.Json.Serialization;

namespace Api.Contracts.Webhooks;

// Minimal slice of Meta's WhatsApp webhook payload - just enough to pull out
// inbound text messages. Meta's actual payload has many more fields (statuses,
// media, reactions, etc.) that are simply ignored here until a later sprint
// needs them; System.Text.Json silently skips unknown properties by default.

public record WhatsAppWebhookPayload(
    [property: JsonPropertyName("entry")] List<WhatsAppWebhookEntry>? Entry);

public record WhatsAppWebhookEntry(
    [property: JsonPropertyName("changes")] List<WhatsAppWebhookChange>? Changes);

public record WhatsAppWebhookChange(
    [property: JsonPropertyName("value")] WhatsAppWebhookValue? Value);

public record WhatsAppWebhookValue(
    [property: JsonPropertyName("messages")] List<WhatsAppInboundMessage>? Messages);

public record WhatsAppInboundMessage(
    [property: JsonPropertyName("from")] string? From,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("text")] WhatsAppInboundText? Text);

public record WhatsAppInboundText(
    [property: JsonPropertyName("body")] string? Body);
