using Api.Application.Abstractions;
using Api.Contracts.Knowledge;
using Api.Domain.Entities;
using Api.Infrastructure.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

/// <summary>
/// Sprint 4: CRUD for the knowledge base. Authenticated agents upload text
/// entries; on create/update the entry is immediately embedded via OpenAI
/// Cohere embed-multilingual-v3.0 and stored as a pgvector column so it's instantly
/// searchable by RagService.
///
/// Sprint 6 enhancement: large-document chunking + Hangfire background jobs
/// for async re-embedding when the OpenAI API is slow or rate-limited.
///
/// All queries are auto-tenant-scoped by the global EF Core query filter —
/// agents can only see and modify their own company's chunks.
/// </summary>
[ApiController]
[Authorize]
[Route("api/knowledge")]
public class KnowledgeController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly RagService _rag;
    private readonly ICurrentTenantProvider _tenant;
    private readonly ILogger<KnowledgeController> _logger;

    public KnowledgeController(
        IAppDbContext db,
        RagService rag,
        ICurrentTenantProvider tenant,
        ILogger<KnowledgeController> logger)
    {
        _db     = db;
        _rag    = rag;
        _tenant = tenant;
        _logger = logger;
    }

    /// <summary>
    /// List all knowledge base entries for the current company, ordered newest first.
    /// The global query filter guarantees tenant isolation — no explicit CompanyId
    /// predicate needed here.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<KnowledgeChunkDto>>> List(CancellationToken ct)
    {
        // Sprint 4 web crawling addition: crawled web pages create KnowledgeChunk
        // rows in this exact same table (SourceType = Web) so RAG retrieval needs
        // no changes — but they're managed from the separate Web Sources tab, not
        // here, so this manual-entries list only ever shows SourceType = Document.
        var chunks = await _db.KnowledgeChunks
            .AsNoTracking()
            .Where(c => c.SourceType == Api.Domain.Enums.KnowledgeSourceType.Document)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new KnowledgeChunkDto(
                c.Id,
                c.DocumentId,
                c.DocumentName,
                c.Text.Length > 200 ? c.Text.Substring(0, 200) + "…" : c.Text,
                c.Text,
                c.ChunkIndex,
                c.CreatedAt))
            .ToListAsync(ct);

        return Ok(chunks);
    }

    /// <summary>
    /// Returns a single knowledge chunk by id (must belong to current company).
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<KnowledgeChunkDto>> GetById(Guid id, CancellationToken ct)
    {
        var chunk = await _db.KnowledgeChunks
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.SourceType == Api.Domain.Enums.KnowledgeSourceType.Document, ct);

        if (chunk is null) return NotFound();

        return Ok(new KnowledgeChunkDto(
            chunk.Id,
            chunk.DocumentId,
            chunk.DocumentName,
            chunk.Text.Length > 200 ? chunk.Text[..200] + "…" : chunk.Text,
            chunk.Text,
            chunk.ChunkIndex,
            chunk.CreatedAt));
    }

    /// <summary>
    /// Creates a new knowledge base entry.
    ///
    /// Embedding is performed synchronously: the text is sent to OpenAI's
    /// Cohere embed-multilingual-v3.0 endpoint, the resulting 1024-dimension float[]
    /// is stored in the pgvector column, and the chunk is immediately available
    /// for semantic search. Typical latency: 300–600ms.
    ///
    /// Each POST creates a new Document (new DocumentId). To add multiple chunks
    /// from the same logical document, either split client-side and POST each chunk
    /// individually, or wait for the Sprint 6 PDF-upload endpoint which does
    /// automatic semantic chunking via Hangfire.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Admin,Agent")]
    public async Task<ActionResult<KnowledgeChunkDto>> Create(
        [FromBody] UpsertKnowledgeRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentName))
            return BadRequest(new { message = "DocumentName is required." });

        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { message = "Text is required." });

        if (_tenant.CompanyId is not { } companyId)
            return Unauthorized(new { message = "No company context." });

        // Embed the text. This call goes to OpenAI — if the API key is missing or
        // the call fails, we return 502 rather than saving an empty embedding that
        // would silently produce garbage search results.
        float[] embedding;
        try
        {
            embedding = await _rag.EmbedTextAsync(request.Text, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Embedding failed for company {CompanyId} document '{Doc}'",
                companyId, request.DocumentName);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "Could not generate embedding. Check OpenAI:ApiKey and try again." });
        }

        var chunk = new KnowledgeChunk
        {
            CompanyId    = companyId,
            DocumentId   = Guid.NewGuid(),   // each POST = a standalone document in Sprint 4
            DocumentName = request.DocumentName.Trim(),
            Text         = request.Text.Trim(),
            ChunkIndex   = 0,
            Embedding    = embedding,
        };

        _db.KnowledgeChunks.Add(chunk);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Knowledge chunk created | company={CompanyId} doc='{Doc}' id={ChunkId}",
            companyId, chunk.DocumentName, chunk.Id);

        return CreatedAtAction(nameof(GetById), new { id = chunk.Id },
            new KnowledgeChunkDto(
                chunk.Id,
                chunk.DocumentId,
                chunk.DocumentName,
                chunk.Text.Length > 200 ? chunk.Text[..200] + "…" : chunk.Text,
                chunk.Text,
                chunk.ChunkIndex,
                chunk.CreatedAt));
    }

    /// <summary>
    /// Updates document name and/or text. Re-embeds if the text changed.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Owner,Admin,Agent")]
    public async Task<ActionResult<KnowledgeChunkDto>> Update(
        Guid id,
        [FromBody] UpsertKnowledgeRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentName))
            return BadRequest(new { message = "DocumentName is required." });

        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { message = "Text is required." });

        // Global filter scopes to current company automatically.
        var chunk = await _db.KnowledgeChunks.FirstOrDefaultAsync(c => c.Id == id && c.SourceType == Api.Domain.Enums.KnowledgeSourceType.Document, ct);
        if (chunk is null) return NotFound();

        var textChanged = !string.Equals(
            chunk.Text, request.Text.Trim(), StringComparison.Ordinal);

        chunk.DocumentName = request.DocumentName.Trim();
        chunk.Text         = request.Text.Trim();

        if (textChanged)
        {
            try
            {
                chunk.Embedding = await _rag.EmbedTextAsync(chunk.Text, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Re-embedding failed for chunk {ChunkId}", id);
                return StatusCode(StatusCodes.Status502BadGateway,
                    new { message = "Could not regenerate embedding. Check OpenAI:ApiKey and try again." });
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Knowledge chunk updated | id={ChunkId} re-embedded={Reembedded}", id, textChanged);

        return Ok(new KnowledgeChunkDto(
            chunk.Id,
            chunk.DocumentId,
            chunk.DocumentName,
            chunk.Text.Length > 200 ? chunk.Text[..200] + "…" : chunk.Text,
            chunk.Text,
            chunk.ChunkIndex,
            chunk.CreatedAt));
    }

    /// <summary>
    /// Hard-deletes a knowledge chunk (and its embedding) from the database.
    /// The vector index is updated by PostgreSQL automatically on delete.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var chunk = await _db.KnowledgeChunks.FirstOrDefaultAsync(c => c.Id == id && c.SourceType == Api.Domain.Enums.KnowledgeSourceType.Document, ct);
        if (chunk is null) return NotFound();

        _db.KnowledgeChunks.Remove(chunk);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Knowledge chunk deleted | id={ChunkId}", id);
        return NoContent();
    }
}
