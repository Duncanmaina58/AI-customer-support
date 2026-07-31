namespace Api.Contracts.Agents;

public record AgentListItemDto(
    Guid Id,
    string Name,
    string Email,
    string Role,
    bool IsActive,
    DateTime? LastActiveAt,
    DateTime CreatedAt);

/// <summary>
/// Role is restricted to "Admin" or "Agent" here on purpose — ownership transfer
/// (promoting someone else to Owner) is a separate, more sensitive flow that isn't
/// built yet, so invites can never create a second Owner.
/// </summary>
public record InviteAgentRequest(string Name, string Email, string Role);

/// <summary>
/// TemporaryPassword is returned exactly once, at creation time, since there's no
/// email service wired up yet to deliver an invite link. The inviting admin is
/// responsible for sharing it with the new agent out-of-band. It is never stored
/// in plaintext and can never be retrieved again after this response.
/// </summary>
public record InviteAgentResponse(AgentListItemDto Agent, string TemporaryPassword);

/// <summary>
/// Partial update — null fields are left unchanged. Role must be "Admin" or "Agent";
/// changing IsActive/Role on yourself or on the company's last remaining Owner is
/// rejected by AgentsController (see the business rules there).
/// </summary>
public record UpdateAgentRequest(string? Role, bool? IsActive);
