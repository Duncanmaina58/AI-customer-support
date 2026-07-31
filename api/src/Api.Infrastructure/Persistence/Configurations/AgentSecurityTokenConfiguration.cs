using Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Persistence.Configurations;

public class AgentSecurityTokenConfiguration : IEntityTypeConfiguration<AgentSecurityToken>
{
    public void Configure(EntityTypeBuilder<AgentSecurityToken> builder)
    {
        builder.ToTable("AgentSecurityTokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(128);
        builder.Property(t => t.RequestedFromIp).HasMaxLength(64);

        // Hot path for redeeming a token: look it up by hash + type together
        // (a stolen verification-token hash should never redeem as a password
        // reset, even in the astronomically unlikely case of a hash collision).
        builder.HasIndex(t => new { t.TokenHash, t.Type }).IsUnique();
        builder.HasIndex(t => t.AgentId);

        builder.HasOne(t => t.Agent)
            .WithMany()
            .HasForeignKey(t => t.AgentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(t => t.IsActive);
    }
}
