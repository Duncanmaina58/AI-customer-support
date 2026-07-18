using Api.Application.Abstractions;
using Api.Domain.Common;
using Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure.Persistence;

/// <summary>
/// The EF Core context. The constructor takes ICurrentTenantProvider and uses it to
/// install a GLOBAL QUERY FILTER on every ITenantScoped entity (see OnModelCreating).
///
/// This is the single most important piece of code for multi-tenant safety in this
/// codebase — it's what guarantees that, no matter what LINQ query a future controller
/// or service writes, it is IMPOSSIBLE to accidentally read another company's rows
/// without explicitly opting out via IgnoreQueryFilters().
///
/// See tests/Api.Tests/TenantIsolationTests.cs for the test that proves this.
/// </summary>
public class AppDbContext : DbContext, IAppDbContext
{
    private readonly ICurrentTenantProvider _tenantProvider;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenantProvider tenantProvider)
        : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AgentSecurityToken> AgentSecurityTokens => Set<AgentSecurityToken>();
    public DbSet<ChannelConnection> ChannelConnections => Set<ChannelConnection>();

    // NOTE: this previously read `=> throw new NotImplementedException();`, which meant
    // every M-Pesa STK Push flow (BillingController.InitiateStkPush / MpesaCallback)
    // threw at runtime the instant it touched this DbSet. Fixed as part of the Sprint 4
    // web crawling pass — flagged here because it's a pre-existing, unrelated bug that
    // was encountered while wiring up the new WebSources/WebPages DbSets below.
    public DbSet<MpesaTransaction> MpesaTransactions => Set<MpesaTransaction>();

    // ---- Sprint 4 web crawling ----
    public DbSet<WebSource> WebSources => Set<WebSource>();
    public DbSet<WebPage> WebPages => Set<WebPage>();


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // ---- pgvector incompatibility with InMemory provider (unit tests) ----
        // The InMemory provider used in tests cannot map Pgvector.Vector. We ignore
        // the Embedding property entirely when running under InMemory so the model
        // validates cleanly and all tests pass. In production (Npgsql) the full
        // KnowledgeChunkConfiguration applies and the vector(1024) column is used.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            modelBuilder.Entity<KnowledgeChunk>()
                .Ignore(k => k.Embedding);
        }

        // ---- Global tenant query filter ----
        // For every entity type that implements ITenantScoped, automatically add
        // `.Where(e => e.CompanyId == _tenantProvider.CompanyId)` to EVERY query EF
        // Core generates for it — Find, Where, Include, everything. Callers that
        // genuinely need cross-tenant access (e.g. a system/admin job) must call
        // `.IgnoreQueryFilters()` explicitly, which makes the bypass visible in review.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(AppDbContext)
                    .GetMethod(nameof(BuildTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .MakeGenericMethod(entityType.ClrType);

                var filter = method.Invoke(null, new object[] { this });
                entityType.SetQueryFilter((System.Linq.Expressions.LambdaExpression)filter!);
            }
        }

        base.OnModelCreating(modelBuilder);
    }

    private static System.Linq.Expressions.LambdaExpression BuildTenantFilter<TEntity>(AppDbContext context)
        where TEntity : class, ITenantScoped
    {
        System.Linq.Expressions.Expression<Func<TEntity, bool>> filter =
            e => e.CompanyId == context._tenantProvider.CompanyId;
        return filter;
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Auto-stamp UpdatedAt on every modified AuditableEntity.
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}