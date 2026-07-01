namespace Api.Contracts.Auth;

public record LoginRequest(string Email, string Password);

public record LoginResponse(string AccessToken, DateTime ExpiresAtUtc, string RefreshToken, AgentDto Agent);

public record AgentDto(Guid Id, string Name, string Email, string Role, Guid CompanyId);

public record RegisterCompanyRequest(
    string CompanyName,
    string OwnerName,
    string OwnerEmail,
    string OwnerPassword);

public record RefreshRequest(string RefreshToken);

public record LogoutRequest(string RefreshToken);
