using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.Security;

namespace Api.Tests;

/// <summary>
/// Unit tests for the auth-hardening rebuild's pure-logic pieces — password
/// policy and AgentSecurityToken.IsActive. AuthController itself is
/// integration-level (real DB + email provider) and isn't covered here, same
/// rationale as every other controller in this codebase's test suite.
/// </summary>
public class AuthHardeningTests
{
    private const string Email = "amina@acme.co.ke";
    private const string Name = "Amina Yusuf";

    // -------------------------------------------------------------------
    // PasswordPolicy
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("Sh0rt!")]        // too short
    [InlineData("short")]         // too short, no variety either
    public void Rejects_passwords_below_minimum_length(string password)
    {
        var result = PasswordPolicy.Validate(password, Email, Name);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("10 characters"));
    }

    [Fact]
    public void Rejects_password_with_insufficient_character_variety()
    {
        // 12 lowercase letters only - long enough, but only 1 of 4 classes.
        var result = PasswordPolicy.Validate("abcdefghijkl", Email, Name);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("at least 3 of"));
    }

    [Fact]
    public void Accepts_a_password_with_exactly_three_character_classes()
    {
        // Upper + lower + digit, no symbol - should be enough (3 of 4).
        var result = PasswordPolicy.Validate("CorrectHorse99", Email, Name);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Rejects_common_denylisted_passwords_even_if_otherwise_compliant()
    {
        // Satisfies length + variety rules, but is on the denylist.
        var result = PasswordPolicy.Validate("Password123!", Email, Name);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("too common"));
    }

    [Fact]
    public void Rejects_password_containing_the_email_local_part()
    {
        var result = PasswordPolicy.Validate("amina123456!", Email, Name);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("email"));
    }

    [Fact]
    public void Rejects_password_containing_the_users_name()
    {
        var result = PasswordPolicy.Validate("AminaYusuf123!", Email, Name);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("name"));
    }

    [Fact]
    public void Accepts_a_genuinely_strong_password()
    {
        var result = PasswordPolicy.Validate("Tr0ubad0ur&Ferns!", Email, Name);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Reports_multiple_violations_at_once()
    {
        var result = PasswordPolicy.Validate("short", Email, Name);
        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 2); // too short AND insufficient variety
    }

    // -------------------------------------------------------------------
    // AgentSecurityToken.IsActive
    // -------------------------------------------------------------------

    [Fact]
    public void SecurityToken_is_active_when_unused_and_unexpired()
    {
        var token = new AgentSecurityToken
        {
            AgentId = Guid.NewGuid(),
            Type = AgentSecurityTokenType.EmailVerification,
            TokenHash = "hash",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
        };

        Assert.True(token.IsActive);
    }

    [Fact]
    public void SecurityToken_is_inactive_once_used()
    {
        var token = new AgentSecurityToken
        {
            AgentId = Guid.NewGuid(),
            Type = AgentSecurityTokenType.PasswordReset,
            TokenHash = "hash",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            UsedAtUtc = DateTime.UtcNow,
        };

        Assert.False(token.IsActive);
    }

    [Fact]
    public void SecurityToken_is_inactive_once_expired()
    {
        var token = new AgentSecurityToken
        {
            AgentId = Guid.NewGuid(),
            Type = AgentSecurityTokenType.PasswordReset,
            TokenHash = "hash",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1),
        };

        Assert.False(token.IsActive);
    }
}
