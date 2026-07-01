using Api.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.AI;

/// <summary>
/// Sprint 3 placeholder implementation of IAiProvider.
///
/// Returns a canned response so the full message pipeline can be exercised and
/// tested end-to-end — webhook → normalise → conversation → message storage →
/// AI response → reply sent — without requiring an OpenAI API key yet.
///
/// Sprint 4 replaces this with GroqChatProvider, which calls Llama 3.3 via Groq's
/// LPU inference API with a pgvector RAG context assembled from the company's
/// knowledge base. The IAiProvider interface is the seam; no other code changes.
/// </summary>
public class PlaceholderAiProvider : IAiProvider
{
    private readonly ILogger<PlaceholderAiProvider> _logger;

    public PlaceholderAiProvider(ILogger<PlaceholderAiProvider> logger)
    {
        _logger = logger;
    }

    public Task<AiReplyResult> GenerateReplyAsync(
        AiReplyRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "PlaceholderAiProvider generating reply | company={CompanyId} conv={ConvId} question={Question}",
            request.CompanyId, request.ConversationId,
            request.CustomerMessage.Length > 80
                ? request.CustomerMessage[..80] + "…"
                : request.CustomerMessage);

        // Personalise slightly based on message length so it doesn't feel totally static.
        var questionPreview = request.CustomerMessage.Length > 60
            ? request.CustomerMessage[..60].TrimEnd() + "…"
            : request.CustomerMessage;

        var historyCount = request.RecentHistory.Count;
        var contextNote = historyCount > 2
            ? $"This is message #{historyCount / 2 + 1} in our conversation. "
            : string.Empty;

        var reply =
            $"Thanks for your message: \"{questionPreview}\" 👋 " +
            $"{contextNote}" +
            "I'm your AI support assistant and the end-to-end messaging pipeline is confirmed working — " +
            "your message was received, stored in the database, and this reply was generated and delivered back to you in real time. " +
            "In Sprint 4, I'll be connected to your company's knowledge base via RAG (Retrieval-Augmented Generation), " +
            "so I'll answer from your actual documents instead of this placeholder. " +
            "If you need a human right now, reply with \"agent\" and I'll escalate to your support team.";

        return Task.FromResult(new AiReplyResult(
            ReplyText: reply,
            ConfidenceScore: 0.95,
            ModelUsed: "placeholder-v1-sprint3"));
    }
}
