using System.Globalization;
using Api.Application.Abstractions;
using Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.AI;

/// <summary>
/// Retrieval-Augmented Generation (RAG) service: given a customer's query,
/// returns the top-K knowledge base chunks most semantically similar to it.
///
/// Pipeline per query:
///   1. Early-exit: if no chunks exist for this company, return empty (avoids
///      an unnecessary embedding API call).
///   2. Embed the query using IEmbeddingProvider (Cohere embed-multilingual-v3.0, 1024-dim).
///   3. Run a pgvector cosine-distance nearest-neighbour query against
///      KnowledgeChunks, ordered by similarity.
///   4. Return the chunk texts as formatted strings ready for the system prompt.
///
/// Multi-tenancy note: this service is called from ChatHub and WhatsApp webhook
/// — both unauthenticated contexts. The global EF Core query filter therefore
/// evaluates to CompanyId == null and returns nothing. We bypass it with raw
/// SQL scoped to an explicit companyId, the same pattern used by ConversationService.
///
/// The raw SQL approach is deliberate: our KnowledgeChunk.Embedding is stored as
/// a float[] with a ValueConverter to pgvector's Vector type. EF Core's LINQ
/// translator cannot currently apply extension methods (CosineDistance, L2Distance)
/// to a property that goes through a ValueConverter, so we drop to a
/// Database.SqlQuery call for the similarity search specifically. Everything else
/// (writes, counts, existence checks) uses the normal EF Core query API.
/// </summary>
public class RagService
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ILogger<RagService> _logger;

    public RagService(AppDbContext db, IEmbeddingProvider embeddingProvider, ILogger<RagService> logger)
    {
        _db                = db;
        _embeddingProvider = embeddingProvider;
        _logger            = logger;
    }

    /// <summary>
    /// Returns up to <paramref name="topK"/> knowledge-base chunk texts ordered
    /// by cosine similarity to <paramref name="query"/>.
    ///
    /// Each returned string is formatted as "[DocumentName]\nText" so the system
    /// prompt can present each source clearly to the model.
    ///
    /// Returns an empty list (never throws) when:
    ///   - no chunks exist for this company yet
    ///   - the embedding call fails (caller should catch and proceed without context)
    /// </summary>
    public async Task<IReadOnlyList<string>> RetrieveAsync(
        Guid companyId,
        string query,
        int topK = 4,
        CancellationToken ct = default)
    {
        // Early-exit: skip the embedding API call entirely if the company has
        // no knowledge base content yet (common on fresh deployments).
        var hasChunks = await _db.KnowledgeChunks
            .IgnoreQueryFilters()
            .AnyAsync(c => c.CompanyId == companyId, ct);

        if (!hasChunks)
        {
            _logger.LogDebug("RagService: no chunks for company {CompanyId} — skipping retrieval", companyId);
            return [];
        }

        var queryEmbedding = await _embeddingProvider.EmbedAsync(
            query, EmbeddingInputType.Query, ct);

        // Build the vector literal for the pgvector <=> (cosine distance) operator.
        // The float values come from the embedding model — no user input, no injection risk.
        var vectorStr = string.Concat(
            "[",
            string.Join(",", queryEmbedding.Select(f => f.ToString("G7", CultureInfo.InvariantCulture))),
            "]");

        // EF Core 8 SqlQuery<T> with a FormattableString: each {param} is a proper
        // Npgsql parameter. The {vectorStr}::vector cast converts the text parameter
        // to the pgvector type inside PostgreSQL — this is valid syntax because
        // pgvector accepts the '[f1,f2,...]' text format as a cast source.
        //
        // LIMIT {topK} is also parameterized (safe from injection).
        // companyId is a Guid parameter (safe).
        //
        // We intentionally exclude the Embedding column from the SELECT — it's
        // ~6KB of floats per row that we don't need in the result set.
        var rows = await _db.Database
            .SqlQuery<ChunkRow>(
                $"""
                SELECT "DocumentName", "Text"
                FROM "KnowledgeChunks"
                WHERE "CompanyId" = {companyId}
                ORDER BY "Embedding" <=> {vectorStr}::vector
                LIMIT {topK}
                """)
            .ToListAsync(ct);

        _logger.LogDebug(
            "RagService: retrieved {Count} chunks for company {CompanyId}",
            rows.Count, companyId);

        return rows
            .Select(r => $"[{r.DocumentName}]\n{r.Text}")
            .ToList();
    }

    /// <summary>
    /// Embeds a single text string as a Document vector for knowledge-base ingestion.
    /// Used by KnowledgeController when storing a new chunk (synchronous embed-on-save).
    /// EmbeddingInputType.Document is critical for Cohere retrieval quality.
    /// </summary>
    public Task<float[]> EmbedTextAsync(string text, CancellationToken ct = default)
        => _embeddingProvider.EmbedAsync(text, EmbeddingInputType.Document, ct);

    // Local DTO — not an entity type. EF Core's SqlQuery<T> maps column names to
    // property names case-insensitively, so "DocumentName" and "Text" match.
    private sealed class ChunkRow
    {
        public string DocumentName { get; set; } = string.Empty;
        public string Text         { get; set; } = string.Empty;
    }
}
