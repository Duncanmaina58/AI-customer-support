using System.Security.Cryptography;
using System.Text.Json;
using Api.Domain.Entities;
using Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Actions;

public enum OtpVerifyOutcome { Verified, InvalidCode, Expired, TooManyAttempts, NoPendingVerification }

public sealed record OtpVerifyResult(OtpVerifyOutcome Outcome, OtpVerification? Verification = null);

public interface IOtpService
{
    /// <summary>Generates a 6-digit code, stores its hash + the pending action's parameters, sends it via ISmsSender, and returns the new record. Any earlier still-active OtpVerification for this conversation is invalidated first — only the newest code is ever valid.</summary>
    Task<OtpVerification> SendAsync(
        Guid companyId, Guid conversationId, string customerId,
        string actionType, IReadOnlyDictionary<string, string> pendingParameters, CancellationToken ct = default);

    /// <summary>The most recent still-redeemable OtpVerification for this conversation, or null if there isn't one.</summary>
    Task<OtpVerification?> GetPendingAsync(Guid companyId, Guid conversationId, CancellationToken ct = default);

    /// <summary>Checks a customer-provided code against the pending verification, incrementing AttemptCount on failure and marking VerifiedAt on success.</summary>
    Task<OtpVerifyResult> VerifyAsync(OtpVerification verification, string providedCode, CancellationToken ct = default);
}

/// <summary>
/// Sprint 9 Action Engine: 6-digit numeric OTPs, 5-minute expiry, 3-attempt
/// lockout. Hashed with ASP.NET Core Identity's PasswordHasher — the same
/// mechanism this codebase already uses for Agent passwords (see
/// AuthController) — rather than adding a new hashing dependency for a second,
/// much shorter secret; PBKDF2 is entirely adequate for a rate-limited,
/// short-lived 6-digit code, and reusing it keeps the dependency surface small.
/// </summary>
public class OtpService : IOtpService
{
    private const int CodeLength = 6;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly ISmsSender _sms;
    private readonly ILogger<OtpService> _logger;
    private readonly PasswordHasher<object> _hasher = new();

    public OtpService(AppDbContext db, ISmsSender sms, ILogger<OtpService> logger)
    {
        _db     = db;
        _sms    = sms;
        _logger = logger;
    }

    public async Task<OtpVerification> SendAsync(
        Guid companyId, Guid conversationId, string customerId,
        string actionType, IReadOnlyDictionary<string, string> pendingParameters, CancellationToken ct = default)
    {
        // Only the newest code for a conversation is ever valid — invalidate
        // anything still outstanding before issuing a new one.
        var previouslyActive = await _db.OtpVerifications
            .Where(o => o.ConversationId == conversationId && o.VerifiedAt == null)
            .ToListAsync(ct);

        foreach (var old in previouslyActive)
        {
            old.ExpiresAt = DateTime.UtcNow; // expire in place rather than delete - keeps the audit trail intact
        }

        var code = GenerateCode();

        var verification = new OtpVerification
        {
            CompanyId  = companyId,
            ConversationId = conversationId,
            CustomerId = customerId,
            OtpHash    = _hasher.HashPassword(null!, code),
            ActionType = actionType,
            PendingParametersJson = JsonSerializer.Serialize(pendingParameters),
            ExpiresAt  = DateTime.UtcNow.Add(Lifetime),
        };

        _db.OtpVerifications.Add(verification);
        await _db.SaveChangesAsync(ct);

        var smsResult = await _sms.SendAsync(customerId, $"Your verification code is {code}. It expires in 5 minutes.", ct);
        if (!smsResult.Success)
        {
            _logger.LogError("Failed to send OTP SMS | company={CompanyId} conversation={ConversationId} error={Error}",
                companyId, conversationId, smsResult.ErrorMessage);
        }

        return verification;
    }

    public Task<OtpVerification?> GetPendingAsync(Guid companyId, Guid conversationId, CancellationToken ct = default) =>
        _db.OtpVerifications
            .Where(o => o.CompanyId == companyId && o.ConversationId == conversationId && o.VerifiedAt == null)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(o => o.ExpiresAt > DateTime.UtcNow && o.AttemptCount < 3, ct);

    public async Task<OtpVerifyResult> VerifyAsync(OtpVerification verification, string providedCode, CancellationToken ct = default)
    {
        if (verification.VerifiedAt is not null)
            return new OtpVerifyResult(OtpVerifyOutcome.NoPendingVerification);

        if (DateTime.UtcNow >= verification.ExpiresAt)
            return new OtpVerifyResult(OtpVerifyOutcome.Expired, verification);

        if (verification.AttemptCount >= 3)
            return new OtpVerifyResult(OtpVerifyOutcome.TooManyAttempts, verification);

        var checkResult = _hasher.VerifyHashedPassword(null!, verification.OtpHash, providedCode.Trim());

        if (checkResult == PasswordVerificationResult.Failed)
        {
            verification.AttemptCount++;
            await _db.SaveChangesAsync(ct);

            return verification.AttemptCount >= 3
                ? new OtpVerifyResult(OtpVerifyOutcome.TooManyAttempts, verification)
                : new OtpVerifyResult(OtpVerifyOutcome.InvalidCode, verification);
        }

        verification.VerifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new OtpVerifyResult(OtpVerifyOutcome.Verified, verification);
    }

    private static string GenerateCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString($"D{CodeLength}");
}
