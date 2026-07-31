using System.Text.Json;
using Api.Application.Abstractions;
using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.Persistence;
using Api.Infrastructure.Security;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Actions;

/// <summary>Text to show the customer, and how many extra AI tokens (if any) were spent composing it — threaded back to the pipeline's token-budget accounting.</summary>
public sealed record ActionEngineOutcome(string ReplyText, int TokensUsed = 0);

/// <summary>
/// Sprint 9 Action Engine: the single entry point every chat pipeline
/// (ChatHub, ChatChannelPipelineService) calls into. Two methods, called at
/// two different points in a turn:
///
///   1. TryHandlePendingVerificationAsync — called BEFORE RAG/AI, on every
///      non-sandbox message. If the conversation has an outstanding OTP
///      challenge, this intercepts the message as the verification attempt
///      instead of letting it reach the model as a fresh question.
///
///   2. HandleDetectedActionAsync — called AFTER the model responds, only
///      when its parsed &lt;intent&gt; block says type=action. Looks up the
///      registered ActionDefinition, sends an OTP if required, or executes
///      the webhook immediately, and always writes exactly one ActionLog row.
///
/// Both are DB/HTTP-bound (integration-level), so they're exercised by
/// ActionEngineTests using an in-memory DB + a fake HttpMessageHandler rather
/// than pure unit tests — see that file for the true end-to-end coverage.
/// </summary>
public class ActionEngineService
{
    private readonly AppDbContext _db;
    private readonly IActionRegistry _registry;
    private readonly IAIActionRunner _runner;
    private readonly IOtpService _otp;
    private readonly IAiProvider _aiProvider;
    private readonly IActionEngineSecretProtector _protector;
    private readonly ILogger<ActionEngineService> _logger;

    public ActionEngineService(
        AppDbContext db,
        IActionRegistry registry,
        IAIActionRunner runner,
        IOtpService otp,
        IAiProvider aiProvider,
        IActionEngineSecretProtector protector,
        ILogger<ActionEngineService> logger)
    {
        _db         = db;
        _registry   = registry;
        _runner     = runner;
        _otp        = otp;
        _aiProvider = aiProvider;
        _protector  = protector;
        _logger     = logger;
    }

    public async Task<ActionEngineOutcome?> TryHandlePendingVerificationAsync(
        Guid companyId, Guid conversationId, string customerId, string incomingText, CancellationToken ct = default)
    {
        var pending = await _otp.GetPendingAsync(companyId, conversationId, ct);
        if (pending is null) return null;

        if (string.Equals(incomingText.Trim(), "cancel", StringComparison.OrdinalIgnoreCase))
        {
            pending.ExpiresAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new ActionEngineOutcome("No problem — I've cancelled that request. Let me know if there's anything else I can help with.");
        }

        var code = ExtractSixDigitCode(incomingText);
        if (code is null)
        {
            return new ActionEngineOutcome(
                "I need the 6-digit verification code we just sent you to continue — please enter it, or say \"cancel\" to stop.");
        }

        var verifyResult = await _otp.VerifyAsync(pending, code, ct);

        switch (verifyResult.Outcome)
        {
            case OtpVerifyOutcome.Verified:
                return await ExecuteVerifiedActionAsync(pending, ct);

            case OtpVerifyOutcome.InvalidCode:
                var attemptsLeft = 3 - pending.AttemptCount;
                return new ActionEngineOutcome(
                    $"That code doesn't match — {attemptsLeft} attempt{(attemptsLeft == 1 ? "" : "s")} left. Please double-check and try again.");

            case OtpVerifyOutcome.TooManyAttempts:
                return new ActionEngineOutcome(
                    "That's too many incorrect attempts — for your security, this verification has been cancelled. Please ask again if you'd still like to proceed.");

            case OtpVerifyOutcome.Expired:
                return new ActionEngineOutcome(
                    "That verification code has expired. Please ask again if you'd still like to proceed, and we'll send a fresh code.");

            default:
                return null; // NoPendingVerification - shouldn't happen given the GetPendingAsync guard above, but fail safe into normal processing
        }
    }

