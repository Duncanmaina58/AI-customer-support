using Api.Application.Abstractions;
using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.Billing;
using Api.Infrastructure.Channels;
using Api.Infrastructure.Persistence;
using Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests;

/// <summary>
/// Sprint 7: token-budget enforcement (the piece safe to test without a
/// mocking library — same InMemory-DbContext pattern established in
/// Sprint5Tests/Sprint6Tests) and BillingPlanCatalog sanity checks.
///
/// Not covered here: MpesaClient (real HTTP to Safaricom), BillingController's
/// M-Pesa initiate/callback flow (needs a real or mocked IMpesaClient — no
/// mocking library in this project), and the analytics endpoints' SQL
/// aggregation (would need seeding a much larger dataset to be meaningful).
/// All three got careful manual review instead, same rationale used for
/// ChatChannelPipelineService and the channel clients in Sprint 6.
/// </summary>
public class Sprint7Tests
{
    private class FakeTenantProvider : ICurrentTenantProvider
    {
        public Guid? CompanyId { get; set; }
        public Guid? AgentId { get; set; }
    }

    private class NoOpBrevoEmailClient : IBrevoEmailClient
    {
        public List<BrevoOutboundEmail> Sent { get; } = [];

        public Task SendAsync(BrevoOutboundEmail email, CancellationToken ct = default)
        {
            Sent.Add(email);
            return Task.CompletedTask;
        }
    }

