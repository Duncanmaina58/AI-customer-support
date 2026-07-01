namespace Api.Contracts.Companies;

public record CompanyDto(Guid Id, string Name, string Plan, string PublicApiKey);

/// <summary>
/// SecretApiKey is the plaintext secret key, returned exactly once at registration
/// time. Only its hash is stored server-side (see AuthController.HashSecret) —
/// if it's lost, it has to be regenerated, not recovered. Same one-time-reveal
/// pattern as InviteAgentResponse.TemporaryPassword.
/// </summary>
public record RegisterCompanyResponse(CompanyDto Company, string SecretApiKey);

public record CompanyDetailsDto(
    Guid Id,
    string Name,
    string Plan,
    string PublicApiKey,
    string DefaultCurrency,
    string TimeZone,
    string? Industry,
    string? LogoUrl,
    string BrandVoice,
    string PrimaryLanguage,
    string? BusinessHoursJson,
    DateTime? OnboardingCompletedAt,
    DateTime CreatedAt);

/// <summary>
/// Partial update covering both the Settings page AND onboarding wizard steps 1-3
/// (details, brand voice, business hours) — they're all just Company fields, so one
/// flexible endpoint serves both call sites instead of near-duplicate ones. Null
/// fields are left unchanged.
/// </summary>
public record UpdateCompanyRequest(
    string? Name,
    string? TimeZone,
    string? DefaultCurrency,
    string? Industry,
    string? LogoUrl,
    string? BrandVoice,
    string? PrimaryLanguage,
    string? BusinessHoursJson);
