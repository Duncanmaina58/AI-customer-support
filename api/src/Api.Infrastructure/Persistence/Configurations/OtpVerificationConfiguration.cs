using Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Persistence.Configurations;

public class OtpVerificationConfiguration : IEntityTypeConfiguration<OtpVerification>
{
    public void Configure(EntityTypeBuilder<OtpVerification> builder)
    {
        builder.ToTable("OtpVerifications");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.CustomerId).IsRequired().HasMaxLength(200);
        builder.Property(o => o.OtpHash).IsRequired().HasMaxLength(200);
        builder.Property(o => o.Purpose).IsRequired().HasMaxLength(100);
        builder.Property(o => o.ActionType).IsRequired().HasMaxLength(100);
        builder.Property(o => o.PendingParametersJson).IsRequired().HasColumnType("text");

        // Hot path for ActionEngineService.TryHandlePendingVerificationAsync:
        // "the most recent still-redeemable OTP for this conversation".
        builder.HasIndex(o => new { o.ConversationId, o.VerifiedAt, o.ExpiresAt });

        builder.Ignore(o => o.IsActive);

        builder.HasOne(o => o.Company)
            .WithMany()
            .HasForeignKey(o => o.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        // No Conversation navigation property, same reasoning as ActionLog. Restrict
        // (not Cascade) even though this is short-lived state - Company already
        // cascades to OtpVerification directly above, so a second Cascade path via
        // Conversation -> Company would be a multi-cascade-path conflict.
        builder.HasOne<Api.Domain.Entities.Conversation>()
            .WithMany()
            .HasForeignKey(o => o.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
