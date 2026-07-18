using Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;

namespace Api.Infrastructure.Persistence.Configurations;

public class KnowledgeChunkConfiguration : IEntityTypeConfiguration<KnowledgeChunk>
{
    // Cohere embed-multilingual-v3.0 produces 1024-dim vectors.
    // (OpenAI text-embedding-3-small was 1536-dim — updated when switching to Cohere.)
    // If you change embedding model again, update this constant AND run the SQL
    // migration script in docs/migrations/cohere-1024-dim.sql to drop existing
    // vectors and re-embed them. Existing rows with a different dimension are
    // incompatible and will silently produce incorrect similarity results.
    public const int EmbeddingDimensions = 1024;

    public void Configure(EntityTypeBuilder<KnowledgeChunk> builder)
    {
        builder.ToTable("KnowledgeChunks");
        builder.HasKey(k => k.Id);

        builder.Property(k => k.DocumentName).IsRequired().HasMaxLength(300);
        builder.Property(k => k.Text).IsRequired();

        // Domain.KnowledgeChunk.Embedding is a plain float[] (Domain has zero
        // dependency on the pgvector package). Convert to/from Pgvector.Vector
        // here so it maps onto a real `vector(1024)` column, enabling fast
        // approximate-nearest-neighbour search (HNSW/IVFFlat) at the DB layer.
        //
        // The explicit ValueComparer below tells EF Core's change tracker to
        // compare array CONTENTS (SequenceEqual) instead of array reference
        // identity. Without it, EF logs a warning at startup and - more
        // importantly - could silently miss an UPDATE if an array's contents
        // were mutated in place rather than reassigned.
        var embeddingComparer = new ValueComparer<float[]>(
            (a, b) => (a ?? Array.Empty<float>()).SequenceEqual(b ?? Array.Empty<float>()),
            v => v.Aggregate(0, (hash, value) => HashCode.Combine(hash, value)),
            v => v.ToArray());

        builder.Property(k => k.Embedding)
            .HasConversion(
                v => new Vector(v),
                v => v.ToArray())
            .Metadata.SetValueComparer(embeddingComparer);

        builder.Property(k => k.Embedding)
            .HasColumnType($"vector({EmbeddingDimensions})");

        builder.HasIndex(k => new { k.CompanyId, k.DocumentId });

        builder.HasOne(k => k.Company)
            .WithMany()
            .HasForeignKey(k => k.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        // ---- Sprint 4 web crawling addition ----
        // SourceType/SourceUrl/WebSourceId are nullable-by-default (SourceType
        // defaults to Document at the DB level) so existing document/manual-entry
        // rows are completely unaffected by this migration.
        builder.Property(k => k.SourceType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(Api.Domain.Enums.KnowledgeSourceType.Document);

        builder.Property(k => k.SourceUrl).HasMaxLength(2000);

        // Surgical update index: "delete only this page's chunks" (see
        // WebCrawlerService.UpdateChangedPageAsync) is a single indexed
        // DELETE WHERE CompanyId = ? AND SourceUrl = ? — this is what makes
        // that operation cheap even on a knowledge base with tens of thousands
        // of chunks across many web sources.
        builder.HasIndex(k => new { k.CompanyId, k.SourceUrl })
            .HasFilter("\"SourceUrl\" IS NOT NULL");

        builder.HasOne(k => k.WebSource)
            .WithMany()
            .HasForeignKey(k => k.WebSourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
