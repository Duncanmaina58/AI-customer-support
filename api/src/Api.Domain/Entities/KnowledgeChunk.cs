using Api.Domain.Common;

namespace Api.Domain.Entities;

/// <summary>
/// A chunk of an uploaded knowledge-base document, plus its vector embedding.
/// The Embedding property is mapped to a pgvector column in Api.Infrastructure
/// (see KnowledgeChunkConfiguration) — kept as float[] here so Domain has zero
/// dependency on any specific database package.
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
}
