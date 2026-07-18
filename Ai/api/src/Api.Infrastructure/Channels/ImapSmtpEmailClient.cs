using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Api.Infrastructure.Channels;

/// <summary>
/// Sprint 5 follow-up: credentials for the IMAP/SMTP email mode — a free
/// alternative to Brevo's inbound parsing webhook, which requires a paid Brevo
/// plan. Works with any regular mailbox (Gmail, Outlook/Office365, Zoho, cPanel
/// hosting, etc.) that exposes IMAP + SMTP, which almost all do even on free
/// tiers. Stored AES-encrypted via IChannelCredentialProtector, same as
/// EmailChannelCredentials and WhatsAppCredentials.
/// </summary>
public record ImapChannelCredentials(
    string ImapHost,
    int    ImapPort,
    string SmtpHost,
    int    SmtpPort,
    string Username,
    string Password,
    string SenderName);

public record ImapVerifyResult(bool Success, string? ErrorMessage = null);

/// <summary>
/// Thin MailKit wrapper: verifies IMAP+SMTP credentials actually work (called
/// once, when connecting the channel — mirrors WhatsAppClient.VerifyAsync so a
/// typo'd password fails fast with a clear message instead of silently breaking
/// the polling background service later), and sends outbound mail over SMTP.
/// Inbound *polling* itself lives in ImapPollingService, not here — this class
/// only owns the two things that talk to a mail server: verify and send.
/// </summary>
public interface IImapSmtpEmailClient
{
    Task<ImapVerifyResult> VerifyAsync(ImapChannelCredentials credentials, CancellationToken ct = default);
    Task SendAsync(BrevoOutboundEmail email, ImapChannelCredentials credentials, CancellationToken ct = default);
}

public class ImapSmtpEmailClient : IImapSmtpEmailClient
{
    private readonly ILogger<ImapSmtpEmailClient> _logger;

    public ImapSmtpEmailClient(ILogger<ImapSmtpEmailClient> logger)
    {
        _logger = logger;
    }

    public async Task<ImapVerifyResult> VerifyAsync(ImapChannelCredentials credentials, CancellationToken ct = default)
    {
        try
        {
            using var imap = new ImapClient();
            await imap.ConnectAsync(credentials.ImapHost, credentials.ImapPort, SecureSocketOptions.Auto, ct);
            await imap.AuthenticateAsync(credentials.Username, credentials.Password, ct);
            await imap.DisconnectAsync(true, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IMAP verification failed | host={Host}", credentials.ImapHost);
            return new ImapVerifyResult(false, $"Couldn't connect to IMAP server: {ex.Message}");
        }

        try
        {
            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(credentials.SmtpHost, credentials.SmtpPort, SecureSocketOptions.Auto, ct);
            await smtp.AuthenticateAsync(credentials.Username, credentials.Password, ct);
            await smtp.DisconnectAsync(true, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTP verification failed | host={Host}", credentials.SmtpHost);
            return new ImapVerifyResult(false, $"IMAP connected fine, but SMTP failed: {ex.Message}");
        }

        return new ImapVerifyResult(true);
    }

    public async Task SendAsync(BrevoOutboundEmail email, ImapChannelCredentials credentials, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(email.SenderName, email.SenderEmail));
        message.To.Add(new MailboxAddress(email.ToName ?? email.ToEmail, email.ToEmail));
        message.Subject = email.Subject;

        if (email.ReplyToEmail is not null)
        {
            message.ReplyTo.Add(new MailboxAddress(email.SenderName, email.ReplyToEmail));
        }

        // MimeKit expects Message-Id-style headers wrapped in angle brackets.
        if (email.InReplyTo is not null)
        {
            message.InReplyTo = Wrap(email.InReplyTo);
        }
        if (email.References is not null)
        {
            foreach (var reference in email.References.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                message.References.Add(Wrap(reference));
            }
        }

        message.Body = new TextPart("plain") { Text = email.TextContent };

        using var smtp = new SmtpClient();
        try
        {
            await smtp.ConnectAsync(credentials.SmtpHost, credentials.SmtpPort, SecureSocketOptions.Auto, ct);
            await smtp.AuthenticateAsync(credentials.Username, credentials.Password, ct);
            await smtp.SendAsync(message, ct);
        }
        finally
        {
            if (smtp.IsConnected)
            {
                await smtp.DisconnectAsync(true, ct);
            }
        }
    }

    private static string Wrap(string messageId) =>
        messageId.StartsWith('<') ? messageId : $"<{messageId}>";
}

/// <summary>
/// Sprint 5 follow-up: polls an IMAP inbox for unread mail on an interval and
/// feeds each one through EmailPipelineService — the free alternative to Brevo's
/// inbound webhook (see ImapChannelCredentials' doc comment for why this exists).
///
/// Design choices, and why:
///   - Polls "NotSeen" messages rather than tracking last-seen UID. UID-range
///     tracking is more precise but breaks if the mailbox's UIDVALIDITY ever
///     changes (folder recreated, some providers rotate it); \Seen is simpler,
///     self-correcting, and fine as long as this mailbox is dedicated to the
///     integration (not also read by a human in a normal mail client, which
///     would mark messages seen before this service processes them).
///   - Runs against *every* company's Imap-mode Email connection in one loop
///     rather than one BackgroundService per company — much cheaper at scale,
///     and companies rarely have more than one Email connection.
///   - A failure on one company's mailbox (bad password, server down) is caught
///     and logged per-connection, not allowed to kill the polling loop for
///     everyone else.
/// </summary>
public class ImapMailFetcher
{
    private readonly ILogger<ImapMailFetcher> _logger;

