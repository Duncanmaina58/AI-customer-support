namespace Api.Domain.Enums;

public enum BrandVoice
{
    Formal,
    Friendly,
    Neutral
}

public enum CompanyPlan
{
    Starter,
    Growth,
    Enterprise
}

public enum ChannelType
{
    WebChat,
    WhatsApp,
    Email,
    Messenger,
    Telegram,
    Instagram,
    MobileSdk
}

public enum ConversationStatus
{
    Open,
    Pending,
    Resolved,
    Escalated
}

public enum MessageRole
{
    User,
    Ai,
    Agent,
    System
}

public enum TicketStatus
{
    Open,
    InProgress,
    Resolved,
    Closed
}

public enum TicketPriority
{
    Low,
    Medium,
    High,
    Urgent
}

public enum ChannelConnectionStatus
{
    Active,
    Paused,
    Error
}

public enum AgentRole
{
    Owner,
    Admin,
    Agent
}
