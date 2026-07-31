using Api.Domain.Common;

namespace Api.Domain.Entities;

/// <summary>
/// Sprint 9 Action Engine: tracks one pending OTP challenge for a
/// RequiresVerification action. The customer's *next* message in this
/// conversation is checked against this record before it's treated as a
/// fresh question — see ActionEngineService.TryHandlePendingVerificationAsync,
/// invoked by the chat pipelines (ChatHub / ChatChannelPipelineService)
/// before RAG/AI runs at all.
///
/// SMS delivery (Africa's Talking) is Sprint 10's job per the Phase 2 build
/// plan — this table and OtpService's generate/hash/verify logic are Sprint 9
/// scaffolding so Sprint 10 only has to plug in a real ISmsSender, not design
/// the verification state machine too. See Api.Infrastructure.Actions.ISmsSender.
/// </summary>
public class OtpVerification : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public Guid ConversationId { get; set; }

    /// <summary>Phone number (or other channel identifier) the OTP was sent to — the verification target, not necessarily the conversation's CustomerId if those ever diverge.</summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>BCrypt hash of the 6-digit code. Never store the plaintext OTP.</summary>
    public string OtpHash { get; set; } = string.Empty;

    public string Purpose { get; set; } = "action_verification";

    /// <summary>Which action this verification unblocks once completed.</summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>The action's extracted parameters, captured at OTP-send time so ActionEngineService can execute the *original* request once verified, without asking the customer to repeat themselves.</summary>
    public string PendingParametersJson { get; set; } = string.Empty;

    /// <summary>Failed verification attempts. Locked out at 3 — see OtpService.VerifyAsync.</summary>
    public int AttemptCount { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True if this record can still be redeemed — unused, unexpired, and under the attempt limit.</summary>
    public bool IsActive => VerifiedAt is null && DateTime.UtcNow < ExpiresAt && AttemptCount < 3;
}
