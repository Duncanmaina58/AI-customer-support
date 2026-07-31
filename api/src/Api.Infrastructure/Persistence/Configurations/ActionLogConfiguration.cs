using Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Persistence.Configurations;

public class ActionLogConfiguration : IEntityTypeConfiguration<ActionLog>
{
    public void Configure(EntityTypeBuilder<ActionLog> builder)
    {
        builder.ToTable("ActionLogs");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ActionType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.CustomerId).IsRequired().HasMaxLength(200);
        builder.Property(a => a.ParametersEncrypted).IsRequired().HasColumnType("text");
        builder.Property(a => a.VerificationMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.WebhookResponseJson).HasColumnType("text");
        builder.Property(a => a.ErrorMessage).HasColumnType("text");

        // Query patterns: "every action for this company, newest first" and
        // "every action in this conversation" (used by
        // ActionEngineService.TryHandlePendingVerificationAsync's lookup, and
        // by the dashboard's audit view).
        builder.HasIndex(a => new { a.CompanyId, a.ExecutedAt });
        builder.HasIndex(a => a.ConversationId);

        builder.HasOne(a => a.Company)
            .WithMany()
            .HasForeignKey(a => a.CompanyId)
            // Restrict, not Cascade: ActionDefinition -> Company is already Cascade,
            // and ActionLog -> ActionDefinition is SetNull below — having a second
            // Cascade path from ActionLog -> Company directly would give EF Core two
            // possible cascade routes to the same row, which it refuses to model.
            // Same pattern already used for WebPage -> Company in the Sprint 4 web
            // crawling migration.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.ActionDefinition)
            .WithMany()
            .HasForeignKey(a => a.ActionDefinitionId)
            // SetNull, not Cascade: deleting/deactivating an ActionDefinition must
            // never delete its own audit history - the whole point of this table.
            .OnDelete(DeleteBehavior.SetNull);

        // No Conversation navigation property (kept off ActionLog - nothing in this
        // codebase needs to traverse Conversation -> ActionLogs), but the FK still
        // exists for referential integrity. Restrict so a conversation can never be
        // deleted out from under its own audit trail.
        builder.HasOne<Api.Domain.Entities.Conversation>()
            .WithMany()
            .HasForeignKey(a => a.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
