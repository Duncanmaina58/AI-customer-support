using Api.Application.Abstractions;
using Api.Contracts.Billing;
using Api.Contracts.Webhooks;
using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.Billing;
using Api.Infrastructure.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

/// <summary>
/// Sprint 7: subscription plans + M-Pesa STK Push billing.
///
/// STK Push is inherently a two-step async flow: InitiateMpesaPayment sends
/// the prompt to the customer's phone and returns immediately (Daraja's HTTP
/// response only confirms the *push was sent*, not that payment succeeded);
/// the customer then enters their M-Pesa PIN on their phone, and Safaricom
/// calls MpesaCallback with the actual result some seconds later. The
/// dashboard polls GetTransactionStatus in between.
/// </summary>
[ApiController]
[Route("api/billing")]
public class BillingController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly ICurrentTenantProvider _tenant;
    private readonly IMpesaClient _mpesa;
    private readonly IBrevoEmailClient _brevo;
    private readonly ILogger<BillingController> _logger;

    public BillingController(
        IAppDbContext db,
        ICurrentTenantProvider tenant,
        IMpesaClient mpesa,
        IBrevoEmailClient brevo,
        ILogger<BillingController> logger)
    {
        _db = db;
        _tenant = tenant;
        _mpesa = mpesa;
        _brevo = brevo;
        _logger = logger;
    }

    /// <summary>Public — used by the pricing page for new signups and the in-dashboard billing page alike.</summary>
    [HttpGet("plans")]
    [AllowAnonymous]
    public ActionResult<IReadOnlyList<BillingPlanDto>> GetPlans()
    {
        var plans = BillingPlanCatalog.Plans.Values
            .Select(p => new BillingPlanDto(
                p.Plan.ToString(), p.Name, p.PriceKes, p.ConversationLimit,
                p.ChannelLimit, p.KnowledgeBaseLimit, p.AgentLimit, p.Features))
            .ToList();

        return Ok(plans);
    }

    [HttpGet("info")]
    [Authorize]
    public async Task<ActionResult<BillingInfoDto>> GetInfo(CancellationToken ct)
    {
        if (_tenant.CompanyId is not { } companyId) return Unauthorized();

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null) return NotFound();

        var planInfo = BillingPlanCatalog.Get(company.Plan);
        var percentUsed = company.MonthlyTokenBudget > 0
            ? Math.Round((double)company.TokensUsedThisMonth / company.MonthlyTokenBudget * 100, 1)
            : 0;

        return Ok(new BillingInfoDto(
            CurrentPlan:          company.Plan.ToString(),
            CurrentPlanPriceKes:  planInfo.PriceKes,
            MonthlyTokenBudget:   company.MonthlyTokenBudget,
            TokensUsedThisMonth:  company.TokensUsedThisMonth,
            PercentUsed:          percentUsed,
            CurrentPeriodStartAt: company.CurrentPeriodStartAt,
            // Rolling 30-day window — see BillingPeriodResetService's doc comment for why.
            NextResetAt:          company.CurrentPeriodStartAt.AddDays(30),
            BillingPhoneNumber:   company.BillingPhoneNumber));
    }

    /// <summary>Sends an STK Push prompt to the customer's phone. Returns immediately — poll GetTransactionStatus for the outcome.</summary>
    [HttpPost("mpesa/initiate")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<InitiateMpesaPaymentResponse>> InitiateMpesaPayment(
        [FromBody] InitiateMpesaPaymentRequest request, CancellationToken ct)
    {
        if (_tenant.CompanyId is not { } companyId) return Unauthorized();

        if (!Enum.TryParse<CompanyPlan>(request.Plan, true, out var plan))
            return BadRequest(new { message = $"Unknown plan '{request.Plan}'." });

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return BadRequest(new { message = "Phone number is required." });

        var planInfo = BillingPlanCatalog.Get(plan);

        var result = await _mpesa.InitiateStkPushAsync(
            phoneNumber:            request.PhoneNumber,
            amountKes:              planInfo.PriceKes,
            accountReference:       $"Plan-{planInfo.Name}",
            transactionDescription: $"{planInfo.Name} plan subscription",
            ct:                     ct);

        if (!result.Success)
        {
            return BadRequest(new { message = result.ErrorMessage ?? "Couldn't initiate the M-Pesa payment." });
        }

        var transaction = new MpesaTransaction
        {
            CompanyId         = companyId,
            RequestedPlan     = plan,
            PhoneNumber       = request.PhoneNumber.Trim(),
            AmountKes         = planInfo.PriceKes,
            CheckoutRequestId = result.CheckoutRequestId!,
            MerchantRequestId = result.MerchantRequestId!,
            Status            = MpesaTransactionStatus.Pending,
        };
        _db.MpesaTransactions.Add(transaction);

        // Remember the phone number for next time — convenience prefill only.
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is not null) company.BillingPhoneNumber = request.PhoneNumber.Trim();

        await _db.SaveChangesAsync(ct);

        return Ok(new InitiateMpesaPaymentResponse(transaction.Id, transaction.CheckoutRequestId));
    }

    [HttpGet("mpesa/status/{transactionId:guid}")]
    [Authorize]
    public async Task<ActionResult<MpesaTransactionStatusDto>> GetTransactionStatus(Guid transactionId, CancellationToken ct)
    {
        var transaction = await _db.MpesaTransactions.FirstOrDefaultAsync(t => t.Id == transactionId, ct);
        if (transaction is null) return NotFound();

        return Ok(new MpesaTransactionStatusDto(
            transaction.Id, transaction.Status.ToString(), transaction.ResultDescription, transaction.MpesaReceiptNumber));
    }

    /// <summary>
    /// Safaricom calls this asynchronously once the customer has entered (or
    /// cancelled/ignored) the PIN prompt. Always returns 200 — Daraja doesn't
    /// meaningfully retry on non-200 the way some webhook providers do, but
    /// there's still no reason to expose internal errors to Safaricom's caller.
    /// </summary>
    [HttpPost("mpesa/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> MpesaCallback([FromBody] MpesaCallbackPayload payload, CancellationToken ct)
    {
        try
        {
            var callback = payload.Body?.StkCallback;
            if (callback?.CheckoutRequestId is null)
            {
                _logger.LogWarning("M-Pesa callback missing CheckoutRequestID");
                return Ok();
            }

            var transaction = await _db.MpesaTransactions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.CheckoutRequestId == callback.CheckoutRequestId, ct);

            if (transaction is null)
            {
                _logger.LogWarning("M-Pesa callback for unknown CheckoutRequestID={Id}", callback.CheckoutRequestId);
                return Ok();
            }

            // Idempotency: Safaricom can retry a callback delivery. Once this
            // transaction has a final status, don't re-apply the plan change
            // or send a second receipt email.
            if (transaction.Status != MpesaTransactionStatus.Pending) return Ok();

            transaction.ResultCode        = callback.ResultCode.ToString();
            transaction.ResultDescription = callback.ResultDesc;
            transaction.CompletedAt       = DateTime.UtcNow;

            if (callback.IsSuccess)
            {
                transaction.Status             = MpesaTransactionStatus.Success;
                transaction.MpesaReceiptNumber = callback.GetMetadataValue("MpesaReceiptNumber");

                var company = await _db.Companies
                    .FirstOrDefaultAsync(c => c.Id == transaction.CompanyId, ct);

                if (company is not null)
                {
                    company.Plan = transaction.RequestedPlan;
                    company.MonthlyTokenBudget = BillingPlanCatalog.Get(transaction.RequestedPlan).MonthlyTokenBudget;
                    // A plan upgrade/renewal resets the usage clock — same
                    // rationale as BillingPeriodResetService's rollover.
                    company.CurrentPeriodStartAt = DateTime.UtcNow;
                    company.TokensUsedThisMonth = 0;
                    company.TokenBudgetWarningSentAt = null;

                    await SendReceiptEmailAsync(company, transaction, ct);
                }
            }
            else
            {
                transaction.Status = callback.ResultCode == 1032
                    ? MpesaTransactionStatus.Cancelled // 1032 = "Request cancelled by user"
                    : MpesaTransactionStatus.Failed;
            }

            await _db.SaveChangesAsync(ct);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in M-Pesa callback");
            return Ok();
        }
    }

    private async Task SendReceiptEmailAsync(Company company, MpesaTransaction transaction, CancellationToken ct)
    {
        try
        {
            var owner = await _db.Agents
                .IgnoreQueryFilters()
                .Where(a => a.CompanyId == company.Id && a.IsActive && a.Role == AgentRole.Owner)
                .FirstOrDefaultAsync(ct);

            if (owner is null) return;

            var planInfo = BillingPlanCatalog.Get(transaction.RequestedPlan);
            var body =
                $"Hi {owner.Name},\n\n" +
                $"Your payment of KES {transaction.AmountKes:N2} for the {planInfo.Name} plan was received.\n\n" +
                $"M-Pesa receipt: {transaction.MpesaReceiptNumber}\n" +
                $"Plan: {planInfo.Name} ({planInfo.PriceKes:N0} KES/month)\n\n" +
                $"Your new usage period started just now.\n\n" +
                $"Thank you for your business.\n" +
                $"— AI Support Platform";

            await _brevo.SendAsync(new BrevoOutboundEmail(
                SenderName:  "AI Support Platform",
                SenderEmail: "globaljobhubplatform@gmail.com",
                ToEmail:     owner.Email,
                ToName:      owner.Name,
                Subject:     $"Payment received — {planInfo.Name} plan activated",
                TextContent: body), ct);
        }
        catch (Exception ex)
        {
            // Receipt email is a nice-to-have, not load-bearing — the plan
            // change above already committed regardless of whether this succeeds.
            _logger.LogError(ex, "Failed to send M-Pesa receipt email | company={CompanyId}", company.Id);
        }
    }
}
