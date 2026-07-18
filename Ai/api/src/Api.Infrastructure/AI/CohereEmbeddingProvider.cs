using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.AI;

/// <summary>
/// IEmbeddingProvider backed by Cohere embed-multilingual-v3.0.
///
/// Why Cohere for embeddings?
///   - 100+ language support natively — Swahili, English, French, Arabic, etc.
///     are all first-class, making this the right model for East African markets.
///   - 1024 dimensions (vs OpenAI's 1536) — smaller vectors, faster search,
///     smaller storage footprint, still excellent semantic quality.
///   - Asymmetric search: Cohere requires separate input_type values for
///     indexing ("search_document") and querying ("search_query"), which
///     improves retrieval precision. This is why EmbeddingInputType was added
///     to the IEmbeddingProvider interface.
///   - Free tier: 1,000 API calls/month on the Trial key — enough for
///     development and small deployments without a credit card.
///
/// API used: POST https://api.cohere.com/v2/embed
///   Model: embed-multilingual-v3.0
///   Request:  { "model": "...", "texts": ["..."], "input_type": "search_query"|"search_document",
///               "embedding_types": ["float"] }
///   Response: { "embeddings": { "float": [[...1024 floats...]] } }
///
/// Get a free API key at: https://dashboard.cohere.com/api-keys
/// </summary>
public sealed class CohereEmbeddingProvider : IEmbeddingProvider
{
    private const string DefaultModel  = "embed-multilingual-v3.0";
    private const string CohereBaseUrl = "https://api.cohere.com/";

    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration    _configuration;
    private readonly ILogger<CohereEmbeddingProvider> _logger;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public CohereEmbeddingProvider(
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        ILogger<CohereEmbeddingProvider> logger)
    {
        _httpFactory   = httpFactory;
        _configuration = configuration;
        _logger        = logger;
    }

    public async Task<float[]> EmbedAsync(
        string text,
        EmbeddingInputType inputType = EmbeddingInputType.Query,
        CancellationToken cancellationToken = default)
    {
        // Map our enum to Cohere's API string values.
        var cohereInputType = inputType switch
        {
            EmbeddingInputType.Document => "search_document",
            _                           => "search_query",
        };

        var model = _configuration["Cohere:EmbeddingModel"] ?? DefaultModel;

        // embedding_types: ["float"] requests 32-bit float vectors.
        // Cohere also supports int8 and binary, but pgvector expects floats.
        var requestBody = new
        {
            model,
            texts            = new[] { text },
            input_type       = cohereInputType,
            embedding_types  = new[] { "float" },
        };

        var http = CreateHttpClient();

        using var response = await http.PostAsJsonAsync(
            "v2/embed", requestBody, _json, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Cohere embed API returned {Status}: {Body}", response.StatusCode, errorBody);
            throw new CohereEmbeddingException(
                $"Cohere embed API error {(int)response.StatusCode}: {errorBody}");
        }

        var result = await response.Content
            .ReadFromJsonAsync<EmbedResponse>(_json, cancellationToken)
            ?? throw new CohereEmbeddingException("Cohere returned an empty embedding response.");

        var embedding = result.Embeddings?.Float?[0]
            ?? throw new CohereEmbeddingException(
                "Cohere embedding response did not contain a float embedding. " +
                "Ensure embedding_types includes 'float'.");

        _logger.LogDebug(
            "Cohere embedding generated | model={Model} inputType={InputType} dims={Dims}",
            model, cohereInputType, embedding.Length);

        return embedding;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private HttpClient CreateHttpClient()
    {
        var apiKey = _configuration["Cohere:ApiKey"]
            ?? throw new InvalidOperationException(
                "Cohere:ApiKey is not configured. " +
                "Get a free key at https://dashboard.cohere.com/api-keys and add it to " +
                "appsettings.Development.json or set the COHERE__ApiKey environment variable. " +
                "Without it, knowledge-base RAG retrieval will fail (non-fatal — chat still works).");

        var client = _httpFactory.CreateClient("cohere");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    // -------------------------------------------------------------------------
    // Private JSON response models — Cohere v2 /embed response shape
    // -------------------------------------------------------------------------

    private sealed class EmbedResponse
    {
        public EmbeddingsData? Embeddings { get; set; }
    }

    private sealed class EmbeddingsData
    {
        // "float" is the key in the embeddings object when embedding_types=["float"].
        // System.Text.Json's SnakeCaseLower policy maps this property name correctly.
        [JsonPropertyName("float")]
        public List<float[]>? Float { get; set; }
    }
}

/// <summary>
/// Thrown when the Cohere embeddings API returns a non-success response.
/// Caught non-fatally in RagService callers (ChatHub, WhatsAppWebhookController).
/// </summary>
public sealed class CohereEmbeddingException : Exception
{
    public CohereEmbeddingException(string message) : base(message) { }
}
