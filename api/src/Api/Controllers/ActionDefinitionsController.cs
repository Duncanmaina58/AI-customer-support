using System.Security.Cryptography;
using System.Text.Json;
using Api.Application.Abstractions;
using Api.Contracts.Actions;
using Api.Domain.Entities;
using Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

/// <summary>
/// Sprint 9 Action Engine: dashboard CRUD for a company's registered actions,
/// plus a read-only audit view over ActionLogs. Mutating endpoints (create,
/// update, delete, regenerate-secret) are Owner/Admin-only — an action
/// definition controls a live webhook with a real backend side effect, same
/// trust tier as connecting a channel (see ChannelsController).
/// </summary>
[ApiController]
[Authorize]
[Route("api/actions")]
public class ActionDefinitionsController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly ICurrentTenantProvider _tenant;
    private readonly IActionEngineSecretProtector _protector;

    public ActionDefinitionsController(
        IAppDbContext db, ICurrentTenantProvider tenant, IActionEngineSecretProtector protector)
    {
        _db        = db;
        _tenant    = tenant;
        _protector = protector;
    }

    // -------------------------------------------------------------------
    // GET /api/actions
    // -------------------------------------------------------------------
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ActionDefinitionDto>>> List(CancellationToken ct)
    {
        var definitions = await _db.ActionDefinitions
            .AsNoTracking()
            .OrderBy(a => a.DisplayName)
            .ToListAsync(ct);

        var stats = await _db.ActionLogs
            .AsNoTracking()
            .Where(l => l.ActionDefinitionId != null)
            .GroupBy(l => l.ActionDefinitionId)
            .Select(g => new { ActionDefinitionId = g.Key, Total = g.Count(), Successful = g.Count(l => l.Success) })
            .ToListAsync(ct);

        var statsById = stats.ToDictionary(s => s.ActionDefinitionId!.Value, s => s);

        var result = definitions.Select(d =>
        {
            statsById.TryGetValue(d.Id, out var s);
            return ToDto(d, s?.Total ?? 0, s?.Successful ?? 0);
        });

        return Ok(result);
    }

    // -------------------------------------------------------------------
    // GET /api/actions/{id}
    // -------------------------------------------------------------------
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ActionDefinitionDto>> GetById(Guid id, CancellationToken ct)
    {
        var definition = await _db.ActionDefinitions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (definition is null) return NotFound();

        var total = await _db.ActionLogs.CountAsync(l => l.ActionDefinitionId == id, ct);
        var successful = await _db.ActionLogs.CountAsync(l => l.ActionDefinitionId == id && l.Success, ct);

        return Ok(ToDto(definition, total, successful));
    }

    // -------------------------------------------------------------------
    // POST /api/actions
    // -------------------------------------------------------------------
    [HttpPost]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<ActionDefinitionDto>> Create(CreateActionDefinitionRequest request, CancellationToken ct)
    {
        if (_tenant.CompanyId is not { } companyId)
            return Unauthorized(new { message = "No company context." });

        var validationError = ValidateActionType(request.ActionType) ?? ValidateWebhookUrl(request.WebhookUrl);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return BadRequest(new { message = "Display name is required." });

        if (string.IsNullOrWhiteSpace(request.WebhookSecret) || request.WebhookSecret.Length < 16)
            return BadRequest(new { message = "Webhook secret must be at least 16 characters — use the generated one unless you have a specific reason to bring your own." });

        var normalizedActionType = request.ActionType.Trim().ToLowerInvariant();

        var alreadyExists = await _db.ActionDefinitions
            .AnyAsync(a => a.CompanyId == companyId && a.ActionType == normalizedActionType, ct);
        if (alreadyExists)
            return Conflict(new { message = $"An action with type \"{normalizedActionType}\" already exists." });

        var definition = new ActionDefinition
        {
            CompanyId              = companyId,
            ActionType             = normalizedActionType,
            DisplayName            = request.DisplayName.Trim(),
            WebhookUrl             = request.WebhookUrl.Trim(),
            WebhookSecretEncrypted = _protector.Encrypt(request.WebhookSecret),
            RequiresVerification   = request.RequiresVerification,
            IsReadOnly             = request.IsReadOnly,
            ParameterSchema        = string.IsNullOrWhiteSpace(request.ParameterSchema) ? null : request.ParameterSchema.Trim(),
            TimeoutSeconds         = Math.Clamp(request.TimeoutSeconds <= 0 ? 10 : request.TimeoutSeconds, 1, 30),
        };

        _db.ActionDefinitions.Add(definition);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = definition.Id }, ToDto(definition, 0, 0));
    }

    // -------------------------------------------------------------------
    // PUT /api/actions/{id}
    // -------------------------------------------------------------------
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<ActionDefinitionDto>> Update(Guid id, UpdateActionDefinitionRequest request, CancellationToken ct)
    {
        var definition = await _db.ActionDefinitions.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (definition is null) return NotFound();

        if (request.WebhookUrl is not null)
        {
            var error = ValidateWebhookUrl(request.WebhookUrl);
            if (error is not null) return BadRequest(new { message = error });
            definition.WebhookUrl = request.WebhookUrl.Trim();
        }

        if (request.DisplayName is not null)
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName))
                return BadRequest(new { message = "Display name can't be blank." });
            definition.DisplayName = request.DisplayName.Trim();
        }

        if (request.RequiresVerification.HasValue) definition.RequiresVerification = request.RequiresVerification.Value;
        if (request.IsReadOnly.HasValue) definition.IsReadOnly = request.IsReadOnly.Value;
        if (request.ParameterSchema is not null) definition.ParameterSchema = string.IsNullOrWhiteSpace(request.ParameterSchema) ? null : request.ParameterSchema.Trim();
        if (request.TimeoutSeconds.HasValue) definition.TimeoutSeconds = Math.Clamp(request.TimeoutSeconds.Value, 1, 30);
        if (request.IsActive.HasValue) definition.IsActive = request.IsActive.Value;

        await _db.SaveChangesAsync(ct);

        var total = await _db.ActionLogs.CountAsync(l => l.ActionDefinitionId == id, ct);
        var successful = await _db.ActionLogs.CountAsync(l => l.ActionDefinitionId == id && l.Success, ct);
        return Ok(ToDto(definition, total, successful));
    }

    // -------------------------------------------------------------------
    // POST /api/actions/{id}/regenerate-secret
    // -------------------------------------------------------------------
    [HttpPost("{id:guid}/regenerate-secret")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<WebhookSecretRevealDto>> RegenerateSecret(Guid id, CancellationToken ct)
    {
        var definition = await _db.ActionDefinitions.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (definition is null) return NotFound();

        var newSecret = GenerateWebhookSecret();
        definition.WebhookSecretEncrypted = _protector.Encrypt(newSecret);
        await _db.SaveChangesAsync(ct);

        return Ok(new WebhookSecretRevealDto(newSecret));
    }

    // -------------------------------------------------------------------
    // DELETE /api/actions/{id}
    // -------------------------------------------------------------------
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var definition = await _db.ActionDefinitions.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (definition is null) return NotFound();

        var hasHistory = await _db.ActionLogs.AnyAsync(l => l.ActionDefinitionId == id, ct);
        if (hasHistory)
        {
            return Conflict(new
            {
                message = "This action has execution history and can't be deleted — turn it off instead (set it inactive) to preserve the audit trail.",
            });
        }

        _db.ActionDefinitions.Remove(definition);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // -------------------------------------------------------------------
    // GET /api/actions/logs
    // -------------------------------------------------------------------
    [HttpGet("logs")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<IReadOnlyList<ActionLogDto>>> GetLogs(
        [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var logs = await _db.ActionLogs
            .AsNoTracking()
            .OrderByDescending(l => l.ExecutedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);

        var result = logs.Select(l =>
        {
            Dictionary<string, string> parameters;
            try
            {
                parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(_protector.Decrypt(l.ParametersEncrypted))
                             ?? new Dictionary<string, string>();
            }
            catch
            {
                // Belt-and-braces: a log written before a key-ring rotation, or any
                // other decrypt/deserialize hiccup, shouldn't 500 the whole list.
                parameters = new Dictionary<string, string>();
            }

            return new ActionLogDto(
                l.Id, l.ActionDefinitionId, l.ActionType, l.CustomerId, parameters,
                l.IdentityVerified, l.VerificationMethod.ToString(), l.WebhookStatusCode,
                l.Success, l.ErrorMessage, l.ExecutedAt, l.DurationMs);
        });

        return Ok(result);
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static ActionDefinitionDto ToDto(ActionDefinition d, int total, int successful) => new(
        d.Id, d.ActionType, d.DisplayName, d.WebhookUrl, d.RequiresVerification, d.IsReadOnly,
        d.ParameterSchema, d.TimeoutSeconds, d.IsActive, d.CreatedAt, total, successful);

    private static string? ValidateActionType(string actionType)
    {
        if (string.IsNullOrWhiteSpace(actionType))
            return "Action type is required.";
        if (!System.Text.RegularExpressions.Regex.IsMatch(actionType.Trim(), "^[a-z][a-z0-9_]{2,99}$"))
            return "Action type must be lowercase snake_case (letters, numbers, underscores), starting with a letter, e.g. \"cancel_order\".";
        return null;
    }

    private static string? ValidateWebhookUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps)
            return "Webhook URL must be a valid https:// address — plain http:// is not allowed since the action payload can include customer data.";
        return null;
    }

    private static string GenerateWebhookSecret() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}
