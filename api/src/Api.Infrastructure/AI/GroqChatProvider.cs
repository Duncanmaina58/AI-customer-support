using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Application.Abstractions;
using Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.AI;

/// <summary>
/// IAiProvider implementation backed by Groq Cloud (https://api.groq.com/openai/v1/).
///
/// Groq's API is OpenAI-compatible at the HTTP level — same JSON request/response
/// shape as the OpenAI chat completions endpoint. The only differences are:
///   - Base URL:  https://api.groq.com/openai/v1/
///   - Auth:      Bearer {GROQ_API_KEY}
///   - Models:    Llama, Mixtral, etc. (NOT gpt-* identifiers)
///
/// Default model: llama-3.3-70b-versatile
///   - Best quality / speed balance on Groq as of Sprint 4
///   - Configurable via Groq:ChatModel in appsettings
///   - Other good options: llama-3.1-8b-instant (faster/cheaper),
///                         moonshotai/kimi-k2-instruct (tool use)
///
/// This class implements ONLY IAiProvider (chat). Groq does not provide an
/// embeddings endpoint — IEmbeddingProvider is handled by CohereEmbeddingProvider
/// (embed-multilingual-v3.0, 1024-dim, 100+ languages including Swahili).
///
/// Registered as scoped. SystemPromptBuilder (singleton) is injected and
/// assembles the grounded prompt from company profile + retrieved knowledge chunks.
/// </summary>
public sealed class GroqChatProvider : IAiProvider
{
    private const string DefaultChatModel = "llama-3.3-70b-versatile";
    private const string GroqBaseUrl      = "https://api.groq.com/openai/v1/";

    private readonly IHttpClientFactory   _httpFactory;
    private readonly AppDbContext         _db;
    private readonly SystemPromptBuilder  _promptBuilder;
    private readonly IConfiguration      _configuration;
    private readonly ILogger<GroqChatProvider> _logger;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public GroqChatProvider(
        IHttpClientFactory httpFactory,
        AppDbContext db,
        SystemPromptBuilder promptBuilder,
        IConfiguration configuration,
        ILogger<GroqChatProvider> logger)
    {
        _httpFactory   = httpFactory;
        _db            = db;
        _promptBuilder = promptBuilder;
        _configuration = configuration;
        _logger        = logger;
    }

    public async Task<AiReplyResult> GenerateReplyAsync(
        AiReplyRequest request,
        CancellationToken cancellationToken = default)
    {
        // Load company for the system prompt (no tenant filter on Companies table —
        // this method is called from ChatHub and WhatsApp webhook with no JWT context).
        var company = await _db.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken);

        if (company is null)
        {
            _logger.LogWarning("GroqChatProvider: company {CompanyId} not found", request.CompanyId);
            return new AiReplyResult(
                "I'm sorry, I couldn't retrieve your company's settings.",
                0, DefaultChatModel);
        }

        var systemPrompt = _promptBuilder.Build(company, request.RetrievedKnowledgeChunks, request.AvailableActions);
        var messages     = BuildMessageArray(systemPrompt, request.RecentHistory, request.CustomerMessage);
        var chatModel    = _configuration["Groq:ChatModel"] ?? DefaultChatModel;

        var requestBody = new
        {
            model       = chatModel,
            messages,
            max_tokens  = 600,
            temperature = 0.3,
        };

        var http = CreateHttpClient();

        using var response = await http.PostAsJsonAsync(
            "chat/completions", requestBody, _json, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Groq chat API returned {Status}: {Body}", response.StatusCode, errorBody);
            throw new GroqException(
                $"Groq chat API error {(int)response.StatusCode}: {errorBody}");
        }

        var result = await response.Content
            .ReadFromJsonAsync<ChatCompletionResponse>(_json, cancellationToken)
            ?? throw new GroqException("Groq returned an empty chat completion response.");

        var rawReplyText = result.Choices[0].Message.Content ?? string.Empty;
        var tokensUsed    = result.Usage?.TotalTokens ?? 0;

        var (replyText, detectedIntent) = ExtractIntent(rawReplyText);

        _logger.LogInformation(
            "Groq reply generated | company={CompanyId} model={Model} tokens={Tokens} chunks={Chunks} intent={Intent}",
            request.CompanyId, chatModel, tokensUsed, request.RetrievedKnowledgeChunks.Count, detectedIntent?.Type);

