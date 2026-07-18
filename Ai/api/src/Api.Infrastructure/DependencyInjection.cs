using Api.Application.Abstractions;
using Api.Infrastructure.AI;
using Api.Infrastructure.Billing;
using Api.Infrastructure.Channels;
using Api.Infrastructure.Identity;
using Api.Infrastructure.Persistence;
using Api.Infrastructure.Security;
using Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pgvector.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
namespace Api.Infrastructure;
using Polly;
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
        services.AddHttpClient<IMessengerClient, MessengerClient>();
        services.AddHttpClient<ITelegramClient, TelegramClient>();
        services.AddHttpClient<IMpesaClient, MpesaClient>();

        // ---- Sprint 3 -------------------------------------------------------
        services.AddScoped<ConversationService>();

        // ---- Sprint 4: AI ---------------------------------------------------
        services.AddHttpClient("groq", client =>
{
    client.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
    client.Timeout     = TimeSpan.FromSeconds(60);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime    = TimeSpan.FromMinutes(2),
    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
})
.AddStandardResilienceHandler(options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
    options.Retry.Delay = TimeSpan.FromMilliseconds(200);
});

        services.AddHttpClient("cohere", client =>
        {
            client.BaseAddress = new Uri("https://api.cohere.com/");
            client.Timeout     = TimeSpan.FromSeconds(30);
        })
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime    = TimeSpan.FromMinutes(2),
    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
})
.AddStandardResilienceHandler(options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
    options.Retry.Delay = TimeSpan.FromMilliseconds(200);
});

        services.AddSingleton<SystemPromptBuilder>();
        services.AddScoped<IAiProvider, GroqChatProvider>();
        services.AddScoped<IEmbeddingProvider, CohereEmbeddingProvider>();
        services.AddScoped<RagService>();

        // ---- Sprint 5: Tickets + Email --------------------------------------

        // Brevo HTTP client for transactional email (send + inbound notifications).
       services.AddHttpClient("brevo", client =>
{
    client.BaseAddress = new Uri("https://api.brevo.com/");
    client.Timeout     = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime    = TimeSpan.FromMinutes(2),
    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
})
.AddStandardResilienceHandler();

        // Register BrevoEmailClient using the named "brevo" HttpClient.
        services.AddScoped<IBrevoEmailClient>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http    = factory.CreateClient("brevo");
            var cfg     = sp.GetRequiredService<IConfiguration>();
            var logger  = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BrevoEmailClient>>();
            return new BrevoEmailClient(http, cfg, logger);
        });

        // Auth hardening: verification/reset/welcome/security-alert emails —
        // built on top of the same IBrevoEmailClient registered just above.
        services.AddScoped<Api.Infrastructure.Identity.IAuthEmailService, Api.Infrastructure.Identity.AuthEmailService>();

        services.AddScoped<EscalationService>();
        services.AddScoped<TicketService>();
        services.AddScoped<ChatChannelPipelineService>();
        services.AddScoped<TokenBudgetService>();

        // ---- Sprint 5 follow-up: IMAP/SMTP, free alternative to Brevo's paid
        // inbound webhook (see ImapChannelCredentials' doc comment) ------------
        services.AddSingleton<IImapSmtpEmailClient, ImapSmtpEmailClient>();
        services.AddSingleton<ImapMailFetcher>();
        services.AddScoped<EmailPipelineService>();
        services.AddHostedService<Api.Infrastructure.BackgroundServices.ImapPollingService>();
        services.AddHostedService<Api.Infrastructure.BackgroundServices.BillingPeriodResetService>();

        // ---- Sprint 4 (Web Crawling update) ----------------------------------
        // Shared config for every outbound request the crawler makes: a real UA
        // string (politeness — lets site owners identify and, if needed, block
        // us via robots.txt) and a bounded timeout so one slow/dead server can't
        // stall a whole crawl.
        static void ConfigureCrawlerClient(HttpClient client)
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "AiSupportPlatformBot/1.0 (+https://aisupportplatform.com/bot)");
        }

        services.AddHttpClient(Api.Infrastructure.Crawling.WebCrawlHttpClients.CrawlerClientName, ConfigureCrawlerClient)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime    = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
                AllowAutoRedirect           = true,
                MaxAutomaticRedirections    = 5,
            });

        services.AddHttpClient<Api.Infrastructure.Crawling.IRobotsTxtChecker, Api.Infrastructure.Crawling.RobotsTxtChecker>(
            ConfigureCrawlerClient);
        services.AddHttpClient<Api.Infrastructure.Crawling.ISitemapService, Api.Infrastructure.Crawling.SitemapService>(
            ConfigureCrawlerClient);
        services.AddHttpClient<Api.Infrastructure.Crawling.IWebPageChangeDetector, Api.Infrastructure.Crawling.WebPageChangeDetector>(
            ConfigureCrawlerClient);

        services.AddScoped<Api.Infrastructure.Crawling.IHtmlContentExtractor, Api.Infrastructure.Crawling.HtmlContentExtractor>();
        services.AddScoped<Api.Infrastructure.Crawling.IAdaptiveCheckScheduler, Api.Infrastructure.Crawling.AdaptiveCheckScheduler>();
        services.AddScoped<Api.Infrastructure.Crawling.IWebCrawlerService, Api.Infrastructure.Crawling.WebCrawlerService>();
        services.AddScoped<KnowledgeIngestionService>();

        // Channel-backed job queue (this codebase's Hangfire-free equivalent of a
        // background job dispatcher) — one singleton BackgroundService instance
        // doubles as both IWebCrawlQueue (the enqueue API controllers call) and
        // the hosted consumer loop that actually runs crawls.
        services.AddSingleton<Api.Infrastructure.BackgroundServices.WebCrawlQueueService>();
        services.AddSingleton<Api.Infrastructure.BackgroundServices.IWebCrawlQueue>(
            sp => sp.GetRequiredService<Api.Infrastructure.BackgroundServices.WebCrawlQueueService>());
        services.AddHostedService(sp => sp.GetRequiredService<Api.Infrastructure.BackgroundServices.WebCrawlQueueService>());

        services.AddHostedService<Api.Infrastructure.BackgroundServices.ContentFreshnessService>();

        // ---- Startup warnings -----------------------------------------------
        WarnIfMissing(configuration, "Groq:ApiKey",
            "AI chat replies will fail. Get a free key at https://console.groq.com");

        WarnIfMissing(configuration, "Cohere:ApiKey",
            "RAG retrieval skipped (non-fatal). Get a free key at https://dashboard.cohere.com/api-keys");

        WarnIfMissing(configuration, "Brevo:ApiKey",
            "Email sending and ticket notifications will fail. Get a free key at https://app.brevo.com/settings/keys/api");

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
