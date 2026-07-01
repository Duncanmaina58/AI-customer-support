using Api.Domain.Common;
using Api.Domain.Enums;

namespace Api.Domain.Entities;

/// <summary>
/// A human user belonging to a Company — agents, admins, the company owner.
/// (Distinct from the AI itself, which acts as a virtual sender on Messages.)
/// </summary>
public class Agent : AuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public AgentRole Role { get; set; } = AgentRole.Agent;

    public DateTime? LastActiveAt { get; set; }
    public bool IsActive { get; set; } = true;
}
