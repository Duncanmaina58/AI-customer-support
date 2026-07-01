using System.Text.Json;
using Api.Application.Abstractions;
using Api.Contracts.Webhooks;
using Api.Domain.Enums;
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
/// Sprint 4: identical pipeline to ChatHub — inbound WhatsApp message now goes
/// through RagService + GroqChatProvider instead of the Sprint 3 placeholder.
///
/// Flow per inbound message:
///   1. Verify active channel connection
///   2. Decrypt credentials
///   3. GetOrCreate conversation (keyed on company + phone + WhatsApp)
///   4. Save customer message
///   5. Retrieve relevant knowledge chunks via pgvector cosine search
///   6. Generate AI reply (GPT-4o-mini grounded in those chunks)
///   7. Save AI reply + track token usage
///   8. Send via WhatsApp Graph API
///
/// RAG is non-fatal: embedding failure → proceed with empty chunks (graceful).
/// Always returns HTTP 200 to prevent Meta from retrying or suspending the webhook.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("webhook/whatsapp")]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly IChannelCredentialProtector _protector;
    private readonly IWhatsAppClient _whatsAppClient;
    private readonly ConversationService _conversations;
    private readonly RagService _rag;
    private readonly IAiProvider _aiProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(
        IAppDbContext db,
        IChannelCredentialProtector protector,
        IWhatsAppClient whatsAppClient,
        ConversationService conversations,
        RagService rag,
        IAiProvider aiProvider,
        IConfiguration configuration,
        ILogger<WhatsAppWebhookController> logger)
    {
        _db            = db;
        _protector     = protector;
        _whatsAppClient = whatsAppClient;
        _conversations = conversations;
        _rag           = rag;
        _aiProvider    = aiProvider;
        _configuration = configuration;
        _logger        = logger;
    }

    [HttpGet("{companyId:guid}")]
    public IActionResult Verify(
        Guid companyId,
        [FromQuery(Name = "hub.mode")] string? hubMode,
        [FromQuery(Name = "hub.verify_token")] string? hubVerifyToken,
        [FromQuery(Name = "hub.challenge")] string? hubChallenge)
    {
        var expectedToken = _configuration["WhatsApp:VerifyToken"];

        if (hubMode == "subscribe"
            && !string.IsNullOrEmpty(expectedToken)
            && hubVerifyToken == expectedToken)
        {
            return Content(hubChallenge ?? string.Empty, "text/plain");
        }

        _logger.LogWarning("WhatsApp webhook verification failed for company {CompanyId}", companyId);
        return StatusCode(StatusCodes.Status403Forbidden);
    }

    [HttpPost("{companyId:guid}")]
    public async Task<IActionResult> Receive(
        Guid companyId,
        [FromBody] WhatsAppWebhookPayload payload,
        CancellationToken ct)
    {
        try
        {
            var inboundMessages = (payload.Entry ?? [])
                .SelectMany(e => e.Changes ?? [])
                .SelectMany(c => c.Value?.Messages ?? [])
                .Where(m => !string.IsNullOrWhiteSpace(m.From)
                         && !string.IsNullOrWhiteSpace(m.Text?.Body))
                .ToList();

            if (inboundMessages.Count == 0)
                return Ok();

            var connection = await _db.ChannelConnections
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c =>
                    c.CompanyId == companyId && c.Channel == ChannelType.WhatsApp, ct);

            if (connection is null || connection.Status != ChannelConnectionStatus.Active)
            {
                _logger.LogWarning(
                    "WhatsApp webhook for company {CompanyId}: no active connection", companyId);
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
                _logger.LogError(ex, "Could not decrypt WhatsApp credentials for company {CompanyId}", companyId);
                return Ok();
            }

            if (credentials is null)
            {
                _logger.LogError("Null credentials after decrypt for company {CompanyId}", companyId);
                return Ok();
            }

            foreach (var inbound in inboundMessages)
            {
                await ProcessSingleMessageAsync(companyId, credentials, inbound, ct);
            }

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled error in WhatsApp webhook for company {CompanyId}", companyId);
            return Ok(); // always 200 to prevent Meta from disabling the webhook
        }
    }

    private async Task ProcessSingleMessageAsync(
        Guid companyId,
        WhatsAppCredentials credentials,
        WhatsAppInboundMessage inbound,
        CancellationToken ct)
    {
        var customerPhone = inbound.From!;
        var messageText   = inbound.Text!.Body!;

        try
        {
            _logger.LogInformation(
                "WhatsApp inbound | company={CompanyId} from={Phone}", companyId, customerPhone);

            // 1. Get or create conversation
            var conversation = await _conversations.GetOrCreateAsync(
                companyId, customerPhone, ChannelType.WhatsApp, ct);

            // 2. Save customer message
            await _conversations.AppendMessageAsync(
                conversation.Id, companyId, MessageRole.User, messageText, ct: ct);

            // 3. Recent history for GPT context
            var historyLines = await _conversations.GetRecentHistoryLinesAsync(
                conversation.Id, limit: 10, ct: ct);

            // 4. RAG retrieval — non-fatal, proceed with empty context on failure
            IReadOnlyList<string> knowledgeChunks = [];
            try
            {
                knowledgeChunks = await _rag.RetrieveAsync(companyId, messageText, topK: 4, ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "RAG retrieval failed for company {CompanyId}; proceeding without context", companyId);
            }

            // 5. Generate AI reply
            var aiRequest = new AiReplyRequest(
                CompanyId:                companyId,
                ConversationId:           conversation.Id,
                CustomerMessage:          messageText,
                RecentHistory:            historyLines,
                RetrievedKnowledgeChunks: knowledgeChunks);

            var aiResult = await _aiProvider.GenerateReplyAsync(aiRequest, ct);

            // 6. Save AI reply
            await _conversations.AppendMessageAsync(
                conversation.Id, companyId, MessageRole.Ai, aiResult.ReplyText,
                confidenceScore: aiResult.ConfidenceScore,
                modelUsed:       aiResult.ModelUsed,
                ct:              ct);

            // 7. Track token usage
            await _conversations.IncrementTokenUsageAsync(companyId, aiResult.TokensUsed, ct);

            // 8. Send via WhatsApp
            await _whatsAppClient.SendTextMessageAsync(
                credentials, customerPhone, aiResult.ReplyText, ct);

            _logger.LogInformation(
                "WhatsApp reply sent | company={CompanyId} conv={ConvId} tokens={Tokens} chunks={Chunks}",
                companyId, conversation.Id, aiResult.TokensUsed, knowledgeChunks.Count);
        }
        catch (WhatsAppApiException ex)
        {
            _logger.LogError(ex,
                "WhatsApp send failed | company={CompanyId} to={Phone}", companyId, customerPhone);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing WhatsApp message | company={CompanyId} from={Phone}", companyId, customerPhone);
        }
    }
}
