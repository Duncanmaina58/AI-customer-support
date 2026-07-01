using Api.Application.Abstractions;
using Api.Infrastructure.AI;
using Api.Infrastructure.Channels;
using Api.Infrastructure.Identity;
using Api.Infrastructure.Persistence;
using Api.Infrastructure.Security;
using Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pgvector.EntityFrameworkCore;

namespace Api.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentTenantProvider, HttpCurrentTenantProvider>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        services.AddDataProtection();
        services.AddSingleton<IChannelCredentialProtector, ChannelCredentialProtector>();

        services.AddHttpClient<IWhatsAppClient, WhatsAppClient>();

        // ---- Sprint 3 -------------------------------------------------------
        services.AddScoped<ConversationService>();

        // ---- Sprint 4: AI providers -----------------------------------------

        // Groq — chat completions only (LPU inference, very fast).
        // Bearer token set per-call inside GroqChatProvider.CreateHttpClient().
        services.AddHttpClient("groq", client =>
        {
            client.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
            client.Timeout     = TimeSpan.FromSeconds(60);
        });

        // Cohere — embeddings only (embed-multilingual-v3.0, 1024-dim).
        // Groq does not provide an embeddings API; Cohere is the sole embeddings
        // dependency. Supports 100+ languages natively including Swahili.
        services.AddHttpClient("cohere", client =>
        {
            client.BaseAddress = new Uri("https://api.cohere.com/");
            client.Timeout     = TimeSpan.FromSeconds(30);
        });

        // SystemPromptBuilder is stateless — safe as singleton.
        services.AddSingleton<SystemPromptBuilder>();

        // IAiProvider        → GroqChatProvider         (Llama 3.3 70B via Groq LPU)
        // IEmbeddingProvider → CohereEmbeddingProvider  (embed-multilingual-v3.0, 1024-dim)
        services.AddScoped<IAiProvider, GroqChatProvider>();
        services.AddScoped<IEmbeddingProvider, CohereEmbeddingProvider>();

        // RagService uses IEmbeddingProvider + AppDbContext.
        services.AddScoped<RagService>();

        // Startup warnings — emitted before host.Run() so devs see them immediately.
        WarnIfMissing(configuration, "Groq:ApiKey",
            "AI chat replies will fail at runtime. " +
            "Get a free key at https://console.groq.com and add it to appsettings.Development.json " +
            "or set the GROQ__ApiKey environment variable.");

        WarnIfMissing(configuration, "Cohere:ApiKey",
            "Knowledge-base RAG retrieval will be skipped (non-fatal — chat via Groq still works). " +
            "Get a free key at https://dashboard.cohere.com/api-keys and add Cohere:ApiKey " +
            "to appsettings.Development.json to enable semantic search.");

        return services;
    }

    private static void WarnIfMissing(IConfiguration cfg, string key, string advice)
    {
        if (string.IsNullOrWhiteSpace(cfg[key]))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[WARN] {key} is not configured. {advice}");
            Console.ResetColor();
        }
    }
}
