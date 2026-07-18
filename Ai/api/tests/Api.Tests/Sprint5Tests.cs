using Api.Application.Abstractions;
using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.Channels;
using Api.Infrastructure.Persistence;
using Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests;

/// <summary>
/// Unit tests for Sprint 5: EscalationService's rule engine, and TicketService's
/// sequential per-company ticket numbering + conversation-escalation side effect.
///
/// TicketService.CreateAsync wraps ticket-number generation in a Serializable EF
/// Core transaction (see TicketService's doc comment). The InMemory provider used
/// here accepts BeginTransactionAsync as a no-op rather than throwing — if a local
/// `dotnet test` run disagrees (provider version drift), that call is the first
/// place to look.
///
/// Email delivery (BrevoEmailClient) and WhatsApp/agent-reply delivery are not
/// exercised here — those make real HTTP calls and are out of scope for unit tests,
/// same rationale as Sprint4Tests' treatment of GroqChatProvider/CohereEmbeddingProvider.
/// </summary>
public class Sprint5Tests
{
    private class FakeTenantProvider : ICurrentTenantProvider
    {
        public Guid? CompanyId { get; set; }
        public Guid? AgentId { get; set; }
    }

    /// <summary>Records every email TicketService would have sent, without any network call.</summary>
    private class NoOpBrevoEmailClient : IBrevoEmailClient
    {
        public List<BrevoOutboundEmail> Sent { get; } = [];

        public Task SendAsync(BrevoOutboundEmail email, CancellationToken ct = default)
        {
            Sent.Add(email);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Hand-rolled IServiceScopeFactory (rather than a full ServiceCollection
    /// container) so this test has no dependency on which DI package versions are
    /// resolvable in the sandbox — only Microsoft.Extensions.DependencyInjection.Abstractions'
    /// interfaces are used, which TicketService itself already depends on directly.
    /// Each CreateScope() call opens a fresh AppDbContext against the same named
    /// InMemory database, mirroring TicketService.SendNotificationInNewScopeAsync's
    /// real production behaviour of using its own DbContext per background task.
    /// </summary>
    private class TestServiceScopeFactory(string dbName, IBrevoEmailClient email) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(CreateContext(dbName), email);

        private class Scope(AppDbContext db, IBrevoEmailClient email) : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } = new Provider(db, email);
            public void Dispose() => db.Dispose();
        }

