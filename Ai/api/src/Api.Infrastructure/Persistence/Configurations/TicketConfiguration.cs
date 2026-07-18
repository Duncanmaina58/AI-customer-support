using Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Persistence.Configurations;

public class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TicketNumber).IsRequired();
        builder.Property(t => t.Subject).IsRequired().HasMaxLength(300);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.Priority).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.AssignedTeam).HasMaxLength(100);
        builder.Property(t => t.EscalationReason).HasMaxLength(500);

        // Uniqueness: TicketNumber is per-company, not globally unique.
        builder.HasIndex(t => new { t.CompanyId, t.TicketNumber }).IsUnique();
        builder.HasIndex(t => new { t.CompanyId, t.Status, t.Priority });

        builder.HasOne(t => t.Conversation)
            .WithMany(c => c.Tickets)
            .HasForeignKey(t => t.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.AssignedTo)
            .WithMany()
            .HasForeignKey(t => t.AssignedToId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
