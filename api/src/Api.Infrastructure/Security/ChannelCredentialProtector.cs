using Microsoft.AspNetCore.DataProtection;

namespace Api.Infrastructure.Security;

/// <summary>
/// Encrypts/decrypts channel credentials (WhatsApp access tokens, email passwords,
/// etc.) before they touch the database, using ASP.NET Core's built-in Data
/// Protection API - AES-256-CBC + HMAC-SHA256 by default, no extra NuGet package
/// needed since it ships in the Microsoft.AspNetCore.App shared framework.
///
/// Key storage: by default, Data Protection persists its keys to the local
/// filesystem (%LOCALAPPDATA%\ASP.NET\DataProtection-Keys on Windows). That's fine
/// for a single dev machine, but it means encrypted credentials become
/// undecryptable if you move/redeploy without also moving the key ring - before
/// production, configure persistent key storage (e.g. PersistKeysToDbContext, or a
/// blob/file share) via builder.Services.AddDataProtection() in Program.cs.
/// </summary>
public interface IChannelCredentialProtector
{
    string Encrypt(string plaintextJson);
    string Decrypt(string ciphertext);
}

public class ChannelCredentialProtector : IChannelCredentialProtector
{
    // Purpose string scopes this protector so its keys can never decrypt data
    // encrypted for an unrelated purpose elsewhere in the app, even though they
    // share the same underlying key ring.
    private const string Purpose = "Api.Infrastructure.ChannelCredentials.v1";

    private readonly IDataProtector _protector;

    public ChannelCredentialProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Encrypt(string plaintextJson) => _protector.Protect(plaintextJson);

    public string Decrypt(string ciphertext) => _protector.Unprotect(ciphertext);
}
