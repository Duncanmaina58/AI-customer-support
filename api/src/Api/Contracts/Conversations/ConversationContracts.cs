namespace Api.Contracts.Conversations;

public record ConversationDto(
    Guid Id,
    string Channel,
    string CustomerId,
    string? CustomerDisplayName,
    string Status,
    DateTime CreatedAt,
    int MessageCount);

public record MessageDto(Guid Id, string Role, string Content, DateTime SentAt);
