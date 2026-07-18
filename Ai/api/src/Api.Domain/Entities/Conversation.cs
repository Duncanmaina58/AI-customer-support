using Api.Domain.Common;
using Api.Domain.Enums;

namespace Api.Domain.Entities;

public class Conversation : AuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public ChannelType Channel { get; set; }

    /// <summary>External identifier for the end customer (phone number, email address, widget session id, etc.).</summary>
    public string CustomerId { get; set; } = string.Empty;
    public string? CustomerDisplayName { get; set; }

    public ConversationStatus Status { get; set; } = ConversationStatus.Open;

    /// <summary>
    /// Sprint 6: true if this conversation was started through the company's
    /// sandbox test link/dashboard test chat rather than a real customer
    /// channel. Set once at creation (see ConversationService.GetOrCreateAsync)
    /// and never changes afterward. Sandbox conversations are excluded from
    /// token-budget accounting and never create real tickets — see ChatHub.
    /// </summary>
    public bool IsSandbox { get; set; }

    /// <summary>Set when an agent takes over from the AI.</summary>
    public Guid? AssignedAgentId { get; set; }
    public Agent? AssignedAgent { get; set; }

    // ---- Email threading (Sprint 5) -----------------------------------------

    /// <summary>
    /// For email conversations: the Message-ID header of the most recent inbound
    /// email in this thread. Stored so we can set In-Reply-To on our next outbound
    /// reply and keep the email thread intact in the customer's mail client.
    ///
    /// Format: the full RFC 5322 message-id value including angle brackets,
    ///   e.g. "&lt;CAB_123@mail.gmail.com&gt;". Null for non-email channels.
    /// </summary>
    public string? EmailMessageId { get; set; }

    /// <summary>
    /// For email conversations: the original email subject, preserved so outbound
    /// replies are sent as "Re: {subject}" and the thread stays coherent.
    /// Null for non-email channels.
    /// </summary>
    public string? EmailSubject { get; set; }

    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();

    // ---- CSAT (Sprint 7) ----------------------------------------------------

    /// <summary>
    /// 1-5 star rating the customer submitted for this conversation, if any.
    /// One rating per conversation — first submission wins (see
    /// ConversationService.SubmitCsatRatingAsync); resubmitting doesn't
    /// overwrite it, matching how most chat products treat a rating as a
    /// one-time close-out action rather than an editable field.
    /// </summary>
    public int? CsatScore { get; set; }
    public DateTime? CsatSubmittedAt { get; set; }
}
