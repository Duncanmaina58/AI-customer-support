namespace Api.Contracts.Tickets;

public record TicketListItemDto(
    Guid         Id,
    int          TicketNumber,
    string       Subject,
    string       Status,
    string       Priority,
    string?      AssignedTeam,
    string?      AssignedToName,
    string?      EscalationReason,
    string       ConversationChannel,
    string       CustomerIdentifier,
    DateTime     CreatedAt,
    DateTime?    ResolvedAt);

public record TicketDetailDto(
    Guid         Id,
    int          TicketNumber,
    string       Subject,
    string       Status,
    string       Priority,
    string?      AssignedTeam,
    Guid?        AssignedToId,
    string?      AssignedToName,
    string?      EscalationReason,
    Guid         ConversationId,
    string       ConversationChannel,
    string       CustomerIdentifier,
    string?      CustomerDisplayName,
    DateTime     CreatedAt,
    DateTime?    ResolvedAt,
    IReadOnlyList<TicketMessageDto> Messages);

public record TicketMessageDto(
    Guid     Id,
    string   Role,
    string   Content,
    DateTime SentAt);

public record UpdateTicketStatusRequest(string Status);

public record AssignTicketRequest(Guid? AgentId, string? Team);

public record AgentReplyRequest(string Message);
