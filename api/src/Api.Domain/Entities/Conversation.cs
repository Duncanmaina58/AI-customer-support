using Api.Domain.Common;
using Api.Domain.Enums;

namespace Api.Domain.Entities;

public class Conversation : AuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public ChannelType Channel { get; set; }

    /// <summary>External identifier for the end customer (phone number, email, widget session id, etc.).</summary>
    public string CustomerId { get; set; } = string.Empty;
    public string? CustomerDisplayName { get; set; }

    public ConversationStatus Status { get; set; } = ConversationStatus.Open;

    /// <summary>Set when an agent takes over from the AI.</summary>
    public Guid? AssignedAgentId { get; set; }
    public Agent? AssignedAgent { get; set; }

    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
}
