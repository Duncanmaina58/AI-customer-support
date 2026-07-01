using Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Persistence.Configurations;

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("Companies");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.PublicApiKey).IsRequired().HasMaxLength(64);
        builder.Property(c => c.SecretApiKeyHash).IsRequired();
        builder.Property(c => c.DefaultCurrency).HasMaxLength(3);
        builder.Property(c => c.Industry).HasMaxLength(100);
        builder.Property(c => c.LogoUrl).HasMaxLength(2048);
        builder.Property(c => c.PrimaryLanguage).IsRequired().HasMaxLength(8);
        builder.Property(c => c.BrandVoice).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(c => c.PublicApiKey).IsUnique();
    }
}