    public ImapMailFetcher(ILogger<ImapMailFetcher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Fetches unread mail received on/after <paramref name="sinceUtc"/>.
    ///
    /// Sprint 5 follow-up fix: a real mailbox almost always has a pile of old
    /// unread mail (newsletters, notifications, mail from a year ago nobody got
    /// to) — searching purely on \Seen would pull in that entire backlog as
    /// "new" conversations the moment a company connects. The date cutoff
    /// avoids that: only mail from after the channel was connected (or
    /// reconnected — see ChannelsController.ConnectEmail) is ever processed.
    ///
    /// IMAP's SEARCH SINCE is date-only (no time component), so it's used here
    /// as a coarse, cheap server-side filter with a 1-day safety margin, and the
    /// exact cutoff is enforced afterwards in C# against each message's real
    /// Date header. Messages that clear the coarse IMAP filter but fail the
    /// precise one are still marked \Seen — they're being deliberately excluded
    /// as "too old", not skipped for now, so there's no reason to keep refetching
    /// them on every future poll.
    /// </summary>
    public async Task<IReadOnlyList<FetchedEmail>> FetchUnseenAsync(
        ImapChannelCredentials credentials, DateTime sinceUtc, CancellationToken ct = default)
    {
        var results = new List<FetchedEmail>();

        using var client = new ImapClient();
        await client.ConnectAsync(credentials.ImapHost, credentials.ImapPort, SecureSocketOptions.Auto, ct);
        await client.AuthenticateAsync(credentials.Username, credentials.Password, ct);

        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite, ct);

        var coarseCutoffDate = sinceUtc.Date.AddDays(-1); // safety margin for SINCE's date-only granularity
        var query = SearchQuery.NotSeen.And(SearchQuery.DeliveredAfter(coarseCutoffDate));
        var uids = await inbox.SearchAsync(query, ct);

        foreach (var uid in uids)
        {
            MimeMessage message;
            try
            {
                message = await inbox.GetMessageAsync(uid, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch IMAP message uid={Uid}", uid);
                continue;
            }

            // Precise cutoff — IMAP's SINCE above only got us to "the right day".
            if (message.Date.UtcDateTime < sinceUtc)
            {
                await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, ct);
                continue;
            }

            var from = message.From.Mailboxes.FirstOrDefault();
            if (from is null)
            {
                await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, ct);
                continue;
            }

            var text = ExtractPlainText(message);
            if (!string.IsNullOrWhiteSpace(text))
            {
                results.Add(new FetchedEmail(
                    FromEmail:  from.Address,
                    FromName:   string.IsNullOrWhiteSpace(from.Name) ? null : from.Name,
                    Subject:    message.Subject ?? "(no subject)",
                    MessageId:  message.MessageId ?? $"generated-{Guid.NewGuid():N}@platform",
                    InReplyTo:  message.InReplyTo,
                    BodyText:   text));
            }

            // Mark seen regardless of whether we could extract usable text —
            // an empty/attachment-only email shouldn't be retried forever.
            await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, ct);
        }

        await client.DisconnectAsync(true, ct);
        return results;
    }

    private static string? ExtractPlainText(MimeMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            return message.TextBody.Trim();
        }

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            // Crude HTML→text fallback for providers that only send text/html —
            // good enough for a customer support message, not a general-purpose
            // HTML renderer.
            var stripped = System.Text.RegularExpressions.Regex.Replace(message.HtmlBody, "<[^>]+>", " ");
            return System.Net.WebUtility.HtmlDecode(stripped).Trim();
        }

        return null;
    }
}

public record FetchedEmail(
    string  FromEmail,
    string? FromName,
    string  Subject,
    string  MessageId,
    string? InReplyTo,
    string  BodyText);