        private class Provider(AppDbContext db, IBrevoEmailClient email) : IServiceProvider
        {
            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(AppDbContext)) return db;
                if (serviceType == typeof(IBrevoEmailClient)) return email;
                return null;
            }
        }
    }

    private static AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new AppDbContext(options, new FakeTenantProvider { CompanyId = null });
    }

    private static async Task<Guid> SeedCompanyAsync(AppDbContext db, string? escalationRulesJson = null)
    {
        var company = new Company
        {
            Name              = "Acme Ltd",
            PublicApiKey      = $"pub_{Guid.NewGuid():N}",
            SecretApiKeyHash  = "hash",
            EscalationRulesJson = escalationRulesJson,
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company.Id;
    }

    private static async Task<Guid> SeedConversationAsync(AppDbContext db, Guid companyId, ChannelType channel = ChannelType.WebChat)
    {
        var conversation = new Conversation
        {
            CompanyId  = companyId,
            Channel    = channel,
            CustomerId = $"customer-{Guid.NewGuid():N}",
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation.Id;
    }

    // -------------------------------------------------------------------------
    // EscalationService
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EvaluateAsync_escalates_on_explicit_agent_request_regardless_of_confidence()
    {
        await using var db = CreateContext(nameof(EvaluateAsync_escalates_on_explicit_agent_request_regardless_of_confidence));
        var companyId = await SeedCompanyAsync(db);
        var service   = new EscalationService(db, NullLogger<EscalationService>.Instance);

        var decision = await service.EvaluateAsync(
            companyId, Guid.NewGuid(), "Can I talk to a real person please", aiConfidenceScore: 0.95);

        Assert.True(decision.ShouldEscalate);
        Assert.Equal(TicketPriority.High, decision.Priority);
        Assert.Equal("Customer requested human agent", decision.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_escalates_on_low_confidence_by_default()
    {
        await using var db = CreateContext(nameof(EvaluateAsync_escalates_on_low_confidence_by_default));
        var companyId = await SeedCompanyAsync(db);
        var service   = new EscalationService(db, NullLogger<EscalationService>.Instance);

        var decision = await service.EvaluateAsync(
            companyId, Guid.NewGuid(), "What are your hours?", aiConfidenceScore: 0.2);

        Assert.True(decision.ShouldEscalate);
        Assert.Equal(TicketPriority.Medium, decision.Priority);
        Assert.Contains("Low AI confidence", decision.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_does_not_escalate_on_a_confident_ordinary_reply()
    {
        await using var db = CreateContext(nameof(EvaluateAsync_does_not_escalate_on_a_confident_ordinary_reply));
        var companyId = await SeedCompanyAsync(db);
        var service   = new EscalationService(db, NullLogger<EscalationService>.Instance);

        var decision = await service.EvaluateAsync(
            companyId, Guid.NewGuid(), "What time do you open?", aiConfidenceScore: 0.9);

        Assert.False(decision.ShouldEscalate);
    }

    [Fact]
    public async Task EvaluateAsync_payment_keyword_rule_is_off_by_default()
    {
        await using var db = CreateContext(nameof(EvaluateAsync_payment_keyword_rule_is_off_by_default));
        var companyId = await SeedCompanyAsync(db);
        var service   = new EscalationService(db, NullLogger<EscalationService>.Instance);

        var decision = await service.EvaluateAsync(
            companyId, Guid.NewGuid(), "I want a refund on my order", aiConfidenceScore: 0.9);

        Assert.False(decision.ShouldEscalate);
    }

    [Fact]
    public async Task EvaluateAsync_payment_keyword_rule_escalates_when_enabled_via_company_json()
    {
        const string json = "{\"escalateOnPaymentKeywords\": true}";

        await using var db = CreateContext(nameof(EvaluateAsync_payment_keyword_rule_escalates_when_enabled_via_company_json));
        var companyId = await SeedCompanyAsync(db, json);
        var service   = new EscalationService(db, NullLogger<EscalationService>.Instance);

        var decision = await service.EvaluateAsync(
            companyId, Guid.NewGuid(), "I was double charged on M-Pesa", aiConfidenceScore: 0.9);

        Assert.True(decision.ShouldEscalate);
        Assert.Equal(TicketPriority.High, decision.Priority);
        Assert.Contains("Payment", decision.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_respects_a_stricter_confidence_threshold_from_company_json()
    {
        // Default threshold is 0.60 — a reply at 0.8 confidence would pass under the
        // default, but a company that raises the bar to 0.9 should still escalate it.
        const string json = "{\"confidenceThreshold\": 0.9}";

        await using var db = CreateContext(nameof(EvaluateAsync_respects_a_stricter_confidence_threshold_from_company_json));
        var companyId = await SeedCompanyAsync(db, json);
        var service   = new EscalationService(db, NullLogger<EscalationService>.Instance);

        var decision = await service.EvaluateAsync(
            companyId, Guid.NewGuid(), "What time do you open?", aiConfidenceScore: 0.8);

        Assert.True(decision.ShouldEscalate);
    }

    [Fact]
    public async Task EvaluateAsync_malformed_escalation_rules_json_falls_back_to_defaults_instead_of_throwing()
    {
        await using var db = CreateContext(nameof(EvaluateAsync_malformed_escalation_rules_json_falls_back_to_defaults_instead_of_throwing));
        var companyId = await SeedCompanyAsync(db, "{ this is not valid json");
        var service   = new EscalationService(db, NullLogger<EscalationService>.Instance);

        var decision = await service.EvaluateAsync(
            companyId, Guid.NewGuid(), "What time do you open?", aiConfidenceScore: 0.9);

        // Defaults: no payment rule, confidence 0.9 clears the default 0.6 threshold,
        // no agent-request keyword present — so no escalation, and (critically) no throw.
        Assert.False(decision.ShouldEscalate);
    }

    [Fact]
    public async Task EvaluateAsync_null_confidence_score_does_not_trigger_low_confidence_rule()
    {
        // Channels that haven't computed a confidence score yet (or a provider that
        // doesn't return one) must not be treated as "confidently wrong".
        await using var db = CreateContext(nameof(EvaluateAsync_null_confidence_score_does_not_trigger_low_confidence_rule));
        var companyId = await SeedCompanyAsync(db);
        var service   = new EscalationService(db, NullLogger<EscalationService>.Instance);

        var decision = await service.EvaluateAsync(
            companyId, Guid.NewGuid(), "What time do you open?", aiConfidenceScore: null);

        Assert.False(decision.ShouldEscalate);
    }

    // -------------------------------------------------------------------------
    // TicketService
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_assigns_sequential_per_company_ticket_numbers()
    {
        const string dbName = nameof(CreateAsync_assigns_sequential_per_company_ticket_numbers);

        await using var seedDb = CreateContext(dbName);
        var companyId      = await SeedCompanyAsync(seedDb);
        var conversationId = await SeedConversationAsync(seedDb, companyId);

        var email        = new NoOpBrevoEmailClient();
        var scopeFactory = new TestServiceScopeFactory(dbName, email);

        await using var db1 = CreateContext(dbName);
        var service1 = new TicketService(db1, email, scopeFactory, NullLogger<TicketService>.Instance);
        var first = await service1.CreateAsync(companyId, conversationId, "First issue");

        await using var db2 = CreateContext(dbName);
        var service2 = new TicketService(db2, email, scopeFactory, NullLogger<TicketService>.Instance);
        var second = await service2.CreateAsync(companyId, conversationId, "Second issue");

        Assert.Equal(1, first.TicketNumber);
        Assert.Equal(2, second.TicketNumber);
    }

    [Fact]
    public async Task CreateAsync_keeps_ticket_numbering_independent_per_company()
    {
        const string dbName = nameof(CreateAsync_keeps_ticket_numbering_independent_per_company);

        await using var seedDb = CreateContext(dbName);
        var companyA = await SeedCompanyAsync(seedDb);
        var companyB = await SeedCompanyAsync(seedDb);
        var convA    = await SeedConversationAsync(seedDb, companyA);
        var convB    = await SeedConversationAsync(seedDb, companyB);

        var email        = new NoOpBrevoEmailClient();
        var scopeFactory = new TestServiceScopeFactory(dbName, email);

        await using var db1 = CreateContext(dbName);
        var ticketA = await new TicketService(db1, email, scopeFactory, NullLogger<TicketService>.Instance)
            .CreateAsync(companyA, convA, "Company A's first ticket");

        await using var db2 = CreateContext(dbName);
        var ticketB = await new TicketService(db2, email, scopeFactory, NullLogger<TicketService>.Instance)
            .CreateAsync(companyB, convB, "Company B's first ticket");

        // Both are ticket #1 within their own company — numbering never collides
        // globally and never leaks sequence information across tenants.
        Assert.Equal(1, ticketA.TicketNumber);
        Assert.Equal(1, ticketB.TicketNumber);
    }

    [Fact]
    public async Task CreateAsync_marks_the_linked_conversation_as_escalated()
    {
        const string dbName = nameof(CreateAsync_marks_the_linked_conversation_as_escalated);

        await using var seedDb = CreateContext(dbName);
        var companyId      = await SeedCompanyAsync(seedDb);
        var conversationId = await SeedConversationAsync(seedDb, companyId, ChannelType.WhatsApp);

        var email        = new NoOpBrevoEmailClient();
        var scopeFactory = new TestServiceScopeFactory(dbName, email);

        await using var db = CreateContext(dbName);
        var service = new TicketService(db, email, scopeFactory, NullLogger<TicketService>.Instance);
        await service.CreateAsync(companyId, conversationId, "Needs a human", TicketPriority.High);

        await using var verifyDb = CreateContext(dbName);
        var reloaded = await verifyDb.Conversations
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == conversationId);

        Assert.Equal(ConversationStatus.Escalated, reloaded.Status);
    }

    [Fact]
    public async Task CreateAsync_truncates_subjects_longer_than_300_characters()
    {
        const string dbName = nameof(CreateAsync_truncates_subjects_longer_than_300_characters);

        await using var seedDb = CreateContext(dbName);
        var companyId      = await SeedCompanyAsync(seedDb);
        var conversationId = await SeedConversationAsync(seedDb, companyId);

        var email        = new NoOpBrevoEmailClient();
        var scopeFactory = new TestServiceScopeFactory(dbName, email);
        var longSubject  = new string('x', 500);

        await using var db = CreateContext(dbName);
        var service = new TicketService(db, email, scopeFactory, NullLogger<TicketService>.Instance);
        var ticket = await service.CreateAsync(companyId, conversationId, longSubject);

        Assert.Equal(300, ticket.Subject.Length);
    }

    // -------------------------------------------------------------------------
    // ConversationService — widget continuity fixes (follow-up pass)
    //
    // These cover the bug where escalating a conversation (Status → Escalated)
    // caused the *next* customer message to silently fork into a brand-new,
    // ticket-less conversation, because GetOrCreateAsync only ever reused
    // Status == Open conversations. That forking is also what broke "reply from
    // the dashboard doesn't show up in the widget" — the agent was replying to
    // conversation A, but the widget had already moved on to conversation B.
    // -------------------------------------------------------------------------

  [Fact]
public async Task GetOrCreateAsync_reuses_an_escalated_conversation_instead_of_forking_a_new_one()
{
    await using var db = CreateContext(nameof(GetOrCreateAsync_reuses_an_escalated_conversation_instead_of_forking_a_new_one));
    var companyId = await SeedCompanyAsync(db);
    var service   = new ConversationService(db, NullLogger<ConversationService>.Instance);

    var first = await service.GetOrCreateAsync(companyId, "session-1", ChannelType.WebChat);

    // Before — InMemory doesn't support ExecuteUpdateAsync:
    // await db.Conversations
    //     .IgnoreQueryFilters()
    //     .Where(c => c.Id == first.Id)
    //     .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ConversationStatus.Escalated));

    // After — load + save works on all providers:
    var convToEscalate = await db.Conversations
        .IgnoreQueryFilters()
        .FirstAsync(c => c.Id == first.Id);
    convToEscalate.Status = ConversationStatus.Escalated;
    await db.SaveChangesAsync();

    var second = await service.GetOrCreateAsync(companyId, "session-1", ChannelType.WebChat);

    Assert.Equal(first.Id, second.Id);
}

    [Fact]
    public async Task GetOrCreateAsync_starts_a_fresh_conversation_once_the_previous_one_is_resolved()
    {
        await using var db = CreateContext(nameof(GetOrCreateAsync_starts_a_fresh_conversation_once_the_previous_one_is_resolved));
        var companyId = await SeedCompanyAsync(db);
        var service   = new ConversationService(db, NullLogger<ConversationService>.Instance);

        var first = await service.GetOrCreateAsync(companyId, "session-1", ChannelType.WebChat);
        await service.ResolveAsync(first.Id);

        var second = await service.GetOrCreateAsync(companyId, "session-1", ChannelType.WebChat);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task GetMostRecentAsync_finds_a_conversation_regardless_of_status()
    {
        await using var db = CreateContext(nameof(GetMostRecentAsync_finds_a_conversation_regardless_of_status));
        var companyId = await SeedCompanyAsync(db);
        var service   = new ConversationService(db, NullLogger<ConversationService>.Instance);

        var conversation = await service.GetOrCreateAsync(companyId, "session-1", ChannelType.WebChat);
        await service.ResolveAsync(conversation.Id);

        var found = await service.GetMostRecentAsync(companyId, "session-1", ChannelType.WebChat);

        Assert.NotNull(found);
        Assert.Equal(conversation.Id, found!.Id);
    }

    [Fact]
    public async Task GetMostRecentAsync_returns_null_for_a_customer_who_has_never_messaged_before()
    {
        await using var db = CreateContext(nameof(GetMostRecentAsync_returns_null_for_a_customer_who_has_never_messaged_before));
        var companyId = await SeedCompanyAsync(db);
        var service   = new ConversationService(db, NullLogger<ConversationService>.Instance);

        var found = await service.GetMostRecentAsync(companyId, "never-seen-before", ChannelType.WebChat);

        Assert.Null(found);
    }

    [Fact]
    public async Task GetCustomerFacingMessagesAsync_excludes_internal_system_notes()
    {
        await using var db = CreateContext(nameof(GetCustomerFacingMessagesAsync_excludes_internal_system_notes));
        var companyId = await SeedCompanyAsync(db);
        var service   = new ConversationService(db, NullLogger<ConversationService>.Instance);

        var conversation = await service.GetOrCreateAsync(companyId, "session-1", ChannelType.WebChat);
        await service.AppendMessageAsync(conversation.Id, companyId, MessageRole.User, "Where's my order?");
        await service.AppendMessageAsync(conversation.Id, companyId, MessageRole.Ai, "Let me check that.");
        await service.AppendMessageAsync(conversation.Id, companyId, MessageRole.System, "[Escalated] AI draft: ...");
        await service.AppendMessageAsync(conversation.Id, companyId, MessageRole.Agent, "I'm on it!");

        var messages = await service.GetCustomerFacingMessagesAsync(conversation.Id);

        Assert.Equal(3, messages.Count);
        Assert.DoesNotContain(messages, m => m.Role == MessageRole.System);
    }

    // -------------------------------------------------------------------------
    // EmailChannelMetadata — Webhook vs Imap mode routing (follow-up pass)
    //
    // This logic decides whether a customer's reply goes out via Brevo or via
    // SMTP, so a routing mistake here silently sends mail from the wrong path
    // (or not at all) rather than throwing — worth pinning down with tests.
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadMode_returns_Imap_when_set()
    {
        var json = """{"inboundMode":"Imap","senderEmail":"a@b.com","senderName":"A"}""";
        Assert.Equal(EmailChannelMetadata.ModeImap, EmailChannelMetadata.ReadMode(json));
    }

    [Fact]
    public void ReadMode_returns_Webhook_when_set()
    {
        var json = """{"inboundMode":"Webhook","senderEmail":"a@b.com","senderName":"A"}""";
        Assert.Equal(EmailChannelMetadata.ModeWebhook, EmailChannelMetadata.ReadMode(json));
    }

    [Fact]
    public void ReadMode_defaults_to_Webhook_for_legacy_connections_missing_the_field()
    {
        // Connections created before this mode field existed only ever had
        // { displayEmail } — must default to Webhook (their actual historical
        // behavior) rather than throwing or silently defaulting to Imap (which
        // would try to decrypt IMAP credentials that were never stored).
        var legacyJson = """{"displayEmail":"a@b.com"}""";
        Assert.Equal(EmailChannelMetadata.ModeWebhook, EmailChannelMetadata.ReadMode(legacyJson));
    }

    [Fact]
    public void ReadMode_falls_back_to_Webhook_on_malformed_json_instead_of_throwing()
    {
        Assert.Equal(EmailChannelMetadata.ModeWebhook, EmailChannelMetadata.ReadMode("{ not valid json"));
    }

    [Fact]
    public void ReadSenderEmail_and_ReadSenderName_round_trip()
    {
        var json = """{"inboundMode":"Imap","senderEmail":"support@acme.com","senderName":"Acme Support"}""";
        Assert.Equal("support@acme.com", EmailChannelMetadata.ReadSenderEmail(json));
        Assert.Equal("Acme Support", EmailChannelMetadata.ReadSenderName(json));
    }

    [Fact]
    public void ReadSenderEmail_returns_null_for_legacy_metadata_missing_the_field()
    {
        var legacyJson = """{"displayEmail":"a@b.com"}""";
        Assert.Null(EmailChannelMetadata.ReadSenderEmail(legacyJson));
        Assert.Null(EmailChannelMetadata.ReadSenderName(legacyJson));
    }

    // -------------------------------------------------------------------------
    // EmailChannelMetadata — processing-start cutoff (backlog-flood fix)
    //
    // Covers the bug where IMAP mode's SearchQuery.NotSeen alone pulled in a
    // mailbox's entire pre-existing unread backlog (old newsletters,
    // notifications, mail from months/years ago that was simply never marked
    // read) as brand-new conversations the moment a company connected.
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadProcessingStartedAtUtc_round_trips_through_WithProcessingStartedAtUtc()
    {
        var original = """{"inboundMode":"Imap","senderEmail":"a@b.com","senderName":"A"}""";
        var expected = new DateTime(2026, 7, 4, 12, 0, 0, DateTimeKind.Utc);

        var updated = EmailChannelMetadata.WithProcessingStartedAtUtc(original, expected);
        var readBack = EmailChannelMetadata.ReadProcessingStartedAtUtc(updated);

        Assert.Equal(expected, readBack);
    }

    [Fact]
    public void WithProcessingStartedAtUtc_preserves_every_other_existing_field()
    {
        var original = """{"inboundMode":"Imap","displayEmail":"a@b.com","senderEmail":"a@b.com","senderName":"Acme"}""";

        var updated = EmailChannelMetadata.WithProcessingStartedAtUtc(original, DateTime.UtcNow);

        Assert.Equal(EmailChannelMetadata.ModeImap, EmailChannelMetadata.ReadMode(updated));
        Assert.Equal("a@b.com", EmailChannelMetadata.ReadSenderEmail(updated));
        Assert.Equal("Acme", EmailChannelMetadata.ReadSenderName(updated));
    }

    [Fact]
    public void ReadProcessingStartedAtUtc_returns_null_when_absent()
    {
        var legacyJson = """{"inboundMode":"Imap","senderEmail":"a@b.com","senderName":"A"}""";
        Assert.Null(EmailChannelMetadata.ReadProcessingStartedAtUtc(legacyJson));
    }
}
