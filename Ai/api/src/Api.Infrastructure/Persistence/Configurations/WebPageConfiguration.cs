using Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Persistence.Configurations;

public class WebPageConfiguration : IEntityTypeConfiguration<WebPage>
{
    public void Configure(EntityTypeBuilder<WebPage> builder)
    {
        builder.ToTable("WebPages");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Url).IsRequired().HasMaxLength(2000);
        builder.Property(w => w.Title).HasMaxLength(500);
        builder.Property(w => w.ContentHash).IsRequired().HasMaxLength(64);
        builder.Property(w => w.HttpETag).HasMaxLength(200);
        builder.Property(w => w.Status).HasConversion<string>().HasMaxLength(20);

        // One row per URL per source.
        builder.HasIndex(w => new { w.WebSourceId, w.Url }).IsUnique();

        // Hot path for ContentFreshnessService: "give me every Active page whose
        // NextCheckAt is due, oldest first, capped at 100 per sweep."
        builder.HasIndex(w => new { w.Status, w.NextCheckAt });

        // Per-tenant page listing / change-log queries.
        builder.HasIndex(w => new { w.CompanyId, w.LastChangedAt });

        builder.HasOne(w => w.WebSource)
            .WithMany(s => s.Pages)
            .HasForeignKey(w => w.WebSourceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(w => w.Company)
            .WithMany()
            .HasForeignKey(w => w.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
