using Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Persistence.Configurations;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.CustomerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.Channel).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);

        // Email threading (Sprint 5) — nullable, populated for ChannelType.Email only.
        builder.Property(c => c.EmailMessageId).HasMaxLength(512);
        builder.Property(c => c.EmailSubject).HasMaxLength(500);
        // Index on EmailMessageId so Brevo inbound lookup (WHERE EmailMessageId = ?) is fast.
        builder.HasIndex(c => c.EmailMessageId).HasFilter("\"EmailMessageId\" IS NOT NULL");

        // Hot path: "give me this company's open conversations" / "this customer's history"
        builder.HasIndex(c => new { c.CompanyId, c.Status });
        builder.HasIndex(c => new { c.CompanyId, c.CustomerId });

        builder.HasOne(c => c.Company)
            .WithMany(co => co.Conversations)
            .HasForeignKey(c => c.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.AssignedAgent)
            .WithMany()
            .HasForeignKey(c => c.AssignedAgentId)
            .OnDelete(DeleteBehavior.SetNull);

        // Sprint 7 follow-up: CSAT rating, 1-5 stars or unset.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Conversations_CsatScore_Range", "\"CsatScore\" IS NULL OR (\"CsatScore\" >= 1 AND \"CsatScore\" <= 5)"));
        builder.HasIndex(c => new { c.CompanyId, c.CsatSubmittedAt })
            .HasFilter("\"CsatSubmittedAt\" IS NOT NULL");
    }
}

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Content).IsRequired();
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(16);

        // Hot path: load a conversation's transcript in order.
        builder.HasIndex(m => new { m.ConversationId, m.SentAt });

        builder.HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Company)
            .WithMany()
            .HasForeignKey(m => m.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
