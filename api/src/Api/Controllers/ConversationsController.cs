using Api.Application.Abstractions;
using Api.Contracts.Conversations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/conversations")]
public class ConversationsController : ControllerBase
{
    private readonly IAppDbContext _db;

    public ConversationsController(IAppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Lists conversations for the authenticated agent's company. Note there is no
    /// explicit `.Where(c => c.CompanyId == ...)` here — the global tenant query
    /// filter on AppDbContext applies automatically. Try as you might, this query
    /// cannot leak another company's conversations.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConversationDto>>> List(
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var query = _db.Conversations.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<Api.Domain.Enums.ConversationStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(c => c.Status == parsedStatus);
        }

        var conversations = await query
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new ConversationDto(
                c.Id,
                c.Channel.ToString(),
                c.CustomerId,
                c.CustomerDisplayName,
                c.Status.ToString(),
                c.CreatedAt,
                c.Messages.Count))
            .ToListAsync(ct);

        return Ok(conversations);
    }

    [HttpGet("{id:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<MessageDto>>> GetMessages(Guid id, CancellationToken ct)
    {
        var conversationExists = await _db.Conversations.AnyAsync(c => c.Id == id, ct);
        if (!conversationExists) return NotFound();

        var messages = await _db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == id)
            .OrderBy(m => m.SentAt)
            .Select(m => new MessageDto(m.Id, m.Role.ToString(), m.Content, m.SentAt))
            .ToListAsync(ct);

        return Ok(messages);
    }

    /// <summary>
    /// Marks a conversation as Resolved. Idempotent — resolving an already-resolved
    /// conversation is a no-op that still returns 204.
    /// </summary>
    [HttpPost("{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, CancellationToken ct)
    {
        var conversationExists = await _db.Conversations.AnyAsync(c => c.Id == id, ct);
        if (!conversationExists) return NotFound();

        await _db.Conversations
            .Where(c => c.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.Status, Api.Domain.Enums.ConversationStatus.Resolved),
                ct);

        return NoContent();
    }
}