    private static AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new AppDbContext(options, new FakeTenantProvider { CompanyId = null });
    }

    private static async Task<Guid> SeedCompanyAsync(
        AppDbContext db, int monthlyTokenBudget = 1000, int tokensUsedThisMonth = 0)
    {
        var company = new Company
        {
            Name                 = "Acme Ltd",
            PublicApiKey         = $"pub_{Guid.NewGuid():N}",
            SecretApiKeyHash     = "hash",
            SandboxToken         = $"sbx_{Guid.NewGuid():N}",
            MonthlyTokenBudget   = monthlyTokenBudget,
            TokensUsedThisMonth  = tokensUsedThisMonth,
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company.Id;
    }

    // -------------------------------------------------------------------------
    // TokenBudgetService.IsOverBudgetAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IsOverBudgetAsync_false_when_well_under_budget()
    {
        await using var db = CreateContext(nameof(IsOverBudgetAsync_false_when_well_under_budget));
        var companyId = await SeedCompanyAsync(db, monthlyTokenBudget: 1000, tokensUsedThisMonth: 500);
        var service = new TokenBudgetService(db, new NoOpBrevoEmailClient(), NullLogger<TokenBudgetService>.Instance);

        Assert.False(await service.IsOverBudgetAsync(companyId));
    }

    [Fact]
    public async Task IsOverBudgetAsync_true_once_usage_meets_the_budget_exactly()
    {
        await using var db = CreateContext(nameof(IsOverBudgetAsync_true_once_usage_meets_the_budget_exactly));
        var companyId = await SeedCompanyAsync(db, monthlyTokenBudget: 1000, tokensUsedThisMonth: 1000);
        var service = new TokenBudgetService(db, new NoOpBrevoEmailClient(), NullLogger<TokenBudgetService>.Instance);

        Assert.True(await service.IsOverBudgetAsync(companyId));
    }

    [Fact]
    public async Task IsOverBudgetAsync_true_when_over_budget()
    {
        await using var db = CreateContext(nameof(IsOverBudgetAsync_true_when_over_budget));
        var companyId = await SeedCompanyAsync(db, monthlyTokenBudget: 1000, tokensUsedThisMonth: 1500);
        var service = new TokenBudgetService(db, new NoOpBrevoEmailClient(), NullLogger<TokenBudgetService>.Instance);

        Assert.True(await service.IsOverBudgetAsync(companyId));
    }

    [Fact]
    public async Task IsOverBudgetAsync_treats_a_zero_budget_as_unlimited()
    {
        // Defensive: a data-entry mistake leaving MonthlyTokenBudget at 0
        // shouldn't accidentally lock a company out of the AI entirely.
        await using var db = CreateContext(nameof(IsOverBudgetAsync_treats_a_zero_budget_as_unlimited));
        var companyId = await SeedCompanyAsync(db, monthlyTokenBudget: 0, tokensUsedThisMonth: 5000);
        var service = new TokenBudgetService(db, new NoOpBrevoEmailClient(), NullLogger<TokenBudgetService>.Instance);

        Assert.False(await service.IsOverBudgetAsync(companyId));
    }

    // -------------------------------------------------------------------------
    // TokenBudgetService.CheckAndSendBudgetWarningIfNeededAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CheckAndSendBudgetWarningIfNeededAsync_does_not_send_below_90_percent()
    {
        await using var db = CreateContext(nameof(CheckAndSendBudgetWarningIfNeededAsync_does_not_send_below_90_percent));
        var companyId = await SeedCompanyAsync(db, monthlyTokenBudget: 1000, tokensUsedThisMonth: 800); // 80%
        var email = new NoOpBrevoEmailClient();
        var service = new TokenBudgetService(db, email, NullLogger<TokenBudgetService>.Instance);

        await service.CheckAndSendBudgetWarningIfNeededAsync(companyId);

        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task CheckAndSendBudgetWarningIfNeededAsync_sends_once_the_owner_at_90_percent()
    {
        await using var db = CreateContext(nameof(CheckAndSendBudgetWarningIfNeededAsync_sends_once_the_owner_at_90_percent));
        var companyId = await SeedCompanyAsync(db, monthlyTokenBudget: 1000, tokensUsedThisMonth: 900); // exactly 90%

        db.Agents.Add(new Agent
        {
            CompanyId = companyId, Name = "Jane Owner", Email = "jane@acme.co.ke",
            Role = AgentRole.Owner, IsActive = true, PasswordHash = "hash",
        });
        await db.SaveChangesAsync();

        var email = new NoOpBrevoEmailClient();
        var service = new TokenBudgetService(db, email, NullLogger<TokenBudgetService>.Instance);

        await service.CheckAndSendBudgetWarningIfNeededAsync(companyId);

        Assert.Single(email.Sent);
        Assert.Equal("jane@acme.co.ke", email.Sent[0].ToEmail);
    }

    [Fact]
    public async Task CheckAndSendBudgetWarningIfNeededAsync_only_ever_sends_once_per_period()
    {
        await using var db = CreateContext(nameof(CheckAndSendBudgetWarningIfNeededAsync_only_ever_sends_once_per_period));
        var companyId = await SeedCompanyAsync(db, monthlyTokenBudget: 1000, tokensUsedThisMonth: 950);

        db.Agents.Add(new Agent
        {
            CompanyId = companyId, Name = "Jane Owner", Email = "jane@acme.co.ke",
            Role = AgentRole.Owner, IsActive = true, PasswordHash = "hash",
        });
        await db.SaveChangesAsync();

        var email = new NoOpBrevoEmailClient();
        var service = new TokenBudgetService(db, email, NullLogger<TokenBudgetService>.Instance);

        // Simulate several messages in a row all crossing the threshold check —
        // exactly what happens in production across several AI replies before
        // the period resets.
        await service.CheckAndSendBudgetWarningIfNeededAsync(companyId);
        await service.CheckAndSendBudgetWarningIfNeededAsync(companyId);
        await service.CheckAndSendBudgetWarningIfNeededAsync(companyId);

        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task CheckAndSendBudgetWarningIfNeededAsync_sends_to_both_owner_and_admin()
    {
        await using var db = CreateContext(nameof(CheckAndSendBudgetWarningIfNeededAsync_sends_to_both_owner_and_admin));
        var companyId = await SeedCompanyAsync(db, monthlyTokenBudget: 1000, tokensUsedThisMonth: 950);

        db.Agents.AddRange(
            new Agent { CompanyId = companyId, Name = "Owner", Email = "owner@acme.co.ke", Role = AgentRole.Owner, IsActive = true, PasswordHash = "hash" },
            new Agent { CompanyId = companyId, Name = "Admin", Email = "admin@acme.co.ke", Role = AgentRole.Admin, IsActive = true, PasswordHash = "hash" },
            new Agent { CompanyId = companyId, Name = "Agent", Email = "agent@acme.co.ke", Role = AgentRole.Agent, IsActive = true, PasswordHash = "hash" });
        await db.SaveChangesAsync();

        var email = new NoOpBrevoEmailClient();
        var service = new TokenBudgetService(db, email, NullLogger<TokenBudgetService>.Instance);

        await service.CheckAndSendBudgetWarningIfNeededAsync(companyId);

        // Owner + Admin get warned; a plain Agent (no billing authority) doesn't.
        Assert.Equal(2, email.Sent.Count);
        Assert.Contains(email.Sent, e => e.ToEmail == "owner@acme.co.ke");
        Assert.Contains(email.Sent, e => e.ToEmail == "admin@acme.co.ke");
        Assert.DoesNotContain(email.Sent, e => e.ToEmail == "agent@acme.co.ke");
    }

    // -------------------------------------------------------------------------
    // BillingPlanCatalog
    // -------------------------------------------------------------------------

    [Fact]
    public void BillingPlanCatalog_has_all_three_plans()
    {
        Assert.Equal(3, BillingPlanCatalog.Plans.Count);
        Assert.True(BillingPlanCatalog.Plans.ContainsKey(CompanyPlan.Starter));
        Assert.True(BillingPlanCatalog.Plans.ContainsKey(CompanyPlan.Growth));
        Assert.True(BillingPlanCatalog.Plans.ContainsKey(CompanyPlan.Enterprise));
    }

    [Fact]
    public void BillingPlanCatalog_prices_increase_with_plan_tier()
    {
        var starter = BillingPlanCatalog.Get(CompanyPlan.Starter);
        var growth = BillingPlanCatalog.Get(CompanyPlan.Growth);
        var enterprise = BillingPlanCatalog.Get(CompanyPlan.Enterprise);

        Assert.True(starter.PriceKes < growth.PriceKes);
        Assert.True(growth.PriceKes < enterprise.PriceKes);
    }

    [Fact]
    public void BillingPlanCatalog_starter_token_budget_matches_the_default_new_company_budget()
    {
        // A brand-new signup (Company.MonthlyTokenBudget's C# default) should
        // already match the Starter plan's catalog entry — otherwise a new
        // company's enforcement wouldn't match what the pricing page promised.
        var starter = BillingPlanCatalog.Get(CompanyPlan.Starter);
        var freshCompanyDefault = new Company().MonthlyTokenBudget;

        Assert.Equal(freshCompanyDefault, starter.MonthlyTokenBudget);
    }

    // -------------------------------------------------------------------------
    // ConversationService.SubmitCsatRatingAsync (this pass — real CSAT)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SubmitCsatRatingAsync_accepts_a_valid_score_and_records_it()
    {
        await using var db = CreateContext(nameof(SubmitCsatRatingAsync_accepts_a_valid_score_and_records_it));
        var companyId = await SeedCompanyAsync(db);
        var service = new ConversationService(db, NullLogger<ConversationService>.Instance);
        var conversation = await service.GetOrCreateAsync(companyId, "session-1", ChannelType.WebChat);

        var accepted = await service.SubmitCsatRatingAsync(conversation.Id, 5);

        Assert.True(accepted);

        await using var verifyDb = CreateContext(nameof(SubmitCsatRatingAsync_accepts_a_valid_score_and_records_it));
        var reloaded = await verifyDb.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == conversation.Id);
        Assert.Equal(5, reloaded.CsatScore);
        Assert.NotNull(reloaded.CsatSubmittedAt);
    }

    [Fact]
    public async Task SubmitCsatRatingAsync_rejects_a_resubmission_and_keeps_the_first_rating()
    {
        await using var db = CreateContext(nameof(SubmitCsatRatingAsync_rejects_a_resubmission_and_keeps_the_first_rating));
        var companyId = await SeedCompanyAsync(db);
        var service = new ConversationService(db, NullLogger<ConversationService>.Instance);
        var conversation = await service.GetOrCreateAsync(companyId, "session-1", ChannelType.WebChat);

        var first = await service.SubmitCsatRatingAsync(conversation.Id, 2);
        var second = await service.SubmitCsatRatingAsync(conversation.Id, 5); // different score — shouldn't overwrite

        Assert.True(first);
        Assert.False(second);

        await using var verifyDb = CreateContext(nameof(SubmitCsatRatingAsync_rejects_a_resubmission_and_keeps_the_first_rating));
        var reloaded = await verifyDb.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == conversation.Id);
        Assert.Equal(2, reloaded.CsatScore); // the first rating, not the second
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task SubmitCsatRatingAsync_rejects_scores_outside_1_to_5(int invalidScore)
    {
        await using var db = CreateContext($"{nameof(SubmitCsatRatingAsync_rejects_scores_outside_1_to_5)}_{invalidScore}");
        var companyId = await SeedCompanyAsync(db);
        var service = new ConversationService(db, NullLogger<ConversationService>.Instance);
        var conversation = await service.GetOrCreateAsync(companyId, "session-1", ChannelType.WebChat);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.SubmitCsatRatingAsync(conversation.Id, invalidScore));
    }

    [Fact]
    public async Task SubmitCsatRatingAsync_returns_false_for_a_nonexistent_conversation()
    {
        await using var db = CreateContext(nameof(SubmitCsatRatingAsync_returns_false_for_a_nonexistent_conversation));
        var service = new ConversationService(db, NullLogger<ConversationService>.Instance);

        var accepted = await service.SubmitCsatRatingAsync(Guid.NewGuid(), 4);

        Assert.False(accepted);
    }
}
