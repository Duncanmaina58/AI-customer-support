using Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Persistence.Configurations;

public class ChannelConnectionConfiguration : IEntityTypeConfiguration<ChannelConnection>
{
    public void Configure(EntityTypeBuilder<ChannelConnection> builder)
    {
        builder.ToTable("ChannelConnections");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Channel).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.CredentialsEncrypted).IsRequired();

        // A company can only connect a given channel type once (e.g. one WhatsApp
        // connection per company - reconnecting overwrites rather than duplicates).
        builder.HasIndex(c => new { c.CompanyId, c.Channel }).IsUnique();

        builder.HasOne(c => c.Company)
            .WithMany(co => co.ChannelConnections)
            .HasForeignKey(c => c.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
