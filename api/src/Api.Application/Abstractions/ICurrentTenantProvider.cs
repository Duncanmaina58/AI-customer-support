namespace Api.Application.Abstractions;

/// <summary>
/// Resolves the current tenant (Company) for the request being processed.
/// Implemented in Api (HTTP layer) by reading the CompanyId claim from the
/// validated JWT, and consumed by Api.Infrastructure to drive the global
/// EF Core query filter. Keeping this as an interface here (rather than
/// referencing HttpContext directly) is what lets Domain/Application/Infrastructure
/// stay free of any ASP.NET Core dependency.
/// </summary>
public interface ICurrentTenantProvider
{
    /// <summary>The authenticated Company's id, or null for unauthenticated/system contexts.</summary>
    Guid? CompanyId { get; }

    /// <summary>The authenticated Agent's id, or null.</summary>
    Guid? AgentId { get; }
}
