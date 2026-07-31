using Api.Domain.Entities;
using Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure.Actions;

/// <summary>
/// Sprint 9 Action Engine: resolves (CompanyId, action_type) -> ActionDefinition.
/// Deliberately takes AppDbContext directly (not IAppDbContext) — ActionEngineService
/// and the pipeline callers run inside a normal per-request DI scope where the
/// tenant filter is exactly what we want here (a registry lookup should never
/// leak another company's action definitions), so no IgnoreQueryFilters() anywhere
/// in this class.
/// </summary>
public interface IActionRegistry
{
    Task<ActionDefinition?> FindAsync(Guid companyId, string actionType, CancellationToken ct = default);

    /// <summary>Every active, available action for a company — what the system prompt lists as "things you can do".</summary>
    Task<IReadOnlyList<ActionDefinition>> GetActiveActionsAsync(Guid companyId, CancellationToken ct = default);
}

public class ActionRegistry : IActionRegistry
{
    private readonly AppDbContext _db;

    public ActionRegistry(AppDbContext db)
    {
        _db = db;
    }

    public Task<ActionDefinition?> FindAsync(Guid companyId, string actionType, CancellationToken ct = default) =>
        _db.ActionDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.CompanyId == companyId && a.ActionType == actionType && a.IsActive, ct);

    public async Task<IReadOnlyList<ActionDefinition>> GetActiveActionsAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.ActionDefinitions
            .AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.IsActive)
            .OrderBy(a => a.DisplayName)
            .ToListAsync(ct);
}
