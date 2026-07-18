using Api.Application.Abstractions;
using Api.Contracts.Tickets;
using Api.Domain.Enums;
using Api.Hubs;
using Api.Infrastructure.Channels;
using Api.Infrastructure.Security;
using Api.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

/// <summary>
/// Sprint 5: CRUD for support tickets.
///
/// All queries use the global tenant query filter (authenticated agents always
/// have a JWT → CompanyId is non-null) so no explicit CompanyId predicate needed.
///
/// Endpoints:
///   GET  /api/tickets              — list (filterable by status/priority)
///   GET  /api/tickets/{id}         — detail with full conversation transcript
///   PATCH /api/tickets/{id}/status — open → in_progress → resolved → closed
///   POST /api/tickets/{id}/assign  — assign to agent and/or team
///   POST /api/tickets/{id}/reply   — agent sends reply via the original channel
/// </summary>
[ApiController]
[Authorize]
[Route("api/tickets")]
public class TicketsController : ControllerBase
{
    private readonly IAppDbContext            _db;
    private readonly ICurrentTenantProvider   _tenant;
    private readonly ConversationService      _conversations;
    private readonly IBrevoEmailClient        _brevo;
    private readonly IImapSmtpEmailClient     _imapSmtp;
    private readonly IWhatsAppClient          _whatsApp;
    private readonly IChannelCredentialProtector _protector;
    private readonly IHubContext<ChatHub>     _chatHub;
    private readonly ILogger<TicketsController> _logger;

    public TicketsController(
        IAppDbContext             db,
        ICurrentTenantProvider    tenant,
        ConversationService       conversations,
        IBrevoEmailClient         brevo,
        IImapSmtpEmailClient      imapSmtp,
        IWhatsAppClient           whatsApp,
        IChannelCredentialProtector protector,
        IHubContext<ChatHub>      chatHub,
        ILogger<TicketsController> logger)
    {
        _db            = db;
        _tenant        = tenant;
        _conversations = conversations;
        _brevo         = brevo;
        _imapSmtp      = imapSmtp;
        _whatsApp      = whatsApp;
        _protector     = protector;
        _chatHub       = chatHub;
        _logger        = logger;
    }

