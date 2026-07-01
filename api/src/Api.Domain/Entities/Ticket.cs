using Api.Domain.Common;
using Api.Domain.Enums;

namespace Api.Domain.Entities;

public class Ticket : AuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public Guid ConversationId { get; set; }
    public Conversation? Conversation { get; set; }

    public string Subject { get; set; } = string.Empty;
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;

    public Guid? AssignedToId { get; set; }
    public Agent? AssignedTo { get; set; }

    public DateTime? ResolvedAt { get; set; }
}
