using Api.Application.Abstractions;
using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.Persistence;
using Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests;

/// <summary>
/// ConversationService is the one place in this codebase that deliberately calls
/// IgnoreQueryFilters() — it has to, because ChatHub (web chat widget) and
/// WhatsAppWebhookController both run with no authenticated agent/JWT, so there is
/// no ICurrentTenantProvider context for the global query filter to key off.
///
/// Bypassing the filter is correct here ONLY because every query in
/// ConversationService also applies an explicit `.Where(c => c.CompanyId == companyId)`
/// predicate supplied directly by the caller (the route's {companyId} or the
/// widget's public-key lookup) — see the doc comment on ConversationService itself.
/// These tests prove that explicit predicate actually holds the line.
/// </summary>
public class ConversationServiceTests
{
    private class FakeTenantProvider : ICurrentTenantProvider
    {
        public Guid? CompanyId { get; set; }
        public Guid? AgentId { get; set; }
    }

    private static AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        // CompanyId is always null here on purpose — ConversationService never relies
        // on ICurrentTenantProvider, which mirrors the real ChatHub/webhook callers.
        return new AppDbContext(options, new FakeTenantProvider { CompanyId = null });
    }

    [Fact]
    public async Task GetOrCreateAsync_does_not_return_another_companys_conversation_for_the_same_customer_id()
    {
        const string dbName = nameof(GetOrCreateAsync_does_not_return_another_companys_conversation_for_the_same_customer_id);

        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        const string sharedCustomerId = "+254700000000"; // same phone number messaging two different companies' WhatsApp numbers

        await using var db = CreateContext(dbName);
        var service = new ConversationService(db, NullLogger<ConversationService>.Instance);

        var conversationForA = await service.GetOrCreateAsync(companyA, sharedCustomerId, ChannelType.WhatsApp);
        var conversationForB = await service.GetOrCreateAsync(companyB, sharedCustomerId, ChannelType.WhatsApp);

        Assert.NotEqual(conversationForA.Id, conversationForB.Id);
        Assert.Equal(companyA, conversationForA.CompanyId);
        Assert.Equal(companyB, conversationForB.CompanyId);
    }

    [Fact]
    public async Task GetOrCreateAsync_reuses_the_open_conversation_on_a_second_call()
    {
        const string dbName = nameof(GetOrCreateAsync_reuses_the_open_conversation_on_a_second_call);
        var companyId = Guid.NewGuid();

        await using var db = CreateContext(dbName);
        var service = new ConversationService(db, NullLogger<ConversationService>.Instance);

        var first = await service.GetOrCreateAsync(companyId, "session-123", ChannelType.WebChat);
        var second = await service.GetOrCreateAsync(companyId, "session-123", ChannelType.WebChat);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task AppendMessageAsync_stamps_the_provided_companyId_not_a_filtered_default()
    {
        const string dbName = nameof(AppendMessageAsync_stamps_the_provided_companyId_not_a_filtered_default);
        var companyId = Guid.NewGuid();

        await using var db = CreateContext(dbName);
        var service = new ConversationService(db, NullLogger<ConversationService>.Instance);

        var conversation = await service.GetOrCreateAsync(companyId, "session-abc", ChannelType.WebChat);
        var message = await service.AppendMessageAsync(conversation.Id, companyId, MessageRole.User, "Hello!");

        Assert.Equal(companyId, message.CompanyId);
        Assert.Equal(conversation.Id, message.ConversationId);
    }

    [Fact]
    public async Task GetRecentHistoryLinesAsync_only_returns_messages_from_the_requested_conversation()
    {
        const string dbName = nameof(GetRecentHistoryLinesAsync_only_returns_messages_from_the_requested_conversation);
        var companyId = Guid.NewGuid();

        await using var db = CreateContext(dbName);
        var service = new ConversationService(db, NullLogger<ConversationService>.Instance);

        var conversationA = await service.GetOrCreateAsync(companyId, "session-A", ChannelType.WebChat);
        var conversationB = await service.GetOrCreateAsync(companyId, "session-B", ChannelType.WebChat);

        await service.AppendMessageAsync(conversationA.Id, companyId, MessageRole.User, "Question from A");
        await service.AppendMessageAsync(conversationA.Id, companyId, MessageRole.Ai, "Answer for A");
        await service.AppendMessageAsync(conversationB.Id, companyId, MessageRole.User, "Question from B");

        var historyA = await service.GetRecentHistoryLinesAsync(conversationA.Id);

        Assert.Equal(2, historyA.Count);
        Assert.Contains(historyA, line => line.Contains("Question from A"));
        Assert.DoesNotContain(historyA, line => line.Contains("Question from B"));
    }
}