    /// <summary>List tickets for the current company. Optionally filter by status and/or priority.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TicketListItemDto>>> List(
        [FromQuery] string? status,
        [FromQuery] string? priority,
        CancellationToken ct)
    {
        var query = _db.Tickets
            .AsNoTracking()
            .Include(t => t.Conversation)
            .Include(t => t.AssignedTo)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<TicketStatus>(status, true, out var s))
            query = query.Where(t => t.Status == s);

        if (!string.IsNullOrWhiteSpace(priority) &&
            Enum.TryParse<TicketPriority>(priority, true, out var p))
            query = query.Where(t => t.Priority == p);

        var tickets = await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TicketListItemDto(
                t.Id,
                t.TicketNumber,
                t.Subject,
                t.Status.ToString(),
                t.Priority.ToString(),
                t.AssignedTeam,
                t.AssignedTo != null ? t.AssignedTo.Name : null,
                t.EscalationReason,
                t.Conversation!.Channel.ToString(),
                t.Conversation.CustomerId,
                t.CreatedAt,
                t.ResolvedAt))
            .ToListAsync(ct);

        return Ok(tickets);
    }

    /// <summary>Get a single ticket with the full linked conversation transcript.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TicketDetailDto>> GetById(Guid id, CancellationToken ct)
    {
        var ticket = await _db.Tickets
            .AsNoTracking()
            .Include(t => t.Conversation)
            .Include(t => t.AssignedTo)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (ticket is null) return NotFound();

        // Tenant filter already applied on Tickets above; for Messages we query
        // by ConversationId directly (Message has its own tenant filter too).
        var messages = await _db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == ticket.ConversationId)
            .OrderBy(m => m.SentAt)
            .Select(m => new TicketMessageDto(m.Id, m.Role.ToString(), m.Content, m.SentAt))
            .ToListAsync(ct);

        return Ok(new TicketDetailDto(
            ticket.Id,
            ticket.TicketNumber,
            ticket.Subject,
            ticket.Status.ToString(),
            ticket.Priority.ToString(),
            ticket.AssignedTeam,
            ticket.AssignedToId,
            ticket.AssignedTo?.Name,
            ticket.EscalationReason,
            ticket.ConversationId,
            ticket.Conversation!.Channel.ToString(),
            ticket.Conversation.CustomerId,
            ticket.Conversation.CustomerDisplayName,
            ticket.CreatedAt,
            ticket.ResolvedAt,
            messages));
    }

    /// <summary>
    /// Update ticket status. Valid transitions:
    ///   Open → InProgress → Resolved → Closed
    /// Also supports re-opening: Resolved / Closed → Open.
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id, [FromBody] UpdateTicketStatusRequest body, CancellationToken ct)
    {
        if (!Enum.TryParse<TicketStatus>(body.Status, true, out var newStatus))
            return BadRequest(new { message = $"Invalid status '{body.Status}'." });

        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return NotFound();

        ticket.Status     = newStatus;
        ticket.ResolvedAt = newStatus == TicketStatus.Resolved ? DateTime.UtcNow : ticket.ResolvedAt;

        await _db.SaveChangesAsync(ct);

        // Sprint 5 fix: once a ticket is Resolved or Closed, release its
        // conversation from the "Escalated" state. Otherwise ConversationService.
        // GetOrCreateAsync (which reuses Open OR Escalated conversations) would
        // keep reusing this one forever, permanently routing the customer's next
        // messages into ChatHub's "add to your open ticket" short-circuit even
        // though the ticket is done. Marking it Resolved means their next message
        // starts a clean new conversation the AI can help with again.
        if (newStatus is TicketStatus.Resolved or TicketStatus.Closed)
        {
            await _conversations.ResolveAsync(ticket.ConversationId, ct);
        }

        return NoContent();
    }

    /// <summary>
    /// Assign a ticket to an agent and/or team. Either field may be null
    /// (to clear agent assignment while keeping team, for example).
    /// </summary>
    [HttpPost("{id:guid}/assign")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<IActionResult> Assign(
        Guid id, [FromBody] AssignTicketRequest body, CancellationToken ct)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return NotFound();

        if (body.AgentId.HasValue)
        {
            // Confirm the agent belongs to this company (tenant filter applied).
            var agentExists = await _db.Agents
                .AnyAsync(a => a.Id == body.AgentId.Value && a.IsActive, ct);

            if (!agentExists)
                return BadRequest(new { message = "Agent not found in this company." });
        }

        ticket.AssignedToId = body.AgentId;
        ticket.AssignedTeam = body.Team ?? ticket.AssignedTeam;
        ticket.Status       = ticket.Status == TicketStatus.Open
                              ? TicketStatus.InProgress
                              : ticket.Status;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Agent sends a reply via the ticket's originating channel.
    /// The message is stored as Role=Agent and delivered:
    ///   Email     → Brevo transactional API with In-Reply-To for threading
    ///   WhatsApp  → Meta Graph API
    ///   WebChat   → pushed live over SignalR to ChatHub.ConversationGroupName(conversationId);
    ///               also visible via ChatHub.GetHistory next time the widget connects
    /// </summary>
    [HttpPost("{id:guid}/reply")]
    public async Task<IActionResult> Reply(
        Guid id, [FromBody] AgentReplyRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Message))
            return BadRequest(new { message = "Reply message is required." });

        var ticket = await _db.Tickets
            .Include(t => t.Conversation)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (ticket is null) return NotFound();

        var conversation = ticket.Conversation!;

        // Save agent message (shared across all channels)
        await _conversations.AppendMessageAsync(
            conversation.Id, conversation.CompanyId, MessageRole.Agent, body.Message, ct: ct);

        // Advance ticket status to InProgress if still Open
        if (ticket.Status == TicketStatus.Open)
        {
            ticket.Status = TicketStatus.InProgress;
            await _db.SaveChangesAsync(ct);
        }

        // Channel-specific delivery
        switch (conversation.Channel)
        {
            case ChannelType.Email:
                await SendEmailReplyAsync(conversation, body.Message, ct);
                break;

            case ChannelType.WhatsApp:
                await SendWhatsAppReplyAsync(conversation, body.Message, ct);
                break;

            default:
                // WebChat, MobileSdk: no external API to call — push straight to
                // the customer's live connection (if any) over the same SignalR
                // group ChatHub subscribes it to. If they're not currently
                // connected, the message is still saved (above) and will show up
                // via ChatHub.GetHistory next time they open the widget.
                await _chatHub.Clients
                    .Group(ChatHub.ConversationGroupName(conversation.Id))
                    .SendAsync("AgentReply", new { text = body.Message }, ct);
                break;
        }

        _logger.LogInformation(
            "Agent reply sent | ticket={TicketId} channel={Channel}",
            id, conversation.Channel);

        return NoContent();
    }

    // -------------------------------------------------------------------------
    // Private channel-delivery helpers
    // -------------------------------------------------------------------------

    private async Task SendEmailReplyAsync(
        Api.Domain.Entities.Conversation conversation,
        string message,
        CancellationToken ct)
    {
        var connection = await _db.ChannelConnections
            .FirstOrDefaultAsync(c =>
                c.Channel == ChannelType.Email
                && c.Status == ChannelConnectionStatus.Active, ct);

        if (connection is null) return;

        // Sender name/email live in plaintext MetadataJson for both inbound
        // modes (see EmailChannelMetadata) — only the *transport* credentials
        // (Brevo has none beyond the API key; Imap mode has host/port/user/pass)
        // differ and live in the encrypted blob, decrypted below per mode.
        var senderName  = EmailChannelMetadata.ReadSenderName(connection.MetadataJson);
        var senderEmail = EmailChannelMetadata.ReadSenderEmail(connection.MetadataJson);
        if (senderName is null || senderEmail is null) return;

        var subject = string.IsNullOrEmpty(conversation.EmailSubject)
            ? "Your support request"
            : conversation.EmailSubject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
                ? conversation.EmailSubject
                : $"Re: {conversation.EmailSubject}";

        var outbound = new BrevoOutboundEmail(
            SenderName:   senderName,
            SenderEmail:  senderEmail,
            ToEmail:      conversation.CustomerId,   // CustomerId = customer email address
            ToName:       conversation.CustomerDisplayName,
            Subject:      subject,
            TextContent:  message,
            InReplyTo:    conversation.EmailMessageId,
            References:   conversation.EmailMessageId,
            ReplyToEmail: senderEmail);

        if (EmailChannelMetadata.ReadMode(connection.MetadataJson) == EmailChannelMetadata.ModeImap)
        {
            try
            {
                var imapCreds = EmailChannelMetadata.DecryptImapCredentials(connection, _protector);
                await _imapSmtp.SendAsync(outbound, imapCreds, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send agent email reply via SMTP | conv={ConvId}", conversation.Id);
            }
        }
        else
        {
            await _brevo.SendAsync(outbound, ct);
        }
    }

    private async Task SendWhatsAppReplyAsync(
        Api.Domain.Entities.Conversation conversation,
        string message,
        CancellationToken ct)
    {
        var connection = await _db.ChannelConnections
            .FirstOrDefaultAsync(c =>
                c.Channel == ChannelType.WhatsApp
                && c.Status == ChannelConnectionStatus.Active, ct);

        if (connection is null) return;

        WhatsAppCredentials? creds;
        try
        {
            creds = System.Text.Json.JsonSerializer.Deserialize<WhatsAppCredentials>(
                _protector.Decrypt(connection.CredentialsEncrypted));
        }
        catch { return; }

        if (creds is null) return;

        await _whatsApp.SendTextMessageAsync(creds, conversation.CustomerId, message, ct);
    }
}
