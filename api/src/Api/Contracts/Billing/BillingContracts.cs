namespace Api.Contracts.Billing;

public record BillingPlanDto(
    string  Plan,
    string  Name,
    decimal PriceKes,
    int     ConversationLimit,
    int     ChannelLimit,
    int     KnowledgeBaseLimit,
    int     AgentLimit,
    IReadOnlyList<string> Features);

public record BillingInfoDto(
    string   CurrentPlan,
    decimal  CurrentPlanPriceKes,
    int      MonthlyTokenBudget,
    int      TokensUsedThisMonth,
    double   PercentUsed,
    DateTime CurrentPeriodStartAt,
    DateTime NextResetAt,
    string?  BillingPhoneNumber);

public record InitiateMpesaPaymentRequest(string Plan, string PhoneNumber);

public record InitiateMpesaPaymentResponse(Guid TransactionId, string CheckoutRequestId);

public record MpesaTransactionStatusDto(
    Guid    TransactionId,
    string  Status,
    string? ResultDescription,
    string? MpesaReceiptNumber);
