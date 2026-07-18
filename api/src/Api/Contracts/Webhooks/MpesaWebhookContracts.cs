using System.Text.Json.Serialization;

namespace Api.Contracts.Webhooks;

// Daraja's actual callback shape wraps everything in Body.stkCallback.
// CallbackMetadata.Item is a flat list of {Name, Value} pairs rather than a
// proper object (a well-known Daraja API quirk) — only present when
// ResultCode is 0 (success); absent entirely on failure/cancellation.

public record MpesaCallbackPayload(
    [property: JsonPropertyName("Body")] MpesaCallbackBody? Body);

public record MpesaCallbackBody(
    [property: JsonPropertyName("stkCallback")] MpesaStkCallback? StkCallback);

public record MpesaStkCallback(
    [property: JsonPropertyName("MerchantRequestID")] string? MerchantRequestId,
    [property: JsonPropertyName("CheckoutRequestID")] string? CheckoutRequestId,
    [property: JsonPropertyName("ResultCode")]        int     ResultCode,
    [property: JsonPropertyName("ResultDesc")]         string? ResultDesc,
    [property: JsonPropertyName("CallbackMetadata")]   MpesaCallbackMetadata? CallbackMetadata)
{
    /// <summary>Daraja's ResultCode is an int; "0" means success, everything else is a specific failure.</summary>
    public bool IsSuccess => ResultCode == 0;

    public string? GetMetadataValue(string name) =>
        CallbackMetadata?.Item?.FirstOrDefault(i => i.Name == name)?.Value?.ToString();
}

public record MpesaCallbackMetadata(
    [property: JsonPropertyName("Item")] List<MpesaCallbackItem>? Item);

public record MpesaCallbackItem(
    [property: JsonPropertyName("Name")]  string? Name,
    [property: JsonPropertyName("Value")] object? Value);
