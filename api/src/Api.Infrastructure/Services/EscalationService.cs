using System.Text.Json;
using Api.Domain.Enums;
using Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Services;

/// <summary>
/// Evaluates whether a conversation should be escalated to a human agent based on
/// the company's configured escalation rules plus a set of always-on defaults.
///
/// Called after every AI response generation, before the reply is sent to the
/// customer. If escalation is triggered, the caller replaces the AI's reply with
/// a "ticket created" message and marks the conversation as Escalated.
///
/// Rules evaluated (in priority order):
///   1. Customer explicitly requested a human agent (always-on keyword check)
///   2. AI confidence below the configured threshold (default 0.6)
///   3. Message contains payment-related keywords (if company has rule enabled)
///
/// Rules NOT yet implemented (Sprint 6+):
///   - 3 consecutive unclear replies (needs per-conversation counter)
///   - Outside business hours + urgency detection (needs Company.BusinessHoursJson parsing)
///
/// Uses AppDbContext directly (not IAppDbContext) because it calls
/// IgnoreQueryFilters() when reading recent message counts.
/// </summary>
public class EscalationService
{
    private static readonly string[] DefaultAgentKeywords =
    [
        "agent", "human", "real person", "speak to someone", "talk to someone",
        "speak to a person", "talk to a person", "customer service", "support staff",
        "help me", "not helping", "useless", "not working"
    ];

    private static readonly string[] DefaultPaymentKeywords =
    [
        "payment", "refund", "billing", "invoice", "charge", "mpesa", "m-pesa",
        "credit card", "debit card", "money back", "overcharged", "double charge"
    ];

    private readonly AppDbContext _db;
    private readonly ILogger<EscalationService> _logger;

    public EscalationService(AppDbContext db, ILogger<EscalationService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates escalation rules for a completed AI response.
    /// Returns an <see cref="EscalationDecision"/> — callers act on it
    /// without needing to understand the rule logic themselves.
    /// </summary>
    public async Task<EscalationDecision> EvaluateAsync(
        Guid    companyId,
        Guid    conversationId,
        string  customerMessage,
        double? aiConfidenceScore,
        CancellationToken ct = default)
    {
        // Company is not tenant-scoped; normal query is fine.
        var company = await _db.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == companyId, ct);

        var rules = ParseRules(company?.EscalationRulesJson);

        var lower = customerMessage.ToLowerInvariant();

        // ---- Rule 1: explicit agent request (always evaluated, highest priority) ----
        var agentKeywords = rules.AgentRequestKeywords.Length > 0
            ? rules.AgentRequestKeywords
            : DefaultAgentKeywords;

        if (agentKeywords.Any(kw => lower.Contains(kw)))
        {
            _logger.LogInformation(
                "Escalation: agent request keyword in message | conv={ConvId}", conversationId);
            return new EscalationDecision(
                ShouldEscalate: true,
                Priority:       TicketPriority.High,
                Reason:         "Customer requested human agent",
                AssignedTeam:   rules.DefaultAssignedTeam);
        }

        // ---- Rule 2: low confidence score ----
        if (rules.EscalateOnLowConfidence
            && aiConfidenceScore.HasValue
            && aiConfidenceScore.Value < rules.ConfidenceThreshold)
        {
            _logger.LogInformation(
                "Escalation: low confidence {Score:F2} < {Threshold:F2} | conv={ConvId}",
                aiConfidenceScore.Value, rules.ConfidenceThreshold, conversationId);
            return new EscalationDecision(
                ShouldEscalate: true,
                Priority:       TicketPriority.Medium,
                Reason:         $"Low AI confidence ({aiConfidenceScore.Value:F2})",
                AssignedTeam:   rules.DefaultAssignedTeam);
        }

        // ---- Rule 3: payment keywords (optional per company) ----
        if (rules.EscalateOnPaymentKeywords)
        {
            var paymentKeywords = rules.PaymentKeywords.Length > 0
                ? rules.PaymentKeywords
                : DefaultPaymentKeywords;

            if (paymentKeywords.Any(kw => lower.Contains(kw)))
            {
                _logger.LogInformation(
                    "Escalation: payment keyword in message | conv={ConvId}", conversationId);
                return new EscalationDecision(
                    ShouldEscalate: true,
                    Priority:       TicketPriority.High,
                    Reason:         "Payment-related inquiry",
                    AssignedTeam:   rules.DefaultAssignedTeam);
            }
        }

        return EscalationDecision.None;
    }

    // -------------------------------------------------------------------------

    private static EscalationRules ParseRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new EscalationRules();

        try
        {
            return JsonSerializer.Deserialize<EscalationRules>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new EscalationRules();
        }
        catch
        {
            return new EscalationRules();
        }
    }
}

/// <summary>
/// The decision returned by EscalationService.EvaluateAsync.
/// If ShouldEscalate is false, all other fields are irrelevant.
/// </summary>
public record EscalationDecision(
    bool           ShouldEscalate,
    TicketPriority Priority,
    string?        Reason,
    string?        AssignedTeam)
{
    public static readonly EscalationDecision None =
        new(false, TicketPriority.Low, null, null);
}

/// <summary>
/// Deserialized from Company.EscalationRulesJson.
/// All fields have safe defaults so a null or empty JSON value produces
/// sensible behaviour (escalate on agent request + low confidence).
/// </summary>
internal class EscalationRules
{
    public bool     EscalateOnLowConfidence  { get; set; } = true;
    public double   ConfidenceThreshold      { get; set; } = 0.60;
    public bool     EscalateOnAgentRequest   { get; set; } = true;
    public string[] AgentRequestKeywords     { get; set; } = [];
    public bool     EscalateOnPaymentKeywords { get; set; } = false;
    public string[] PaymentKeywords          { get; set; } = [];
    public string   DefaultAssignedTeam      { get; set; } = "Support";
}
