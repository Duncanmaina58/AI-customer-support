namespace Api.Contracts.Channels;

public record ChannelConnectionDto(
    Guid Id,
    string Channel,
    string Status,
    string? DisplayInfo,
    DateTime? LastVerifiedAt,
    DateTime CreatedAt,
    // Sprint 5 follow-up: only meaningful for Channel == "Email" — "Webhook" or
    // "Imap". Null for every other channel. Lets the frontend show the right
    // post-connect info (webhook URL vs "polling automatically") without
    // guessing from local component state.
    string? InboundMode = null);

public record ConnectWhatsAppRequest(string AccessToken, string PhoneNumberId);

public record ConnectWebChatResponse(ChannelConnectionDto Channel, string EmbedScriptTag);

public record SendWhatsAppTestMessageRequest(string ToPhoneNumber, string? Message);

// Sprint 5: Email channel — client picks the inbound mode, Webhook (Brevo,
// paid plan required) or Imap (MailKit, free, any regular mailbox).
public record ConnectEmailRequest(
    string  Mode,          // "Webhook" | "Imap"
    string? SenderEmail,
    string? SenderName,
    string? ImapHost = null,
    int?    ImapPort = null,
    string? SmtpHost = null,
    int?    SmtpPort = null,
    string? Username = null,
    string? Password = null);

// BrevoWebhookUrl is null for Imap-mode connections — there's no webhook to configure.
public record ConnectEmailResponse(ChannelConnectionDto Channel, string? BrevoWebhookUrl);

// Sprint 6: Messenger + Telegram
public record ConnectMessengerRequest(string PageAccessToken, string PageId);

public record ConnectTelegramRequest(string BotToken);
