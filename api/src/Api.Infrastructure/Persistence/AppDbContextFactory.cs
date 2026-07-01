using Api.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pgvector.EntityFrameworkCore;

namespace Api.Infrastructure.Persistence;

/// <summary>
/// EF Core design-time tooling (dotnet ef migrations add / database update) needs
/// to construct an AppDbContext without going through the app's normal DI container
/// (and therefore without a real ICurrentTenantProvider). This factory supplies a
/// no-op tenant provider just for that purpose — it is never used at runtime.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        // Design-time only connection string — migrations don't execute data, so any
        // reachable Postgres instance (or even a syntactically valid string) is fine.
        var connectionString = Environment.GetEnvironmentVariable("DESIGN_TIME_CONNECTION_STRING")
            ?? "Host=localhost;Port=5433;Database=ai_support_platform;Username=postgres;Password=4932";

        optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql.UseVector());

        return new AppDbContext(optionsBuilder.Options, new NullCurrentTenantProvider());
    }

    private class NullCurrentTenantProvider : ICurrentTenantProvider
    {
        public Guid? CompanyId => null;
        public Guid? AgentId => null;
    }
}
