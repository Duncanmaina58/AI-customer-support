using System.Security.Claims;
using Api.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Api.Infrastructure.Identity;

/// <summary>
/// Reads CompanyId / AgentId out of the current HTTP request's validated JWT claims.
/// This is the only place in the codebase that's allowed to know about HttpContext —
/// everything downstream (AppDbContext, Application services) just calls
/// ICurrentTenantProvider and stays testable without spinning up ASP.NET Core.
/// </summary>
public class HttpCurrentTenantProvider : ICurrentTenantProvider
{
    public const string CompanyIdClaimType = "company_id";
    public const string AgentIdClaimType = "agent_id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentTenantProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? CompanyId => TryGetGuidClaim(CompanyIdClaimType);
    public Guid? AgentId => TryGetGuidClaim(AgentIdClaimType);

    private Guid? TryGetGuidClaim(string claimType)
    {
        var value = _httpContextAccessor.HttpContext?.User?.FindFirst(claimType)?.Value;
        return Guid.TryParse(value, out var guid) ? guid : null;
    }
}
