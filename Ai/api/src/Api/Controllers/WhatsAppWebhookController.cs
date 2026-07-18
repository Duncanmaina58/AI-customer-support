using System.Text.Json;
using Api.Application.Abstractions;
using Api.Contracts.Webhooks;
using Api.Domain.Enums;
using Api.Filters;
using Api.Infrastructure.AI;
using Api.Infrastructure.Channels;
using Api.Infrastructure.Security;
using Api.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Api.Controllers;

/// <summary>
/// Sprint 5 update: after AI reply generation, EscalationService evaluates rules.
/// If escalated, TicketService creates a ticket and the customer receives a
/// "ticket created" WhatsApp message instead of the uncertain AI reply.
/// Always returns HTTP 200 to prevent Meta from retrying or suspending the webhook.
///
/// Sprint 8 security hardening: the POST endpoint is protected by
/// VerifyMetaSignatureAttribute, which verifies Meta's X-Hub-Signature-256
/// HMAC before model binding even runs — see Api/Filters/VerifyMetaSignatureAttribute.cs.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("webhook/whatsapp")]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly IAppDbContext              _db;
    private readonly IChannelCredentialProtector _protector;
    private readonly IWhatsAppClient            _whatsApp;
    private readonly ConversationService        _conversations;
    private readonly RagService                 _rag;
    private readonly IAiProvider                _aiProvider;
    private readonly EscalationService          _escalation;
    private readonly TicketService              _tickets;
    private readonly TokenBudgetService         _tokenBudget;
    private readonly IConfiguration             _configuration;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(
        IAppDbContext              db,
        IChannelCredentialProtector protector,
        IWhatsAppClient            whatsApp,
        ConversationService        conversations,
        RagService                 rag,
        IAiProvider                aiProvider,
        EscalationService          escalation,
        TicketService              tickets,
        TokenBudgetService         tokenBudget,
        IConfiguration             configuration,
        ILogger<WhatsAppWebhookController> logger)
    {
        _db            = db;
        _protector     = protector;
        _whatsApp      = whatsApp;
        _conversations = conversations;
        _rag           = rag;
        _aiProvider    = aiProvider;
        _escalation    = escalation;
        _tickets       = tickets;
        _tokenBudget   = tokenBudget;
        _configuration = configuration;
        _logger        = logger;
    }

    [HttpGet("{companyId:guid}")]
    public IActionResult Verify(
        Guid companyId,
        [FromQuery(Name = "hub.mode")]         string? hubMode,
        [FromQuery(Name = "hub.verify_token")] string? hubVerifyToken,
        [FromQuery(Name = "hub.challenge")]    string? hubChallenge)
    {
        var expected = _configuration["WhatsApp:VerifyToken"];
        if (hubMode == "subscribe" && !string.IsNullOrEmpty(expected) && hubVerifyToken == expected)
            return Content(hubChallenge ?? string.Empty, "text/plain");

        _logger.LogWarning("WhatsApp verification failed | company={CompanyId}", companyId);
        return StatusCode(StatusCodes.Status403Forbidden);
    }

    [HttpPost("{companyId:guid}")]
    [VerifyMetaSignature]
    public async Task<IActionResult> Receive(
        Guid companyId,
        [FromBody] WhatsAppWebhookPayload payload,
        CancellationToken ct)
    {
        try
        {
            var inbound = (payload.Entry ?? [])
                .SelectMany(e => e.Changes ?? [])
                .SelectMany(c => c.Value?.Messages ?? [])
                .Where(m => !string.IsNullOrWhiteSpace(m.From)
                         && !string.IsNullOrWhiteSpace(m.Text?.Body))
                .ToList();

            if (inbound.Count == 0) return Ok();

            var connection = await _db.ChannelConnections
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c =>
                    c.CompanyId == companyId && c.Channel == ChannelType.WhatsApp, ct);

            if (connection is null || connection.Status != ChannelConnectionStatus.Active)
            {
                _logger.LogWarning("WhatsApp webhook: no active connection | company={CompanyId}", companyId);
                return Ok();
            }

            WhatsAppCredentials? credentials;
            try
            {
                credentials = JsonSerializer.Deserialize<WhatsAppCredentials>(
                    _protector.Decrypt(connection.CredentialsEncrypted));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt WhatsApp credentials | company={CompanyId}", companyId);
                return Ok();
            }

            if (credentials is null) return Ok();

            foreach (var msg in inbound)
                await ProcessMessageAsync(companyId, credentials, msg, ct);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in WhatsApp webhook | company={CompanyId}", companyId);
            return Ok(); // always 200 to Meta
        }
    }

    private async Task ProcessMessageAsync(
        Guid companyId, WhatsAppCredentials credentials,
        WhatsAppInboundMessage inbound, CancellationToken ct)
    {
        var phone = inbound.From!;
        var text  = inbound.Text!.Body!;

        try
        {
            var conversation = await _conversations.GetOrCreateAsync(
                companyId, phone, ChannelType.WhatsApp, ct: ct);

            await _conversations.AppendMessageAsync(
                conversation.Id, companyId, MessageRole.User, text, ct: ct);

            // Sprint 7: 100% token budget cutoff — pause the AI, but still
            // create a ticket so the customer isn't left hanging.
            if (await _tokenBudget.IsOverBudgetAsync(companyId, ct))
            {
                var budgetTicket = await _tickets.CreateAsync(
                    companyId:        companyId,
                    conversationId:   conversation.Id,
                    subject:          text.Length > 120 ? text[..120] : text,
                    priority:         TicketPriority.Medium,
                    escalationReason: "Monthly AI token budget exceeded",
                    ct:               ct);

                await _whatsApp.SendTextMessageAsync(
                    credentials, phone,
                    "Your AI conversation limit has been reached. Upgrade your plan or wait until your next " +
                    $"billing date. Our team has been notified (ticket #{budgetTicket.TicketNumber}) and will follow up.",
                    ct);
                return;
            }

            var historyLines = await _conversations.GetRecentHistoryLinesAsync(
                conversation.Id, limit: 10, ct: ct);

            IReadOnlyList<string> chunks = [];
            try { chunks = await _rag.RetrieveAsync(companyId, text, topK: 4, ct: ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RAG failed | company={CompanyId}", companyId);
            }

            var aiResult = await _aiProvider.GenerateReplyAsync(new AiReplyRequest(
                CompanyId:                companyId,
                ConversationId:           conversation.Id,
                CustomerMessage:          text,
                RecentHistory:            historyLines,
                RetrievedKnowledgeChunks: chunks), ct);

            // Sprint 5: escalation check
            var escalation = await _escalation.EvaluateAsync(
                companyId, conversation.Id, text, aiResult.ConfidenceScore, ct);

            string replyText;
            if (escalation.ShouldEscalate)
            {
                var ticket = await _tickets.CreateAsync(
                    companyId:        companyId,
                    conversationId:   conversation.Id,
                    subject:          text.Length > 120 ? text[..120] : text,
                    priority:         escalation.Priority,
                    assignedTeam:     escalation.AssignedTeam,
                    escalationReason: escalation.Reason,
                    ct:               ct);

                replyText =
                    $"I've raised a support ticket (#{ticket.TicketNumber}) for you. " +
                    $"Our team will follow up shortly. Your reference is #{ticket.TicketNumber}.";

                await _conversations.AppendMessageAsync(
                    conversation.Id, companyId, MessageRole.System,
                    $"[Escalated — {escalation.Reason}] AI draft: {aiResult.ReplyText}",
                    ct: ct);
            }
            else
            {
                replyText = aiResult.ReplyText;
            }

            await _conversations.AppendMessageAsync(
                conversation.Id, companyId, MessageRole.Ai, replyText,
                confidenceScore: aiResult.ConfidenceScore,
                modelUsed:       aiResult.ModelUsed,
                tokensUsed:      aiResult.TokensUsed,
                ct:              ct);

            await _conversations.IncrementTokenUsageAsync(companyId, aiResult.TokensUsed, ct);
            await _tokenBudget.CheckAndSendBudgetWarningIfNeededAsync(companyId, ct);
            await _whatsApp.SendTextMessageAsync(credentials, phone, replyText, ct);

            _logger.LogInformation(
                "WhatsApp reply sent | company={CompanyId} conv={ConvId} escalated={Escalated}",
                companyId, conversation.Id, escalation.ShouldEscalate);
        }
        catch (WhatsAppApiException ex)
        {
            _logger.LogError(ex, "WhatsApp send failed | company={CompanyId} to={Phone}", companyId, phone);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WhatsApp msg | company={CompanyId} from={Phone}", companyId, phone);
        }
    }
}
