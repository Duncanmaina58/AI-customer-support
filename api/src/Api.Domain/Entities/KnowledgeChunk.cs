using Api.Domain.Common;
using Api.Domain.Enums;

namespace Api.Domain.Entities;

/// <summary>
/// A chunk of an uploaded knowledge-base document, plus its vector embedding.
/// The Embedding property is mapped to a pgvector column in Api.Infrastructure
/// (see KnowledgeChunkConfiguration) — kept as float[] here so Domain has zero
/// dependency on any specific database package.
///
/// Sprint 4 web crawling addition: chunks can also come from a crawled web page.
/// SourceType/SourceUrl/WebSourceId default to Document/null/null so existing
/// document/manual-entry chunks are completely unaffected — the RAG query
/// pipeline (RagService) needs ZERO changes, since web chunks live in this
/// exact same table and are retrieved identically regardless of SourceType.
/// </summary>
public class KnowledgeChunk : AuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public Guid DocumentId { get; set; }
    public string DocumentName { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }

    /// <summary>1024-dim embedding (Cohere embed-multilingual-v3.0). Dimension is enforced by the DB column type; changing the model requires a schema migration.</summary>
    public float[] Embedding { get; set; } = Array.Empty<float>();

    /// <summary>Document (manual entry / upload) or Web (crawled page). Defaults to Document.</summary>
    public KnowledgeSourceType SourceType { get; set; } = KnowledgeSourceType.Document;

    /// <summary>The page URL this chunk was extracted from. Null for Document-sourced chunks.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>FK -> WebSource. Null for Document-sourced chunks. Used by the surgical-update delete (WHERE CompanyId + SourceUrl) when a single page changes.</summary>
    public Guid? WebSourceId { get; set; }
    public WebSource? WebSource { get; set; }
}
