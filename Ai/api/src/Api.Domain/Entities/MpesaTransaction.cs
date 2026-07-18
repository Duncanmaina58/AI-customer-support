using Api.Domain.Common;
using Api.Domain.Enums;

namespace Api.Domain.Entities;

/// <summary>
/// Sprint 7: one M-Pesa STK Push payment attempt. Created the moment
/// BillingController.InitiateMpesaPayment calls Daraja, updated when
/// BillingController.MpesaCallback receives Safaricom's async result — STK
/// Push is inherently async (the customer has to actually enter their PIN on
/// their phone), so there's always a real gap between "initiated" and "known
/// outcome" that this row's Status field bridges.
/// </summary>
public class MpesaTransaction : AuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public CompanyPlan RequestedPlan { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public decimal AmountKes { get; set; }

    /// <summary>Daraja's identifiers for this specific STK push — used to match the async callback back to this row.</summary>
    public string CheckoutRequestId { get; set; } = string.Empty;
    public string MerchantRequestId { get; set; } = string.Empty;

    public MpesaTransactionStatus Status { get; set; } = MpesaTransactionStatus.Pending;

    /// <summary>Daraja's ResultCode from the callback — "0" means success, anything else is a specific failure reason.</summary>
    public string? ResultCode { get; set; }
    public string? ResultDescription { get; set; }

    /// <summary>Safaricom's own receipt number (CallbackMetadata item "MpesaReceiptNumber") — shown to the customer as proof of payment.</summary>
    public string? MpesaReceiptNumber { get; set; }

    public DateTime? CompletedAt { get; set; }
}
