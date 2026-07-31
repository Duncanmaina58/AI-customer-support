using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api.Domain.Entities;
using Api.Infrastructure.Security;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Actions;

/// <summary>Named HttpClient used for every outbound action webhook call.</summary>
public static class ActionWebhookHttpClient
{
    public const string Name = "action-webhook";
}

/// <summary>
/// Sprint 9 Action Engine: POSTs the detected action + parameters to the
/// company's own backend webhook, signed the same way Stripe/GitHub/Meta sign
/// their outbound webhooks — HMAC-SHA256 over the raw JSON body, sent as an
/// X-Webhook-Signature header, so the receiving backend can verify the call
/// genuinely came from this platform (and reuse the exact verification
/// pattern this codebase already documents for its OWN inbound webhooks — see
/// Api/Filters/VerifyMetaSignatureAttribute.cs and
/// Api.Infrastructure.Security.MetaWebhookSignature, whose Compute/FixedTimeEquals
/// helpers this reuses directly rather than re-implementing HMAC comparison a
/// second time).
/// </summary>
public class WebhookActionRunner : IAIActionRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _http;
    private readonly IActionEngineSecretProtector _protector;
    private readonly ILogger<WebhookActionRunner> _logger;

    public WebhookActionRunner(
        IHttpClientFactory httpFactory,
        IActionEngineSecretProtector protector,
        ILogger<WebhookActionRunner> logger)
    {
        _http      = httpFactory.CreateClient(ActionWebhookHttpClient.Name);
        _protector = protector;
        _logger    = logger;
    }

    public async Task<ActionRunResult> ExecuteAsync(
        ActionExecutionContext context, ActionDefinition definition, CancellationToken ct = default)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("O");

        var payload = new WebhookPayload(
            ActionType:      context.ActionType,
            Parameters:      context.Parameters,
            ConversationId:  context.ConversationId,
            CompanyId:       context.CompanyId,
            CustomerId:      context.CustomerId,
            IdentityVerified: context.IdentityVerified,
            Timestamp:       timestamp);

        var body = JsonSerializer.Serialize(payload, JsonOptions);

        string webhookSecret;
        try
        {
            webhookSecret = _protector.Decrypt(definition.WebhookSecretEncrypted);
        }
        catch (Exception ex)
        {
            // A corrupted/undecryptable secret is a configuration problem, not a
            // transient failure — fail fast with a clear reason rather than let an
            // unrelated exception type surface from deep inside signing.
            _logger.LogError(ex, "Could not decrypt webhook secret for action {ActionType} | company={CompanyId}",
                definition.ActionType, definition.CompanyId);
            return new ActionRunResult(false, "Action is misconfigured (webhook secret unreadable) — contact support.");
        }

        var signature = MetaWebhookSignature.Compute(body, webhookSecret);

        using var request = new HttpRequestMessage(HttpMethod.Post, definition.WebhookUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Webhook-Signature", $"sha256={signature}");
        request.Headers.Add("X-Webhook-Timestamp", timestamp);

        // Per-call timeout independent of the HttpClient's own default — each
        // ActionDefinition sets its own (spec: enforced at 10s default, editable
        // per action, hard-capped so a misbehaving client backend can't hang a
        // whole chat turn indefinitely).
        var timeoutSeconds = Math.Clamp(definition.TimeoutSeconds <= 0 ? 10 : definition.TimeoutSeconds, 1, 30);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var response = await _http.SendAsync(request, timeoutCts.Token);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new ActionRunResult(true, TrimForLog(responseBody), (int)response.StatusCode);
            }

            _logger.LogWarning(
                "Action webhook returned non-success | action={ActionType} company={CompanyId} status={Status}",
                definition.ActionType, definition.CompanyId, (int)response.StatusCode);
            return new ActionRunResult(false, TrimForLog(responseBody), (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Our own per-call timeout fired (not the caller cancelling the whole
            // chat turn) — this is the "10 second timeout enforced" checklist item.
            _logger.LogWarning(
                "Action webhook timed out after {Timeout}s | action={ActionType} company={CompanyId}",
                timeoutSeconds, definition.ActionType, definition.CompanyId);
            return new ActionRunResult(false, $"The action timed out after {timeoutSeconds} seconds.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Action webhook call failed | action={ActionType} company={CompanyId}",
                definition.ActionType, definition.CompanyId);
            return new ActionRunResult(false, $"Couldn't reach the action endpoint: {ex.Message}");
        }
    }

    /// <summary>Webhook responses are logged on failure for operator visibility — cap length so a misbehaving endpoint can't flood the log stream.</summary>
    private static string TrimForLog(string text) =>
        text.Length > 2000 ? text[..2000] + "…(truncated)" : text;

    private sealed record WebhookPayload(
        string ActionType,
        IReadOnlyDictionary<string, string> Parameters,
        Guid ConversationId,
        Guid CompanyId,
        string CustomerId,
        bool IdentityVerified,
        string Timestamp);
}
