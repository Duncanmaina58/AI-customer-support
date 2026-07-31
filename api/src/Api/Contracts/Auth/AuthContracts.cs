namespace Api.Contracts.Auth;

public record LoginRequest(string Email, string Password);

public record LoginResponse(string AccessToken, DateTime ExpiresAtUtc, string RefreshToken, AgentDto Agent);

public record AgentDto(Guid Id, string Name, string Email, string Role, Guid CompanyId, bool IsEmailVerified);

public record RegisterCompanyRequest(
    string CompanyName,
    string OwnerName,
    string OwnerEmail,
    string OwnerPassword);

public record RefreshRequest(string RefreshToken);

public record LogoutRequest(string RefreshToken);

// ---- Auth hardening ---------------------------------------------------

/// <summary>POST /api/auth/verify-email — token comes from the emailed link.</summary>
public record VerifyEmailRequest(string Token);

/// <summary>POST /api/auth/forgot-password — always returns 200 regardless of whether the email exists (no account enumeration).</summary>
public record ForgotPasswordRequest(string Email);

/// <summary>POST /api/auth/reset-password — token comes from the emailed link.</summary>
public record ResetPasswordRequest(string Token, string NewPassword);

/// <summary>POST /api/auth/change-password — requires the current password even though the agent is already authenticated, so a hijacked-but-still-logged-in session can't silently lock out the real owner.</summary>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>A generic, deliberately vague success/failure shape for auth actions that shouldn't leak *why* they failed beyond a human-readable message (password policy violations, expired tokens, etc).</summary>
public record AuthActionResult(bool Success, string Message);
