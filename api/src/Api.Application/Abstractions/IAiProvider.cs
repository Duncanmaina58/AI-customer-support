namespace Api.Application.Abstractions;

public record AiReplyRequest(
    Guid CompanyId,
    Guid ConversationId,
    string CustomerMessage,
    IReadOnlyList<string> RecentHistory,
    IReadOnlyList<string> RetrievedKnowledgeChunks,
    IReadOnlyList<AvailableAction>? AvailableActions = null);

/// <summary>
/// Sprint 4 adds TokensUsed so the hub/webhook can track consumption against
/// Company.MonthlyTokenBudget. Default = 0 keeps PlaceholderAiProvider unchanged.
///
/// Sprint 9 adds Intent — non-null only when the model's response included a
/// parseable &lt;intent&gt; block (see GroqChatProvider); null for
/// PlaceholderAiProvider and for any response where parsing failed, in which
/// case the caller should treat it exactly like IntentType.Question.
/// </summary>
public record AiReplyResult(
    string ReplyText,
    double ConfidenceScore,
    string ModelUsed,
    int TokensUsed = 0,
    DetectedIntent? Intent = null);

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

// ---------------------------------------------------------------------------
// Sprint 9 — AI Action Engine: intent classification types shared between the
// system-prompt builder (what to tell the model), the response parser (what
// to read back out of it), and every chat pipeline (what to do about it).
//
// Deliberately NOT named ActionContext/ActionResult (the Phase 2 spec's own
// names for the runner-level types below) — those collide with
// Microsoft.AspNetCore.Mvc.ActionResult, which every controller in this
// codebase already has `using Microsoft.AspNetCore.Mvc;` for. Same reasoning
// applies to the runner types in Api.Infrastructure.Actions.
// ---------------------------------------------------------------------------

/// <summary>The three shapes a customer message can take, per the model's own classification.</summary>
public enum IntentType
{
    /// <summary>Answerable from the knowledge base — the existing Sprint 4 RAG flow, unchanged.</summary>
    Question,

    /// <summary>The customer wants something DONE, not answered — routed to the Action Engine.</summary>
    Action,

    /// <summary>
    /// The model believes this needs a human. Parsed but intentionally NOT acted
    /// on in Sprint 9 — EscalationService already owns escalation via a proven,
    /// separately-tested mechanism (confidence score + rule evaluation); wiring
    /// this second, AI-self-reported signal into the same decision is a
    /// reasonable future enhancement but out of scope here to keep this
    /// sprint's blast radius contained to the Action Engine itself.
    /// </summary>
    Escalate,
}

/// <summary>The model's self-reported classification of one customer message, parsed from the response's trailing &lt;intent&gt; JSON block.</summary>
public record DetectedIntent(
    IntentType Type,
    string? ActionType = null,
    IReadOnlyDictionary<string, string>? Parameters = null,
    double Confidence = 1.0);

/// <summary>One row of "things the AI can do for this company" — surfaced in the system prompt so the model knows what's available to route to. Built from ActionDefinition by ActionEngineService/IActionRegistry, never the entity itself (keeps the AI-facing shape decoupled from storage).</summary>
public record AvailableAction(string ActionType, string DisplayName, string? ParameterSchema);

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