    public async Task<ActionEngineOutcome> HandleDetectedActionAsync(
        Guid companyId, Guid conversationId, string customerId, string originalMessage, DetectedIntent intent, CancellationToken ct = default)
    {
        var actionType = intent.ActionType!;
        var parameters = intent.Parameters ?? new Dictionary<string, string>();

        var definition = await _registry.FindAsync(companyId, actionType, ct);

        if (definition is null)
        {
            await WriteLogAsync(
                companyId: companyId,
                conversationId: conversationId,
                customerId: customerId,
                actionType: actionType,
                actionDefinitionId: null,
                parameters: parameters,
                identityVerified: false,
                verificationMethod: VerificationMethod.None,
                success: false,
                statusCode: null,
                responseBody: null,
                errorMessage: "No ActionDefinition registered for this action_type.",
                durationMs: 0,
                ct: ct);

            return new ActionEngineOutcome(
                $"I understand you'd like help with that, but I'm not able to perform that action yet — " +
                "let me connect you with our team, or feel free to ask me something else.");
        }

        if (definition.RequiresVerification && !definition.IsReadOnly)
        {
            await _otp.SendAsync(companyId, conversationId, customerId, actionType, parameters, ct);

            return new ActionEngineOutcome(
                $"Before I can {definition.DisplayName.ToLowerInvariant()}, I need to verify it's really you — " +
                "I've just sent a 6-digit code to your phone. Please reply with that code to continue.");
        }

        return await ExecuteAndLogAsync(
            definition: definition,
            conversationId: conversationId,
            customerId: customerId,
            originalMessage: originalMessage,
            parameters: parameters,
            identityVerified: false,
            verificationMethod: VerificationMethod.None,
            ct: ct);
    }

    // -------------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------------

    private async Task<ActionEngineOutcome> ExecuteVerifiedActionAsync(OtpVerification verification, CancellationToken ct)
    {
        var definition = await _registry.FindAsync(verification.CompanyId, verification.ActionType, ct);
        var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(verification.PendingParametersJson)
                          ?? new Dictionary<string, string>();

        if (definition is null)
        {
            await WriteLogAsync(
                companyId: verification.CompanyId,
                conversationId: verification.ConversationId,
                customerId: verification.CustomerId,
                actionType: verification.ActionType,
                actionDefinitionId: null,
                parameters: parameters,
                identityVerified: true,
                verificationMethod: VerificationMethod.OtpSms,
                success: false,
                statusCode: null,
                responseBody: null,
                errorMessage: "ActionDefinition was removed or deactivated after the OTP was sent.",
                durationMs: 0,
                ct: ct);

            return new ActionEngineOutcome(
                "Thanks — you're verified, but that action isn't available anymore. Please let us know what you need and we'll help another way.");
        }

        return await ExecuteAndLogAsync(
            definition: definition,
            conversationId: verification.ConversationId,
            customerId: verification.CustomerId,
            originalMessage: $"[verified via OTP] {verification.ActionType}",
            parameters: parameters,
            identityVerified: true,
            verificationMethod: VerificationMethod.OtpSms,
            ct: ct);
    }

    private async Task<ActionEngineOutcome> ExecuteAndLogAsync(
        ActionDefinition definition, Guid conversationId, string customerId, string originalMessage,
        IReadOnlyDictionary<string, string> parameters, bool identityVerified, VerificationMethod verificationMethod,
        CancellationToken ct)
    {
        var context = new ActionExecutionContext(
            definition.CompanyId, conversationId, customerId, definition.ActionType, parameters, identityVerified);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        ActionRunResult result;
        try
        {
            result = await _runner.ExecuteAsync(context, definition, ct);
        }
        catch (Exception ex)
        {
            // The runner already catches its own HTTP/timeout exceptions and returns
            // a failed ActionRunResult - this catch is a last-resort safety net so a
            // truly unexpected exception still produces exactly one ActionLog row
            // (never zero) rather than propagating and losing the audit trail.
            _logger.LogError(ex, "Unexpected error executing action {ActionType} | company={CompanyId}",
                definition.ActionType, definition.CompanyId);
            result = new ActionRunResult(false, $"Unexpected error: {ex.Message}");
        }
        stopwatch.Stop();

        await WriteLogAsync(
            definition.CompanyId, conversationId, customerId, definition.ActionType, definition.Id, parameters,
            identityVerified, verificationMethod, result.Success, result.HttpStatusCode, result.Message,
            result.Success ? null : result.Message, (int)stopwatch.ElapsedMilliseconds, ct);

        var replyText = await ComposeConfirmationAsync(definition, originalMessage, result, ct);
        return new ActionEngineOutcome(replyText);
    }

