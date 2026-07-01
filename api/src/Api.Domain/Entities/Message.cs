using Api.Domain.Common;
using Api.Domain.Enums;

namespace Api.Domain.Entities;

public class Message : AuditableEntity, ITenantScoped
{
    // Denormalized onto Message (in addition to via Conversation) so the global
    // tenant query filter can apply directly without requiring a join — this is
    // the standard defense-in-depth pattern for multi-tenant EF Core models.
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public Guid ConversationId { get; set; }
    public Conversation? Conversation { get; set; }

    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;

    /// <summary>Populated when Role == Ai, useful for cost tracking / quality scoring.</summary>
    public string? ModelUsed { get; set; }
    public double? ConfidenceScore { get; set; }

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
