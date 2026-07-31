using System.Net;
using Api.Application.Abstractions;
using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.Actions;
using Api.Infrastructure.Persistence;
using Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests;

/// <summary>
/// Sprint 9 AI Action Engine tests. Covers: OtpVerification's pure IsActive
/// logic, the ActionLogs immutability guard (the literal "verified no
/// UPDATE/DELETE possible" checklist item), and a genuine end-to-end run of
/// ActionEngineService — intent -> registry lookup -> signed webhook call
/// (against a fake HttpMessageHandler, no network) -> audit log write ->
/// AI-composed confirmation (against a fake IAiProvider, no Groq key needed).
///
/// HMAC signature correctness itself is already covered by
/// MetaWebhookSignatureTests.cs (Sprint 8) — WebhookActionRunner reuses that
/// exact same MetaWebhookSignature utility rather than re-implementing HMAC,
/// so there's nothing new to verify about the crypto here.
/// </summary>
public class ActionEngineTests
{
    private class FakeTenantProvider : ICurrentTenantProvider
    {
        public Guid? CompanyId { get; set; }
        public Guid? AgentId { get; set; }
    }

    private static AppDbContext CreateContext(string dbName, ICurrentTenantProvider tenantProvider)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new AppDbContext(options, tenantProvider);
    }

    /// <summary>Returns a fixed HTTP response for every request, capturing the last request so tests can assert on the signature headers.</summary>
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(_statusCode) { Content = new StringContent(_responseBody) };
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public FakeHttpClientFactory(HttpMessageHandler handler) => _client = new HttpClient(handler);
        public HttpClient CreateClient(string name) => _client;
    }

    /// <summary>Always returns a canned reply — avoids any real Groq API dependency in tests.</summary>
    private sealed class FakeAiProvider : IAiProvider
    {
        public Task<AiReplyResult> GenerateReplyAsync(AiReplyRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AiReplyResult("Done — that's all sorted for you.", 0.95, "fake-model", TokensUsed: 12));
    }

    // -------------------------------------------------------------------
    // OtpVerification.IsActive
    // -------------------------------------------------------------------

    [Fact]
    public void OtpVerification_is_active_when_unused_unexpired_and_under_attempt_limit()
    {
        var otp = new OtpVerification { ExpiresAt = DateTime.UtcNow.AddMinutes(5), AttemptCount = 0 };
        Assert.True(otp.IsActive);
    }

    [Fact]
    public void OtpVerification_is_inactive_once_verified()
    {
        var otp = new OtpVerification { ExpiresAt = DateTime.UtcNow.AddMinutes(5), VerifiedAt = DateTime.UtcNow };
        Assert.False(otp.IsActive);
    }

    [Fact]
    public void OtpVerification_is_inactive_once_expired()
    {
        var otp = new OtpVerification { ExpiresAt = DateTime.UtcNow.AddMinutes(-1) };
        Assert.False(otp.IsActive);
    }

    [Fact]
    public void OtpVerification_is_inactive_after_three_failed_attempts()
    {
        var otp = new OtpVerification { ExpiresAt = DateTime.UtcNow.AddMinutes(5), AttemptCount = 3 };
        Assert.False(otp.IsActive);
    }

    // -------------------------------------------------------------------
    // ActionLogs immutability guard (AppDbContext.SaveChangesAsync)
    // -------------------------------------------------------------------

    [Fact]
    public async Task ActionLog_cannot_be_updated_after_insert()
    {
        const string dbName = nameof(ActionLog_cannot_be_updated_after_insert);
        var tenant = new FakeTenantProvider();
        var companyId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();

        Guid logId;
        using (var db = CreateContext(dbName, tenant))
        {
            var log = new ActionLog
            {
                CompanyId = companyId,
                ConversationId = conversationId,
                ActionType = "cancel_order",
                CustomerId = "+254700000000",
                ParametersEncrypted = "ciphertext",
                Success = true,
            };
            db.ActionLogs.Add(log);
            await db.SaveChangesAsync();
            logId = log.Id;
        }

        using (var db = CreateContext(dbName, tenant))
        {
            var log = await db.ActionLogs.FirstAsync(l => l.Id == logId);
            log.Success = false; // mutate a tracked entity - EntityState becomes Modified

            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task ActionLog_cannot_be_deleted()
    {
        const string dbName = nameof(ActionLog_cannot_be_deleted);
        var tenant = new FakeTenantProvider();

        Guid logId;
        using (var db = CreateContext(dbName, tenant))
        {
            var log = new ActionLog
            {
                CompanyId = Guid.NewGuid(),
                ConversationId = Guid.NewGuid(),
                ActionType = "cancel_order",
                CustomerId = "+254700000000",
                ParametersEncrypted = "ciphertext",
                Success = true,
            };
            db.ActionLogs.Add(log);
            await db.SaveChangesAsync();
            logId = log.Id;
        }

        using (var db = CreateContext(dbName, tenant))
        {
            var log = await db.ActionLogs.FirstAsync(l => l.Id == logId);
            db.ActionLogs.Remove(log);

            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }

    // -------------------------------------------------------------------
    // End-to-end: ActionEngineService.HandleDetectedActionAsync
    // -------------------------------------------------------------------

    [Fact]
    public async Task HandleDetectedActionAsync_executes_webhook_signs_it_and_writes_one_audit_log_on_success()
    {
        const string dbName = nameof(HandleDetectedActionAsync_executes_webhook_signs_it_and_writes_one_audit_log_on_success);
        var tenant = new FakeTenantProvider();
        var companyId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();

        var protector = new ActionEngineSecretProtector(TestDataProtection.CreateProvider());
        const string webhookSecret = "super-secret-webhook-key-0123456789";

        using var db = CreateContext(dbName, tenant);
        db.Companies.Add(new Company { Id = companyId, Name = "Acme", PublicApiKey = "pub_x", SecretApiKeyHash = "h" });

        var definition = new ActionDefinition
        {
            CompanyId = companyId,
            ActionType = "cancel_order",
            DisplayName = "Cancel an order",
            WebhookUrl = "https://client-backend.example.com/actions/cancel-order",
            WebhookSecretEncrypted = protector.Encrypt(webhookSecret),
            RequiresVerification = false,
            IsReadOnly = false,
            TimeoutSeconds = 10,
            IsActive = true,
        };
        db.ActionDefinitions.Add(definition);
        await db.SaveChangesAsync();

        var fakeHandler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"status":"cancelled","order_id":"12345"}""");
        var httpFactory = new FakeHttpClientFactory(fakeHandler);

        var runner = new WebhookActionRunner(httpFactory, protector, NullLogger<WebhookActionRunner>.Instance);
        var registry = new ActionRegistry(db);
        var smsSender = new NoOpSmsSender();
        var otpService = new OtpService(db, smsSender, NullLogger<OtpService>.Instance);
        var aiProvider = new FakeAiProvider();

        var engine = new ActionEngineService(
            db, registry, runner, otpService, aiProvider, protector, NullLogger<ActionEngineService>.Instance);

        var intent = new DetectedIntent(
            IntentType.Action, "cancel_order",
            new Dictionary<string, string> { ["order_id"] = "12345" }, 0.97);

        var outcome = await engine.HandleDetectedActionAsync(
            companyId, conversationId, "+254700000000", "please cancel order 12345", intent);

        // The webhook was actually called, signed correctly, and with the right body.
        Assert.NotNull(fakeHandler.LastRequest);
        Assert.True(fakeHandler.LastRequest!.Headers.Contains("X-Webhook-Signature"));
        var signatureHeader = fakeHandler.LastRequest.Headers.GetValues("X-Webhook-Signature").First();
        Assert.True(MetaWebhookSignature.Verify(fakeHandler.LastRequestBody!, signatureHeader, webhookSecret));
        Assert.Contains("cancel_order", fakeHandler.LastRequestBody);
        Assert.Contains("12345", fakeHandler.LastRequestBody);

        // Exactly one ActionLog row, marked successful.
        var logs = await db.ActionLogs.Where(l => l.CompanyId == companyId).ToListAsync();
        var log = Assert.Single(logs);
        Assert.True(log.Success);
        Assert.Equal(definition.Id, log.ActionDefinitionId);
        Assert.Equal(200, log.WebhookStatusCode);
        Assert.False(log.IdentityVerified); // RequiresVerification was false

        // The reply came from the AI compose step (our FakeAiProvider's canned text).
        Assert.Equal("Done — that's all sorted for you.", outcome.ReplyText);
    }

    [Fact]
    public async Task HandleDetectedActionAsync_logs_but_does_not_call_webhook_when_action_type_is_unregistered()
    {
        const string dbName = nameof(HandleDetectedActionAsync_logs_but_does_not_call_webhook_when_action_type_is_unregistered);
        var tenant = new FakeTenantProvider();
        var companyId = Guid.NewGuid();

        var protector = new ActionEngineSecretProtector(TestDataProtection.CreateProvider());
        using var db = CreateContext(dbName, tenant);

        var fakeHandler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        var httpFactory = new FakeHttpClientFactory(fakeHandler);

        var engine = new ActionEngineService(
            db, new ActionRegistry(db), new WebhookActionRunner(httpFactory, protector, NullLogger<WebhookActionRunner>.Instance),
            new OtpService(db, new NoOpSmsSender(), NullLogger<OtpService>.Instance), new FakeAiProvider(), protector,
            NullLogger<ActionEngineService>.Instance);

        var intent = new DetectedIntent(IntentType.Action, "delete_universe", new Dictionary<string, string>(), 0.9);

        var outcome = await engine.HandleDetectedActionAsync(companyId, Guid.NewGuid(), "+254700000000", "delete the universe", intent);

        Assert.Null(fakeHandler.LastRequest); // never called - nothing registered for this action_type
        var log = Assert.Single(db.ActionLogs.Local);
        Assert.False(log.Success);
        Assert.Null(log.ActionDefinitionId);
        Assert.Contains("not able to perform", outcome.ReplyText, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NoOpSmsSender : ISmsSender
    {
        public Task<SmsSendResult> SendAsync(string phoneNumber, string message, CancellationToken ct = default) =>
            Task.FromResult(new SmsSendResult(true));
    }
}

/// <summary>Shared in-memory Data Protection provider for tests that need to encrypt/decrypt without touching the filesystem key ring.</summary>
internal static class TestDataProtection
{
    public static Microsoft.AspNetCore.DataProtection.IDataProtectionProvider CreateProvider() =>
        Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("Api.Tests");
}
