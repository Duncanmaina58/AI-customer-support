using Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Infrastructure.Persistence.Configurations;

public class MpesaTransactionConfiguration : IEntityTypeConfiguration<MpesaTransaction>
{
    public void Configure(EntityTypeBuilder<MpesaTransaction> builder)
    {
        builder.ToTable("MpesaTransactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.RequestedPlan).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.PhoneNumber).IsRequired().HasMaxLength(20);
        builder.Property(t => t.AmountKes).HasColumnType("decimal(12,2)");
        builder.Property(t => t.CheckoutRequestId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.MerchantRequestId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.ResultCode).HasMaxLength(16);
        builder.Property(t => t.ResultDescription).HasMaxLength(500);
        builder.Property(t => t.MpesaReceiptNumber).HasMaxLength(50);

        // The callback arrives with only CheckoutRequestId to identify which
        // transaction it belongs to — this is the lookup path, so it needs to
        // be fast and, in practice, unique (Daraja generates a fresh one per push).
        builder.HasIndex(t => t.CheckoutRequestId).IsUnique();
        builder.HasIndex(t => new { t.CompanyId, t.CreatedAt });
    }
}
