namespace Api.Application.Abstractions;

public record AiReplyRequest(
    Guid CompanyId,
    Guid ConversationId,
    string CustomerMessage,
    IReadOnlyList<string> RecentHistory,
    IReadOnlyList<string> RetrievedKnowledgeChunks);

/// <summary>
/// Sprint 4 adds TokensUsed so the hub/webhook can track consumption against
/// Company.MonthlyTokenBudget. Default = 0 keeps PlaceholderAiProvider unchanged.
/// </summary>
public record AiReplyResult(
    string ReplyText,
    double ConfidenceScore,
    string ModelUsed,
    int TokensUsed = 0);

/// <summary>
/// Generates the AI's reply text. PlaceholderAiProvider is the Sprint 3 stub;
/// GroqChatProvider is the real implementation (Llama via Groq LPU + pgvector RAG).
/// The DI registration in DependencyInjection.cs is the only thing that changes
/// when swapping implementations.
/// </summary>
public interface IAiProvider
{
    Task<AiReplyResult> GenerateReplyAsync(
        AiReplyRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Distinguishes the two embedding use-cases that Cohere (and other
/// asymmetric-embedding models) require separate inputs for:
///
///   Document — text being indexed into the knowledge base (KnowledgeController)
///   Query    — customer question being matched against indexed documents (RagService)
///
/// Cohere's embed-multilingual-v3.0 is trained with these two roles separated.
/// Passing the wrong type still produces a valid embedding, but retrieval quality
/// degrades noticeably — search_query vs search_document is not optional.
///
/// OpenAI's API has no equivalent distinction (all input is treated the same),
/// so this parameter was not needed when we used text-embedding-3-small.
/// </summary>
public enum EmbeddingInputType
{
    /// <summary>Used when embedding a customer's query for nearest-neighbour search.</summary>
    Query,

    /// <summary>Used when embedding knowledge-base content at index/ingest time.</summary>
    Document,
}

/// <summary>
/// Turns text into a dense float vector for semantic similarity search.
/// Implemented by CohereEmbeddingProvider (embed-multilingual-v3.0, 1024-dim).
/// </summary>
public interface IEmbeddingProvider
{
    /// <param name="text">The text to embed.</param>
    /// <param name="inputType">
    ///   Query (default) for retrieval; Document for knowledge-base ingestion.
    ///   Always pass the correct value — it affects Cohere retrieval quality.
    /// </param>
    Task<float[]> EmbedAsync(
        string text,
        EmbeddingInputType inputType = EmbeddingInputType.Query,
        CancellationToken cancellationToken = default);
}
