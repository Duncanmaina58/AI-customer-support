namespace Api.Contracts.Actions;

public record CreateActionDefinitionRequest(
    string ActionType,
    string DisplayName,
    string WebhookUrl,
    string WebhookSecret,
    bool RequiresVerification,
    bool IsReadOnly,
    string? ParameterSchema,
    int TimeoutSeconds = 10);

/// <summary>
/// Every field optional — PATCH-style partial update. WebhookSecret is
/// deliberately excluded here; rotating it is its own endpoint
/// (RegenerateSecret) so a routine display-name edit can never accidentally
/// blank out or leak the signing secret.
/// </summary>
public record UpdateActionDefinitionRequest(
    string? DisplayName,
    string? WebhookUrl,
    bool? RequiresVerification,
    bool? IsReadOnly,
    string? ParameterSchema,
    int? TimeoutSeconds,
    bool? IsActive);

public record ActionDefinitionDto(
    Guid Id,
    string ActionType,
    string DisplayName,
    string WebhookUrl,
    bool RequiresVerification,
    bool IsReadOnly,
    string? ParameterSchema,
    int TimeoutSeconds,
    bool IsActive,
    DateTime CreatedAt,
    int TotalExecutions,
    int SuccessfulExecutions);

/// <summary>Returned once, immediately after RegenerateSecret — never retrievable again afterward, same one-time-reveal pattern as Company's own secret API key.</summary>
public record WebhookSecretRevealDto(string WebhookSecret);

public record ActionLogDto(
    Guid Id,
    Guid? ActionDefinitionId,
    string ActionType,
    string CustomerId,
    IReadOnlyDictionary<string, string> Parameters,
    bool IdentityVerified,
    string VerificationMethod,
    int? WebhookStatusCode,
    bool Success,
    string? ErrorMessage,
    DateTime ExecutedAt,
    int DurationMs);
