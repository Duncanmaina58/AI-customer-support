namespace Api.Infrastructure.Security;

public record PasswordPolicyResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static readonly PasswordPolicyResult Valid = new(true, []);
    public static PasswordPolicyResult Invalid(params string[] errors) => new(false, errors);
}

/// <summary>
/// Auth hardening: the server-side, authoritative password strength check —
/// the frontend's PasswordStrengthMeter gives live feedback as someone types,
/// but this is what actually gets enforced (register, reset-password, and
/// change-password all call this; never trust client-side validation alone).
///
/// Policy, roughly aligned with NIST SP 800-63B (length over forced complexity
/// theatre) plus a a pragmatic minimum-variety check most SaaS products still
/// expect:
///   - at least 10 characters (NIST's floor is 8; 10 is a common, slightly
///     stricter SaaS default without being annoying)
///   - at least 3 of the 4 character classes (upper/lower/digit/symbol) —
///     not all 4, so a strong 14-character passphrase without a symbol still
///     passes, but "alllowercase" or "12345678900" alone don't
///   - not on the common/breached-password denylist below
///   - doesn't contain the account's own email local-part or name (stops the
///     classic "Password123" + reusing your own name/company trivially)
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 10;

    /// <summary>
    /// The most commonly breached/guessed passwords (from public breach-corpus
    /// frequency analyses, e.g. Have I Been Pwned's published top lists) that
    /// are otherwise long/varied enough to slip past the rules above —
    /// "Password123!" satisfies every rule above except this one. Deliberately
    /// short: this is a targeted blocklist for the handful of passwords that
    /// are famous for satisfying naive complexity rules, not a full breach
    /// corpus (there's no bundled multi-million-row list, and no network
    /// access here to check a live service like HIBP's Pwned Passwords API —
    /// see the recommendation in docs/security-audit.md to wire that in later).
    /// </summary>
    private static readonly HashSet<string> CommonPasswordDenylist = new(StringComparer.OrdinalIgnoreCase)
    {
        "password123", "password1234", "password123!", "Password123", "Password123!",
        "qwerty123456", "qwertyuiop12", "1234567890", "123456789012", "letmein12345",
        "welcome12345", "admin1234567", "iloveyou1234", "sunshine1234", "princess1234",
        "football1234", "monkey123456", "dragon123456", "master1234567", "trustno1234",
        "abc123456789", "changeme1234", "passw0rd1234", "p@ssw0rd1234", "companyname1",
    };

    public static PasswordPolicyResult Validate(string password, string email, string? name = null)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            errors.Add($"Password must be at least {MinLength} characters long.");

        var hasUpper  = password.Any(char.IsUpper);
        var hasLower  = password.Any(char.IsLower);
        var hasDigit  = password.Any(char.IsDigit);
        var hasSymbol = password.Any(c => !char.IsLetterOrDigit(c));
        var varietyScore = new[] { hasUpper, hasLower, hasDigit, hasSymbol }.Count(x => x);

        if (varietyScore < 3)
            errors.Add("Password must include at least 3 of: uppercase letters, lowercase letters, numbers, symbols.");

        if (CommonPasswordDenylist.Contains(password))
            errors.Add("This password is too common and appears in known breach lists — please choose another.");

        var emailLocalPart = email.Split('@')[0];
        if (!string.IsNullOrEmpty(emailLocalPart) && password.Contains(emailLocalPart, StringComparison.OrdinalIgnoreCase))
            errors.Add("Password can't contain your email address.");

        if (!string.IsNullOrWhiteSpace(name))
        {
            // Check each name part individually (not just the full "First Last"
            // string) so "Amina2024!" or "Yusuf123456" are caught too, not just
            // a password containing the literal two-word name with a space.
            var nameParts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (nameParts.Any(part => part.Length >= 3 && password.Contains(part, StringComparison.OrdinalIgnoreCase)))
                errors.Add("Password can't contain your name.");
        }

        return errors.Count == 0 ? PasswordPolicyResult.Valid : new PasswordPolicyResult(false, errors);
    }
}