    /// <summary>
    /// Reuses the existing RAG-generation path (IAiProvider.GenerateReplyAsync)
    /// to phrase a natural, brand-voice-consistent confirmation — by presenting
    /// the action's outcome AS IF it were a retrieved knowledge chunk relevant
    /// to the customer's original message. This deliberately avoids adding a
    /// second IAiProvider method: SystemPromptBuilder's existing grounding
    /// instructions ("answer only from the context provided") already produce
    /// exactly the right behaviour here with zero new prompt-engineering surface
    /// area. AvailableActions is passed empty so this call can't recursively
    /// detect a new action from its own synthetic context.
    /// </summary>
    private async Task<string> ComposeConfirmationAsync(
        ActionDefinition definition, string originalMessage, ActionRunResult result, CancellationToken ct)
    {
        var resultChunk = result.Success
            ? $"[Action Result: {definition.DisplayName}]\nThis action was just completed successfully for the customer. " +
              $"Result details from the system: {Truncate(result.Message, 1500)}"
            : $"[Action Result: {definition.DisplayName}]\nThis action could NOT be completed. " +
              $"Reason: {Truncate(result.Message, 500)}. Apologize briefly and suggest contacting support if this keeps happening. " +
              "Do not invent a reason beyond what's given here.";

        try
        {
            var composeResult = await _aiProvider.GenerateReplyAsync(new AiReplyRequest(
                CompanyId: definition.CompanyId,
                ConversationId: Guid.Empty, // this sub-call isn't itself persisted as conversation state
                CustomerMessage: originalMessage,
                RecentHistory: [],
                RetrievedKnowledgeChunks: [resultChunk],
                AvailableActions: []), ct);

            return composeResult.ReplyText;
        }
        catch (Exception ex)
        {
            // Compose is a nice-to-have phrasing pass, not the action itself (which
            // already succeeded/failed and was already logged by this point) -
            // fall back to a plain templated message rather than lose the reply
            // entirely if the AI call itself has a problem.
            _logger.LogWarning(ex, "Action confirmation compose call failed — falling back to template | action={ActionType}",
                definition.ActionType);

            return result.Success
                ? $"Done — {definition.DisplayName.ToLowerInvariant()} completed successfully."
                : $"I wasn't able to complete that ({definition.DisplayName.ToLowerInvariant()}) — please try again shortly or contact support.";
        }
    }

    private async Task WriteLogAsync(
        Guid companyId, Guid conversationId, string customerId, string actionType, Guid? actionDefinitionId,
        IReadOnlyDictionary<string, string> parameters, bool identityVerified, VerificationMethod verificationMethod,
        bool success, int? statusCode, string? responseBody, string? errorMessage, int durationMs, CancellationToken ct)
    {
        var log = new ActionLog
        {
            CompanyId          = companyId,
            ConversationId     = conversationId,
            ActionDefinitionId = actionDefinitionId,
            ActionType         = actionType,
            CustomerId         = customerId,
            ParametersEncrypted = _protector.Encrypt(JsonSerializer.Serialize(parameters)),
            IdentityVerified   = identityVerified,
            VerificationMethod = verificationMethod,
            WebhookStatusCode  = statusCode,
            WebhookResponseJson = responseBody,
            Success            = success,
            ErrorMessage       = errorMessage,
            DurationMs         = durationMs,
        };

        _db.ActionLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }

    private static string? ExtractSixDigitCode(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\b\d{6}\b");
        return match.Success ? match.Value : null;
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length > maxLength ? text[..maxLength] + "…" : text;
}
