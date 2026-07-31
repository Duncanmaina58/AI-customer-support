using Microsoft.AspNetCore.DataProtection;

namespace Api.Infrastructure.Security;

/// <summary>
/// Sprint 9 Action Engine: encrypts/decrypts two things that must be
/// reversible (unlike a password hash) — the webhook HMAC signing secret
/// (WebhookActionRunner needs the raw value back on every call to compute a
/// signature) and ActionLog's extracted parameters (flagged as "may be PII"
/// in the spec, e.g. order numbers, account numbers).
///
/// Same mechanism as ChannelCredentialProtector (ASP.NET Core Data Protection,
/// AES-256-CBC + HMAC-SHA256, no extra package), but with its own Purpose
/// string — a different, cryptographically independent derived key from the
/// same key ring, so this protector's ciphertexts can never be decrypted by
/// ChannelCredentialProtector or vice versa, even though both ultimately sit
/// on the same underlying key store. See ChannelCredentialProtector's own doc
/// comment for the production key-persistence note (applies here too).
/// </summary>
public interface IActionEngineSecretProtector
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}

public class ActionEngineSecretProtector : IActionEngineSecretProtector
{
    private const string Purpose = "Api.Infrastructure.ActionEngine.v1";

    private readonly IDataProtector _protector;

    public ActionEngineSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Encrypt(string plaintext) => _protector.Protect(plaintext);

    public string Decrypt(string ciphertext) => _protector.Unprotect(ciphertext);
}