        return new AiReplyResult(
            ReplyText:       replyText,
            ConfidenceScore: 0.95,
            ModelUsed:       chatModel,
            TokensUsed:      tokensUsed,
            Intent:          detectedIntent);
    }

    // -------------------------------------------------------------------------
    // Sprint 9 Action Engine — intent block parsing
    // -------------------------------------------------------------------------

    private static readonly System.Text.RegularExpressions.Regex IntentBlockPattern =
        new(@"<intent>\s*(\{.*?\})\s*</intent>", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Splits the model's raw response into the customer-facing text and the
    /// trailing &lt;intent&gt; JSON block SystemPromptBuilder instructs it to
    /// always append. Never throws — a missing or malformed block just means
    /// no DetectedIntent (caller treats that exactly like IntentType.Question),
    /// so a prompt-following hiccup degrades to "answered like normal", never
    /// to a broken reply.
    /// </summary>
    private (string CleanText, DetectedIntent? Intent) ExtractIntent(string rawText)
    {
        var match = IntentBlockPattern.Match(rawText);
        if (!match.Success)
            return (rawText.Trim(), null);

        var cleanText = rawText[..match.Index].TrimEnd();

        try
        {
            var parsed = JsonSerializer.Deserialize<IntentJson>(match.Groups[1].Value, _json);
            if (parsed?.Type is null)
                return (cleanText, null);

            var type = parsed.Type.Trim().ToLowerInvariant() switch
            {
                "action"   => IntentType.Action,
                "escalate" => IntentType.Escalate,
                _          => IntentType.Question,
            };

            var actionType = string.IsNullOrWhiteSpace(parsed.ActionType) || parsed.ActionType == "null"
                ? null
                : parsed.ActionType;

            return (cleanText, new DetectedIntent(type, actionType, parsed.Parameters, parsed.Confidence ?? 1.0));
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Could not parse <intent> block — treating as a plain question. Raw: {Raw}",
                match.Groups[1].Value);
            return (cleanText, null);
        }
    }

    private sealed class IntentJson
    {
        public string? Type { get; set; }
        public string? ActionType { get; set; }
        public Dictionary<string, string>? Parameters { get; set; }
        public double? Confidence { get; set; }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private HttpClient CreateHttpClient()
    {
        var apiKey = _configuration["Groq:ApiKey"]
            ?? throw new InvalidOperationException(
                "Groq:ApiKey is not configured. Set it in appsettings.Development.json " +
                "or as the GROQ__ApiKey environment variable.");

        var client = _httpFactory.CreateClient("groq");
        // Set per-call so the same named client works even if the key is rotated
        // between requests (e.g. in a multi-tenant deployment where keys are
        // fetched from a secret store per request).
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    /// <summary>
    /// Converts ConversationService's flat history lines into the
    /// role/content message array Groq (and OpenAI) expect.
    /// </summary>
    private static List<object> BuildMessageArray(
        string systemPrompt,
        IReadOnlyList<string> recentHistory,
        string currentMessage)
    {
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        foreach (var line in recentHistory)
        {
            if (line.StartsWith("Customer: ", StringComparison.Ordinal))
                messages.Add(new { role = "user",      content = line["Customer: ".Length..] });
            else if (line.StartsWith("AI: ", StringComparison.Ordinal))
                messages.Add(new { role = "assistant", content = line["AI: ".Length..] });
        }

        messages.Add(new { role = "user", content = currentMessage });
        return messages;
    }

    // -------------------------------------------------------------------------
    // Private JSON response models — identical shape to OpenAI completions
    // -------------------------------------------------------------------------

    private sealed class ChatCompletionResponse
    {
        public List<ChatChoice> Choices { get; set; } = [];
        public UsageInfo?       Usage   { get; set; }
    }

    private sealed class ChatChoice
    {
        public ChatMessage Message { get; set; } = new();
    }

    private sealed class ChatMessage
    {
        public string? Content { get; set; }
    }

    private sealed class UsageInfo
    {
        public int TotalTokens { get; set; }
    }
}

/// <summary>
/// Thrown when the Groq API returns a non-success response.
/// Caught by ChatHub.SendMessage and WhatsAppWebhookController.ProcessSingleMessageAsync.
/// </summary>
public sealed class GroqException : Exception
{
    public GroqException(string message) : base(message) { }
}
