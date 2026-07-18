using Api.Application.Abstractions;
using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests;

/// <summary>
/// Proves the global tenant query filter on AppDbContext actually works. This is
/// the test referenced from the doc-comment on AppDbContext.OnModelCreating, and
/// the one that should fail loudly if anyone ever weakens multi-tenant isolation.
/// </summary>
public class TenantIsolationTests
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

    [Fact]
    public async Task Agent_query_only_returns_rows_for_the_current_tenant()
    {
        const string dbName = nameof(Agent_query_only_returns_rows_for_the_current_tenant);

        var companyA = new Company { Name = "Acme Clinics", PublicApiKey = "pub_a", SecretApiKeyHash = "hash_a" };
        var companyB = new Company { Name = "Beta Bank", PublicApiKey = "pub_b", SecretApiKeyHash = "hash_b" };

        // Seed both companies' data using a tenant-agnostic context (no filter applied
        // yet because CompanyId is null here, and the filter compares against null
        // only when CompanyId on the provider is null — see seeding workaround below
        // via IgnoreQueryFilters on write, which Add/SaveChanges does not need anyway).
        var seedProvider = new FakeTenantProvider { CompanyId = null };
        using (var seedContext = CreateContext(dbName, seedProvider))
        {
            seedContext.Companies.AddRange(companyA, companyB);
            seedContext.Agents.AddRange(
                new Agent { CompanyId = companyA.Id, Name = "Alice", Email = "alice@acme.test", PasswordHash = "x", Role = AgentRole.Owner },
                new Agent { CompanyId = companyB.Id, Name = "Bob", Email = "bob@beta.test", PasswordHash = "x", Role = AgentRole.Owner });
            await seedContext.SaveChangesAsync();
        }

        // Act as Company A.
        var tenantAProvider = new FakeTenantProvider { CompanyId = companyA.Id };
        using var contextA = CreateContext(dbName, tenantAProvider);

        var visibleAgents = await contextA.Agents.ToListAsync();

        // Assert: Company A's context sees ONLY its own agent, never Bob from Company B.
        Assert.Single(visibleAgents);
        Assert.Equal("alice@acme.test", visibleAgents[0].Email);
        Assert.DoesNotContain(visibleAgents, a => a.Email == "bob@beta.test");
    }

    [Fact]
    public async Task IgnoreQueryFilters_is_the_only_way_to_see_other_tenants_data()
    {
        const string dbName = nameof(IgnoreQueryFilters_is_the_only_way_to_see_other_tenants_data);

        var companyA = new Company { Name = "Acme Clinics", PublicApiKey = "pub_a2", SecretApiKeyHash = "hash_a2" };
        var companyB = new Company { Name = "Beta Bank", PublicApiKey = "pub_b2", SecretApiKeyHash = "hash_b2" };

        using (var seedContext = CreateContext(dbName, new FakeTenantProvider { CompanyId = null }))
        {
            seedContext.Companies.AddRange(companyA, companyB);
            seedContext.Conversations.AddRange(
                new Conversation { CompanyId = companyA.Id, Channel = ChannelType.WebChat, CustomerId = "cust-a" },
                new Conversation { CompanyId = companyB.Id, Channel = ChannelType.WebChat, CustomerId = "cust-b" });
            await seedContext.SaveChangesAsync();
        }

        // Note: Company itself is the tenant ROOT, not an ITenantScoped entity (it has
        // no CompanyId to filter on), so the global filter intentionally does not apply
        // to Companies — only to things that hang off a Company, like Conversations.
        using var contextA = CreateContext(dbName, new FakeTenantProvider { CompanyId = companyA.Id });

        var filteredCount = await contextA.Conversations.CountAsync();
        var unfilteredCount = await contextA.Conversations.IgnoreQueryFilters().CountAsync();

        Assert.Equal(1, filteredCount);   // only Company A's conversation is visible by default
        Assert.Equal(2, unfilteredCount); // both are visible once filters are explicitly bypassed
    }

    /// <summary>
    /// ChannelConnections hold encrypted third-party credentials (WhatsApp access
    /// tokens, etc.) — of everything in this schema, a tenant-isolation failure here
    /// would be the most severe possible breach. Worth its own explicit test rather
    /// than just trusting the same filter logic already proven for Agent/Conversation.
    /// </summary>
    [Fact]
    public async Task ChannelConnection_credentials_are_never_visible_across_tenants()
    {
        const string dbName = nameof(ChannelConnection_credentials_are_never_visible_across_tenants);

        var companyA = new Company { Name = "Acme Clinics", PublicApiKey = "pub_a3", SecretApiKeyHash = "hash_a3" };
        var companyB = new Company { Name = "Beta Bank", PublicApiKey = "pub_b3", SecretApiKeyHash = "hash_b3" };

        using (var seedContext = CreateContext(dbName, new FakeTenantProvider { CompanyId = null }))
        {
            seedContext.Companies.AddRange(companyA, companyB);
            seedContext.ChannelConnections.AddRange(
                new ChannelConnection
                {
                    CompanyId = companyA.Id,
                    Channel = ChannelType.WhatsApp,
                    CredentialsEncrypted = "encrypted-token-belonging-to-company-a",
                },
                new ChannelConnection
                {
                    CompanyId = companyB.Id,
                    Channel = ChannelType.WhatsApp,
                    CredentialsEncrypted = "encrypted-token-belonging-to-company-b",
                });
            await seedContext.SaveChangesAsync();
        }

        using var contextA = CreateContext(dbName, new FakeTenantProvider { CompanyId = companyA.Id });

        var visibleConnections = await contextA.ChannelConnections.ToListAsync();

        Assert.Single(visibleConnections);
        Assert.Equal("encrypted-token-belonging-to-company-a", visibleConnections[0].CredentialsEncrypted);
        Assert.DoesNotContain(visibleConnections, c => c.CredentialsEncrypted == "encrypted-token-belonging-to-company-b");
    }

    /// <summary>
    /// Sprint 4 web crawling addition: WebSource/WebPage are ITenantScoped just like
    /// everything else, but they're new enough (and background-service-heavy enough,
    /// with lots of IgnoreQueryFilters() calls in WebCrawlerService/ContentFreshnessService)
    /// that it's worth proving the *default*, request-scoped path still isolates correctly.
    /// </summary>
    [Fact]
    public async Task WebSource_and_WebPage_queries_only_return_rows_for_the_current_tenant()
    {
        const string dbName = nameof(WebSource_and_WebPage_queries_only_return_rows_for_the_current_tenant);

        var companyA = new Company { Name = "Acme Clinics", PublicApiKey = "pub_a4", SecretApiKeyHash = "hash_a4" };
        var companyB = new Company { Name = "Beta Bank", PublicApiKey = "pub_b4", SecretApiKeyHash = "hash_b4" };

        WebSource sourceA;
        using (var seedContext = CreateContext(dbName, new FakeTenantProvider { CompanyId = null }))
        {
            seedContext.Companies.AddRange(companyA, companyB);

            sourceA = new WebSource { CompanyId = companyA.Id, Url = "https://acme.test" };
            var sourceB = new WebSource { CompanyId = companyB.Id, Url = "https://beta.test" };
            seedContext.WebSources.AddRange(sourceA, sourceB);

            seedContext.WebPages.AddRange(
                new WebPage { CompanyId = companyA.Id, WebSourceId = sourceA.Id, Url = "https://acme.test/pricing", ContentHash = "h1" },
                new WebPage { CompanyId = companyB.Id, WebSourceId = sourceB.Id, Url = "https://beta.test/rates", ContentHash = "h2" });

            await seedContext.SaveChangesAsync();
        }

        using var contextA = CreateContext(dbName, new FakeTenantProvider { CompanyId = companyA.Id });

        var visibleSources = await contextA.WebSources.ToListAsync();
        var visiblePages   = await contextA.WebPages.ToListAsync();

        Assert.Single(visibleSources);
        Assert.Equal("https://acme.test", visibleSources[0].Url);

        Assert.Single(visiblePages);
        Assert.Equal("https://acme.test/pricing", visiblePages[0].Url);
        Assert.DoesNotContain(visiblePages, p => p.Url.Contains("beta.test"));
    }
}
