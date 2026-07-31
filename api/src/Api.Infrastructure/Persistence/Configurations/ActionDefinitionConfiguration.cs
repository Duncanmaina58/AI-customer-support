using Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Persistence.Configurations;

public class ActionDefinitionConfiguration : IEntityTypeConfiguration<ActionDefinition>
{
    public void Configure(EntityTypeBuilder<ActionDefinition> builder)
    {
        builder.ToTable("ActionDefinitions");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ActionType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(a => a.WebhookUrl).IsRequired().HasMaxLength(2000);
        builder.Property(a => a.WebhookSecretEncrypted).IsRequired();
        builder.Property(a => a.ParameterSchema).HasColumnType("text");
        builder.Property(a => a.TimeoutSeconds).HasDefaultValue(10);
        builder.Property(a => a.IsActive).HasDefaultValue(true);

        // One action_type per company - the AI's intent block names an action_type,
        // and the registry lookup (CompanyId, ActionType) must resolve unambiguously.
        builder.HasIndex(a => new { a.CompanyId, a.ActionType }).IsUnique();

        builder.HasOne(a => a.Company)
            .WithMany()
            .HasForeignKey(a => a.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
