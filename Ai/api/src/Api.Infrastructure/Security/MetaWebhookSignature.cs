using System.Security.Cryptography;
using System.Text;

namespace Api.Infrastructure.Security;

/// <summary>
/// Sprint 8 security hardening: pure HMAC-SHA256 computation/comparison logic
/// for Meta's X-Hub-Signature-256 webhook verification, split out from
/// Api/Filters/VerifyMetaSignatureAttribute.cs specifically so it's unit
/// testable without spinning up an ASP.NET Core request pipeline — the filter
/// itself is a thin wrapper that reads the raw body/header and delegates here.
/// </summary>
public static class MetaWebhookSignature
{
    /// <summary>
    /// Verifies that <paramref name="signatureHeader"/> (the raw
    /// X-Hub-Signature-256 header value, e.g. "sha256=abcd...") matches the
    /// HMAC-SHA256 of <paramref name="rawBody"/> computed with <paramref name="appSecret"/>.
    /// </summary>
    public static bool Verify(string rawBody, string? signatureHeader, string appSecret)
    {
        if (string.IsNullOrEmpty(signatureHeader) || !signatureHeader.StartsWith("sha256=", StringComparison.Ordinal))
            return false;

        var providedHex = signatureHeader["sha256=".Length..];
        var computedHex = Compute(rawBody, appSecret);

        return FixedTimeEquals(providedHex, computedHex);
    }

    /// <summary>Returns the lowercase hex HMAC-SHA256 of <paramref name="rawBody"/> using <paramref name="appSecret"/> as the key.</summary>
    public static string Compute(string rawBody, string appSecret)
    {
        var bytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), Encoding.UTF8.GetBytes(rawBody));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Constant-time string comparison — avoids leaking the correct signature via response-timing side channels.</summary>
    public static bool FixedTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return aBytes.Length == bBytes.Length && CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
