using Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Persistence.Configurations;

public class WebSourceConfiguration : IEntityTypeConfiguration<WebSource>
{
    public void Configure(EntityTypeBuilder<WebSource> builder)
    {
        builder.ToTable("WebSources");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Url).IsRequired().HasMaxLength(2000);
        builder.Property(w => w.CrawlMode).HasConversion<string>().HasMaxLength(20);
        builder.Property(w => w.IncludePattern).HasMaxLength(500);
        builder.Property(w => w.ExcludePattern).HasMaxLength(500);
        builder.Property(w => w.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(w => w.MonitoringMode).HasConversion<string>().HasMaxLength(20);
        builder.Property(w => w.CurrentCrawlUrl).HasMaxLength(2000);

        // A company can connect the same root URL only once — resubmitting
        // the same URL should edit/re-crawl the existing source, not duplicate it.
        builder.HasIndex(w => new { w.CompanyId, w.Url }).IsUnique();

        builder.HasOne(w => w.Company)
            .WithMany()
            .HasForeignKey(w => w.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
