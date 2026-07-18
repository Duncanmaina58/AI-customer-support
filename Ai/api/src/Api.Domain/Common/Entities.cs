namespace Api.Domain.Common;

/// <summary>
/// Marker interface for any entity that belongs to a single Company (tenant).
/// Every implementation gets automatically filtered by the global EF Core
/// query filter configured in Api.Infrastructure — see TenantQueryFilterExtensions.
/// </summary>
public interface ITenantScoped
{
    Guid CompanyId { get; set; }
}

/// <summary>
/// Common audit fields shared by most entities.
/// </summary>
public abstract class AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
