namespace Api.Contracts.Channels;

public record ChannelConnectionDto(
    Guid Id,
    string Channel,
    string Status,
    string? DisplayInfo,
    DateTime? LastVerifiedAt,
    DateTime CreatedAt);

public record ConnectWhatsAppRequest(string AccessToken, string PhoneNumberId);

public record ConnectWebChatResponse(ChannelConnectionDto Channel, string EmbedScriptTag);

public record SendWhatsAppTestMessageRequest(string ToPhoneNumber, string? Message);
