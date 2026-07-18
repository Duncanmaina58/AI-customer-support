using Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Application.Abstractions;

/// <summary>
/// Shape of the persistence context that Application-layer use cases depend on.
/// Implemented by AppDbContext in Api.Infrastructure. Application still takes a
/// dependency on Microsoft.EntityFrameworkCore for the DbSet&lt;T&gt; type — if you
/// want Application to be 100% persistence-agnostic, swap DbSet&lt;T&gt; for
/// IQueryable&lt;T&gt; / a repository-per-aggregate instead.
/// </summary>
public interface IAppDbContext
{
    DbSet<Company> Companies { get; }
    DbSet<Agent> Agents { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<Message> Messages { get; }
    DbSet<Ticket> Tickets { get; }
    DbSet<KnowledgeChunk> KnowledgeChunks { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<AgentSecurityToken> AgentSecurityTokens { get; }
    DbSet<ChannelConnection> ChannelConnections { get; }
    DbSet<MpesaTransaction> MpesaTransactions { get; }

    // ---- Sprint 4 web crawling ----
    DbSet<WebSource> WebSources { get; }
    DbSet<WebPage> WebPages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
