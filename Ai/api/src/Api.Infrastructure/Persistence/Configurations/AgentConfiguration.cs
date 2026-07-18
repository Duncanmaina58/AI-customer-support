using Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Persistence.Configurations;

public class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.ToTable("Agents");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Email).IsRequired().HasMaxLength(256);
        builder.Property(a => a.PasswordHash).IsRequired();
        builder.Property(a => a.LastLoginIp).HasMaxLength(64);

        // Email is globally unique across the WHOLE platform, not just per-company.
        // This matches what AuthController.Login and CompaniesController.Register
        // already assume (login looks up an Agent by email alone, with no company
        // context yet - that's the entire point of login). A composite
        // (CompanyId, Email) index would let two different companies register the
        // same email and make that lookup ambiguous, so we enforce it here at the
        // database level too (defense in depth - the app-level checks in Register
        // and AgentsController.Invite are the first line, this is the backstop
        // against a race condition between two concurrent requests).
        builder.HasIndex(a => a.Email).IsUnique();

        builder.HasOne(a => a.Company)
            .WithMany(c => c.Agents)
            .HasForeignKey(a => a.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
