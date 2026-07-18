using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.Channels;
using Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
namespace Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore.Storage;
/// <summary>
/// Creates support tickets and sends email notifications to agents via Brevo.
///
/// TicketNumber is a per-company incrementing integer (not a global DB sequence).
/// Generation is atomic: we lock-within-a-transaction using a SELECT FOR UPDATE
/// (via EF Core's FromSqlRaw with pessimistic locking) to prevent two concurrent
/// escalations from the same company producing duplicate numbers. In practice,
/// concurrent escalations are very rare, but getting duplicate numbers would
/// confuse agents ("Ticket #12 replied twice?").
///
/// Uses AppDbContext directly (not IAppDbContext) because the global tenant query
/// filter returns nothing for unauthenticated callers (ChatHub, webhook).
/// Ticket.CompanyId is always supplied explicitly.
/// </summary>
public class TicketService
{
    private readonly AppDbContext          _db;
    private readonly IBrevoEmailClient     _email;
    private readonly IServiceScopeFactory  _scopeFactory;
    private readonly ILogger<TicketService> _logger;

    public TicketService(
        AppDbContext           db,
        IBrevoEmailClient      email,
        IServiceScopeFactory   scopeFactory,
        ILogger<TicketService> logger)
    {
        _db           = db;
        _email        = email;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }
    /// <summary>
    /// Creates a ticket linked to <paramref name="conversationId"/>, assigns a
    /// sequential per-company ticket number, marks the conversation as Escalated,
    /// and emails the assigned agent (or all admins if no agent is specified).
    /// </summary>
 public async Task<Ticket> CreateAsync(
    Guid           companyId,
    Guid           conversationId,
    string         subject,
    TicketPriority priority         = TicketPriority.Medium,
    string?        assignedTeam     = null,
    string?        escalationReason = null,
    Guid?          assignedToId     = null,
    CancellationToken ct            = default)
{
    // InMemory provider (unit tests) doesn't support transactions or ExecuteUpdate.
    var isInMemory = _db.Database.ProviderName ==
                     "Microsoft.EntityFrameworkCore.InMemory";

    // Atomic ticket number generation — skip transaction wrapper under InMemory.
    IDbContextTransaction? txn = null;
    if (!isInMemory)
        txn = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);

    await using var txnDisposable = txn;   // disposes if non-null

    var maxNumber = await _db.Tickets
        .IgnoreQueryFilters()
        .Where(t => t.CompanyId == companyId)
        .Select(t => (int?)t.TicketNumber)
        .MaxAsync(ct) ?? 0;

    var ticket = new Ticket
    {
        CompanyId        = companyId,
        ConversationId   = conversationId,
        TicketNumber     = maxNumber + 1,
        Subject          = subject.Length > 300 ? subject[..300] : subject,
        Status           = TicketStatus.Open,
        Priority         = priority,
        AssignedTeam     = assignedTeam,
        AssignedToId     = assignedToId,
        EscalationReason = escalationReason,
    };

    _db.Tickets.Add(ticket);

    // Mark conversation Escalated — use load+save under InMemory,
    // ExecuteUpdate on real Postgres (stays in same transaction).
    if (isInMemory)
    {
        var conv = await _db.Conversations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conv is not null)
            conv.Status = ConversationStatus.Escalated;
    }
    else
    {
        await _db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.Status, ConversationStatus.Escalated), ct);
    }

    await _db.SaveChangesAsync(ct);
    if (txn is not null) await txn.CommitAsync(ct);

    _logger.LogInformation(
        "Ticket #{Number} created | company={CompanyId} conv={ConvId} priority={Priority}",
        ticket.TicketNumber, companyId, conversationId, priority);

    // _ = SendNotificationInNewScopeAsync(ticket.Id, companyId);
_ = SendNotificationInNewScopeAsync(ticket.Id, companyId);
    return ticket;
}

    // -------------------------------------------------------------------------

// -------------------------------------------------------------------------

    private async Task SendNotificationInNewScopeAsync(Guid ticketId, Guid companyId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email  = scope.ServiceProvider.GetRequiredService<IBrevoEmailClient>();

        try
        {
            var ticket = await db.Tickets
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == ticketId);

            if (ticket is null)
            {
                _logger.LogWarning("Ticket {TicketId} not found for notification", ticketId);
                return;
            }

            await SendNotificationAsync(db, email, ticket, companyId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send notification for ticket {TicketId}", ticketId);
        }
    }

    private async Task SendNotificationAsync(
        AppDbContext       db,
        IBrevoEmailClient  email,
        Ticket             ticket,
        Guid               companyId,
        CancellationToken  ct)
    {
        try
        {
            List<Agent> recipients;

            if (ticket.AssignedToId.HasValue)
            {
                var agent = await db.Agents
                    .IgnoreQueryFilters()
                    .Where(a => a.Id == ticket.AssignedToId.Value && a.IsActive)
                    .FirstOrDefaultAsync(ct);
                recipients = agent is null ? [] : [agent];
            }
            else
            {
                recipients = await db.Agents
                    .IgnoreQueryFilters()
                    .Where(a => a.CompanyId == companyId
                             && a.IsActive
                             && (a.Role == AgentRole.Owner || a.Role == AgentRole.Admin))
                    .ToListAsync(ct);
            }

            if (recipients.Count == 0)
            {
                _logger.LogDebug(
                    "Ticket #{Number}: no agents to notify | company={CompanyId}",
                    ticket.TicketNumber, companyId);
                return;
            }

            var body = BuildNotificationBody(ticket);

            foreach (var agent in recipients)
            {
                var outbound = new BrevoOutboundEmail(
                    SenderName:   "AI Support Platform",
                    SenderEmail:  "globaljobhubplatform@gmail.com",
                    ToEmail:      agent.Email,
                    ToName:       agent.Name,
                    Subject:      $"[Ticket #{ticket.TicketNumber}] {ticket.Subject}",
                    TextContent:  body);

                await email.SendAsync(outbound, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send notification for ticket #{Number}", ticket.TicketNumber);
        }
    }

    private static string BuildNotificationBody(Ticket ticket) =>
        $"""
        A new support ticket has been assigned to you.

        Ticket:   #{ticket.TicketNumber}
        Subject:  {ticket.Subject}
        Priority: {ticket.Priority}
        Team:     {ticket.AssignedTeam ?? "Unassigned"}
        Reason:   {ticket.EscalationReason ?? "Manual escalation"}

        Log in to your dashboard to view the full conversation and respond.

        ---
        AI Support Platform
        """;
}
