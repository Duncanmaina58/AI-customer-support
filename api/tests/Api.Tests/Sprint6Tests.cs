using Api.Application.Abstractions;
using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.Persistence;
using Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests;

/// <summary>
/// Sprint 6: sandbox mode. Covers ConversationService.GetOrCreateAsync's
/// isSandbox parameter — the one piece of Sprint 6 that's cheap to test without
/// mocking libraries (ChatChannelPipelineService and the Messenger/Telegram
/// clients all need real HTTP or a mocking framework this project doesn't have;
/// same rationale Sprint4Tests used for not testing GroqChatProvider directly).
/// </summary>
public class Sprint6Tests
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

        return new AppDbContext(options, new FakeTenantProvider { CompanyId = null });
    }

    private static async Task<Guid> SeedCompanyAsync(AppDbContext db)
    {
        var company = new Company
        {
            Name             = "Acme Ltd",
            PublicApiKey     = $"pub_{Guid.NewGuid():N}",
            SecretApiKeyHash = "hash",
            SandboxToken     = $"sbx_{Guid.NewGuid():N}",
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company.Id;
    }

    [Fact]
    public async Task GetOrCreateAsync_marks_a_new_conversation_as_sandbox_when_requested()
    {
        await using var db = CreateContext(nameof(GetOrCreateAsync_marks_a_new_conversation_as_sandbox_when_requested));
        var companyId = await SeedCompanyAsync(db);
        var service = new ConversationService(db, NullLogger<ConversationService>.Instance);

        var conversation = await service.GetOrCreateAsync(companyId, "session-1", ChannelType.WebChat, isSandbox: true);

        Assert.True(conversation.IsSandbox);
    }

    [Fact]
    public async Task GetOrCreateAsync_defaults_to_non_sandbox()
    {
        await using var db = CreateContext(nameof(GetOrCreateAsync_defaults_to_non_sandbox));
        var companyId = await SeedCompanyAsync(db);
        var service = new ConversationService(db, NullLogger<ConversationService>.Instance);

        var conversation = await service.GetOrCreateAsync(companyId, "session-1", ChannelType.WebChat);

        Assert.False(conversation.IsSandbox);
    }

    [Fact]
    public async Task GetOrCreateAsync_never_flips_an_existing_conversations_sandbox_flag()
    {
        // A conversation's IsSandbox is set once at creation and must never
        // change afterward — otherwise a production conversation could start
        // being treated as free/no-ticket, or a sandbox one could suddenly
        // start creating real tickets, just because of which key a later
        // message happened to arrive through.
        await using var db = CreateContext(nameof(GetOrCreateAsync_never_flips_an_existing_conversations_sandbox_flag));
        var companyId = await SeedCompanyAsync(db);
        var service = new ConversationService(db, NullLogger<ConversationService>.Instance);

        var first = await service.GetOrCreateAsync(companyId, "session-1", ChannelType.WebChat, isSandbox: true);
        var second = await service.GetOrCreateAsync(companyId, "session-1", ChannelType.WebChat, isSandbox: false);

        Assert.Equal(first.Id, second.Id);
        Assert.True(second.IsSandbox);
    }

    [Fact]
    public async Task GetOrCreateAsync_keeps_sandbox_and_production_conversations_independent_across_customers()
    {
        // Different customerId (session id) values never collide, sandbox or not —
        // this is really a regression guard on GetOrCreateAsync's existing filter,
        // exercised here specifically for the sandbox codepath.
        await using var db = CreateContext(nameof(GetOrCreateAsync_keeps_sandbox_and_production_conversations_independent_across_customers));
        var companyId = await SeedCompanyAsync(db);
        var service = new ConversationService(db, NullLogger<ConversationService>.Instance);

        var sandboxConvo    = await service.GetOrCreateAsync(companyId, "sandbox-session", ChannelType.WebChat, isSandbox: true);
        var productionConvo = await service.GetOrCreateAsync(companyId, "production-session", ChannelType.WebChat, isSandbox: false);

        Assert.NotEqual(sandboxConvo.Id, productionConvo.Id);
        Assert.True(sandboxConvo.IsSandbox);
        Assert.False(productionConvo.IsSandbox);
    }
}
