using Api.Domain.Entities;

namespace Api.Infrastructure.Actions;

/// <summary>
/// Everything a runner needs to execute one action — deliberately NOT named
/// ActionContext (the Phase 2 spec's own name) to avoid any risk of confusion
/// with the also-plausible Microsoft.AspNetCore.Http.HttpContext-adjacent
/// naming conventions elsewhere in ASP.NET Core; ActionExecutionContext is
/// unambiguous on its own.
/// </summary>
public sealed record ActionExecutionContext(
    Guid CompanyId,
    Guid ConversationId,
    string CustomerId,
    string ActionType,
    IReadOnlyDictionary<string, string> Parameters,
    bool IdentityVerified);

/// <summary>
/// Outcome of one runner execution. NOT named ActionResult (the Phase 2 spec's
/// own name) — that identifier collides with Microsoft.AspNetCore.Mvc.ActionResult,
/// which every controller in this codebase already imports via
/// `using Microsoft.AspNetCore.Mvc;`. Using the spec's literal name here would
/// make this type ambiguous (a compile error, not just a style nit) the moment
/// any file needs both. See IAiProvider.cs's doc comment for the same reasoning
/// applied to DetectedIntent/AvailableAction.
/// </summary>
public sealed record ActionRunResult(bool Success, string Message, int? HttpStatusCode = null);

/// <summary>
/// Sprint 9 Action Engine: executes one action against its ActionDefinition.
/// WebhookActionRunner (the only implementation right now) POSTs to the
/// company's own backend; the interface exists so a future built-in runner
/// (e.g. a native M-Pesa balance check in Sprint 10) doesn't have to pretend
/// to be a webhook call to fit in.
/// </summary>
public interface IAIActionRunner
{
    Task<ActionRunResult> ExecuteAsync(
        ActionExecutionContext context,
        ActionDefinition definition,
        CancellationToken ct = default);
}
