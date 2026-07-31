using Api.Application.Abstractions;
using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.AI;

/// <summary>
/// The single ingestion pipeline shared by document/manual knowledge-base
/// entries AND crawled web pages (checklist: "Web crawling is added to
/// Sprint 4 alongside document ingestion. Both share the chunking, embedding,
/// and vector storage code."). Both callers end up creating the exact same
/// KnowledgeChunk rows that RagService.RetrieveAsync searches over — RagService
/// itself needs zero changes to support web-sourced chunks.
/// </summary>
public class KnowledgeIngestionService
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ILogger<KnowledgeIngestionService> _logger;

    public KnowledgeIngestionService(
        AppDbContext db,
        IEmbeddingProvider embeddingProvider,
        ILogger<KnowledgeIngestionService> logger)
    {
        _db                = db;
        _embeddingProvider = embeddingProvider;
        _logger            = logger;
    }

    /// <summary>
    /// Chunks, embeds, and stores <paramref name="text"/> as a crawled web page's
    /// knowledge chunks. Implements the "surgical update" from the spec: any
    /// existing chunks for this exact (CompanyId, SourceUrl) are deleted first via
    /// a single indexed SQL DELETE (no C# loading of the old rows), then the fresh
    /// chunks are inserted — so re-crawling one changed page never touches any
    /// other page's chunks.
    /// </summary>
    public async Task<int> IngestWebPageAsync(
        Guid companyId,
        Guid webSourceId,
        string pageUrl,
        string pageTitle,
        string text,
        CancellationToken ct = default)
    {
        await DeleteChunksForUrlAsync(companyId, pageUrl, ct);

        var chunks = TextChunker.Chunk(text);
        if (chunks.Count == 0)
        {
            _logger.LogInformation(
                "No text extracted for {Url} (company={CompanyId}) — zero chunks created",
                pageUrl, companyId);
            return 0;
        }

        var documentId = Guid.NewGuid();
        var displayName = string.IsNullOrWhiteSpace(pageTitle) ? pageUrl : pageTitle;

        var created = 0;
        for (var i = 0; i < chunks.Count; i++)
        {
            float[] embedding;
            try
            {
                embedding = await _embeddingProvider.EmbedAsync(chunks[i], EmbeddingInputType.Document, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Embedding failed for web chunk {Index} of {Url} (company={CompanyId}) — skipping this chunk",
                    i, pageUrl, companyId);
                continue;
            }

            _db.KnowledgeChunks.Add(new KnowledgeChunk
            {
                CompanyId    = companyId,
                DocumentId   = documentId,
                DocumentName = displayName,
                Text         = chunks[i],
                ChunkIndex   = i,
                Embedding    = embedding,
                SourceType   = KnowledgeSourceType.Web,
                SourceUrl    = pageUrl,
                WebSourceId  = webSourceId,
            });
            created++;
        }

        await _db.SaveChangesAsync(ct);
        return created;
    }

    /// <summary>
    /// Chunks, embeds, and stores <paramref name="text"/> as a document's knowledge
    /// chunks. Available for future multi-chunk document/PDF ingestion; the current
    /// KnowledgeController manual-entry flow intentionally still creates a single
    /// chunk per entry (unchanged, to avoid touching working Sprint 4 behaviour).
    /// </summary>
    public async Task<int> IngestDocumentAsync(
        Guid companyId,
        Guid documentId,
        string documentName,
        string text,
        CancellationToken ct = default)
    {
        var chunks = TextChunker.Chunk(text);
        if (chunks.Count == 0) return 0;

        var created = 0;
        for (var i = 0; i < chunks.Count; i++)
        {
            float[] embedding;
            try
            {
                embedding = await _embeddingProvider.EmbedAsync(chunks[i], EmbeddingInputType.Document, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Embedding failed for document chunk {Index} of '{Doc}' (company={CompanyId}) — skipping this chunk",
                    i, documentName, companyId);
                continue;
            }

            _db.KnowledgeChunks.Add(new KnowledgeChunk
            {
                CompanyId    = companyId,
                DocumentId   = documentId,
                DocumentName = documentName,
                Text         = chunks[i],
                ChunkIndex   = i,
                Embedding    = embedding,
                SourceType   = KnowledgeSourceType.Document,
            });
            created++;
        }

        await _db.SaveChangesAsync(ct);
        return created;
    }

    /// <summary>
    /// Deletes every KnowledgeChunk for this (CompanyId, SourceUrl) via a single
    /// indexed SQL DELETE — no rows loaded into memory. Used both for surgical
    /// re-indexing on content change and for full source deletion (called once
    /// per removed page from WebSourcesController.Delete).
    /// </summary>
    public Task<int> DeleteChunksForUrlAsync(Guid companyId, string pageUrl, CancellationToken ct = default)
        => _db.KnowledgeChunks
            .IgnoreQueryFilters()
            .Where(c => c.CompanyId == companyId && c.SourceUrl == pageUrl)
            .ExecuteDeleteAsync(ct);

    /// <summary>Deletes every KnowledgeChunk belonging to an entire WebSource — used when a source is deleted.</summary>
    public Task<int> DeleteChunksForSourceAsync(Guid companyId, Guid webSourceId, CancellationToken ct = default)
        => _db.KnowledgeChunks
            .IgnoreQueryFilters()
            .Where(c => c.CompanyId == companyId && c.WebSourceId == webSourceId)
            .ExecuteDeleteAsync(ct);
}
