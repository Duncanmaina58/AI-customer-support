namespace Api.Contracts.Knowledge;

/// <summary>
/// Create or update a knowledge base entry. A single request is one "document"
/// which becomes a single KnowledgeChunk in Sprint 4 (multi-chunk splitting for
/// large documents is a Sprint 6 enhancement via Hangfire background jobs).
/// </summary>
public record UpsertKnowledgeRequest(
    string DocumentName,
    string Text);

/// <summary>
/// What the API returns after creating, updating, or listing knowledge chunks.
/// The Embedding field is intentionally excluded — it's ~6KB of floats that the
/// UI has no use for.
/// </summary>
public record KnowledgeChunkDto(
    Guid   Id,
    Guid   DocumentId,
    string DocumentName,
    string TextPreview,   // first 200 characters — keeps list responses small
    string FullText,      // included on single-item responses (GET /{id})
    int    ChunkIndex,
    DateTime CreatedAt);
