using Api.Domain.Common;
using Api.Domain.Enums;

namespace Api.Domain.Entities;

/// <summary>
/// One row per channel a Company has connected (WhatsApp, Web Chat, Email, ...).
/// CredentialsEncrypted holds whatever that channel needs (Meta access token + phone
/// number id for WhatsApp, IMAP/SMTP creds for Email, etc.) as an encrypted JSON blob -
/// see ChannelCredentialProtector in Infrastructure for the actual encrypt/decrypt.
/// Never log or return CredentialsEncrypted verbatim; always go through the protector.
/// </summary>
public class ChannelConnection : AuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public ChannelType Channel { get; set; }
    public ChannelConnectionStatus Status { get; set; } = ChannelConnectionStatus.Active;

    /// <summary>AES-encrypted JSON blob (via ASP.NET Core Data Protection). Shape varies by Channel.</summary>
    public string CredentialsEncrypted { get; set; } = string.Empty;

    /// <summary>Non-secret extras (e.g. WhatsApp display phone number) - safe to show in the UI as-is.</summary>
    public string? MetadataJson { get; set; }

    public DateTime? LastVerifiedAt { get; set; }
}
