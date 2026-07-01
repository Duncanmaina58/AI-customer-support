using Api.Application.Abstractions;
using Api.Domain.Enums;
using Api.Infrastructure.AI;
using Api.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Api.Hubs;

/// <summary>
/// Sprint 4 update: the AI reply is now grounded in the company's knowledge base
/// via the RagService → GroqChatProvider pipeline. Sprint 3 PlaceholderAiProvider
/// is retired from the DI registration (though kept in source as a test stub).
///
/// Full message pipeline per SendMessage call:
///   1. Resolve company by public key
///   2. GetOrCreate conversation
///   3. Save customer message
///   4. Retrieve relevant knowledge chunks via RagService (pgvector cosine search)
///   5. Call GroqChatProvider (Llama 3.3 70B with system-prompt + chunks)
///   6. Stream reply tokens to caller
///   7. Save AI reply + track token usage
///
/// RAG is non-fatal: if the embedding call fails (e.g., no OpenAI key in dev),
/// we log a warning and proceed with empty context — the AI will say it doesn't
/// know, which is still a valid graceful response, not a crash.
/// </summary>
[AllowAnonymous]
public class ChatHub : Hub
{
    private readonly IAppDbContext _db;
    private readonly ConversationService _conversations;
    private readonly RagService _rag;
    private readonly IAiProvider _aiProvider;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(
        IAppDbContext db,
        ConversationService conversations,
        RagService rag,
        IAiProvider aiProvider,
        ILogger<ChatHub> logger)
    {
        _db            = db;
        _conversations = conversations;
        _rag           = rag;
        _aiProvider    = aiProvider;
        _logger        = logger;
    }

    public async Task JoinCompanyGroup(string companyPublicKey)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(companyPublicKey));
    }

    public async Task SendMessage(string companyPublicKey, string sessionId, string text)
    {
        var ct = Context.ConnectionAborted;

        try
        {
            // 1. Company lookup (no tenant filter on Companies table).
            var company = await _db.Companies
                .FirstOrDefaultAsync(c => c.PublicApiKey == companyPublicKey, ct);

            if (company is null)
            {
                await Clients.Caller.SendAsync("Error", "Invalid company key.", ct);
                return;
            }

            // 2. Conversation
            var conversation = await _conversations.GetOrCreateAsync(
                company.Id, sessionId, ChannelType.WebChat, ct);

            // 3. Customer message → DB
            await _conversations.AppendMessageAsync(
                conversation.Id, company.Id, MessageRole.User, text, ct: ct);

            // 4. Signal AI is thinking
            await Clients.Caller.SendAsync("TypingStart", ct);

            // 5. Conversation history for GPT context
            var historyLines = await _conversations.GetRecentHistoryLinesAsync(
                conversation.Id, limit: 10, ct: ct);

            // 6. Semantic retrieval from knowledge base (Sprint 4 addition).
            //    Non-fatal: if embedding fails (no API key in dev) we proceed
            //    with empty context so the widget doesn't hard-error.
            IReadOnlyList<string> knowledgeChunks = [];
            try
            {
                knowledgeChunks = await _rag.RetrieveAsync(company.Id, text, topK: 4, ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "RAG retrieval failed for company {CompanyId}; proceeding without context", company.Id);
            }

            // 7. Generate AI reply
            var aiRequest = new AiReplyRequest(
                CompanyId:                  company.Id,
                ConversationId:             conversation.Id,
                CustomerMessage:            text,
                RecentHistory:              historyLines,
                RetrievedKnowledgeChunks:   knowledgeChunks);

            var aiResult = await _aiProvider.GenerateReplyAsync(aiRequest, ct);

            // 8. Stream tokens — 40 ms/word gives a natural typing feel
            var words        = aiResult.ReplyText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var replyBuilder = new System.Text.StringBuilder();

            foreach (var word in words)
            {
                ct.ThrowIfCancellationRequested();
                var token = word + " ";
                replyBuilder.Append(token);
                await Clients.Caller.SendAsync("ReceiveToken", token, ct);
                await Task.Delay(40, ct);
            }

            var fullReply = replyBuilder.ToString().TrimEnd();

            // 9. Signal completion
            await Clients.Caller.SendAsync("ReplyComplete", new
            {
                conversationId = conversation.Id.ToString(),
                fullText       = fullReply,
            }, ct);

            // 10. Persist AI reply
            await _conversations.AppendMessageAsync(
                conversation.Id, company.Id, MessageRole.Ai, fullReply,
                confidenceScore: aiResult.ConfidenceScore,
                modelUsed:       aiResult.ModelUsed,
                ct:              ct);

            // 11. Track token usage (atomic DB increment — no race on concurrent convs)
            await _conversations.IncrementTokenUsageAsync(company.Id, aiResult.TokensUsed, ct);

            _logger.LogInformation(
                "Web chat reply sent | company={CompanyId} conv={ConvId} tokens={Tokens} chunks={Chunks}",
                company.Id, conversation.Id, aiResult.TokensUsed, knowledgeChunks.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Client disconnected mid-stream | sessionId={SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ChatHub.SendMessage error | sessionId={SessionId}", sessionId);
            try { await Clients.Caller.SendAsync("Error", "Something went wrong. Please try again."); }
            catch { /* client gone */ }
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }

    private static string GroupName(string companyPublicKey) => $"company:{companyPublicKey}";
}
