namespace Api.Infrastructure.Identity;

/// <summary>
/// Auth hardening: a single, small HTML wrapper every account-security email
/// uses, so verification/reset/welcome/alert emails look like they came from
/// one coherent product instead of five different plain-text notices. Kept
/// deliberately simple (inline styles, table-free, no external assets) —
/// email HTML rendering is notoriously inconsistent across clients, and
/// inline-styled divs/paragraphs are the most reliably-rendered subset across
/// Gmail, Outlook, and mobile mail apps alike.
///
/// Palette matches web/src/index.css's dark theme (ink/teal/mint/coral) so the
/// email doesn't feel like a different product from the dashboard it's about.
/// </summary>
public static class AuthEmailTemplates
{
    private const string InkBg      = "#0f172a";
    private const string CardBg     = "#1e293b";
    private const string BorderCol  = "#334155";
    private const string TealAccent = "#0d9488";
    private const string MintText   = "#5eead4";
    private const string LineText   = "#e2e8f0";
    private const string MutedText  = "#94a3b8";
    private const string CoralText  = "#f43f5e";

    /// <summary>Wraps a body of pre-built HTML (paragraphs, a button, whatever) in the shared card/header/footer chrome.</summary>
    public static string Wrap(string preheader, string bodyHtml, string? footerNote = null) => $"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1.0"></head>
        <body style="margin:0;padding:0;background-color:{InkBg};font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
          <span style="display:none;font-size:1px;color:{InkBg};line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;">{preheader}</span>
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:{InkBg};padding:32px 16px;">
            <tr><td align="center">
              <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="max-width:480px;width:100%;">
                <tr><td style="padding-bottom:24px;text-align:center;">
                  <span style="display:inline-block;width:32px;height:32px;border-radius:9999px;background-color:{TealAccent};"></span>
                  <div style="margin-top:8px;color:{LineText};font-size:14px;font-weight:600;">AI Support Platform</div>
                </td></tr>
                <tr><td style="background-color:{CardBg};border:1px solid {BorderCol};border-radius:12px;padding:32px 28px;">
                  {bodyHtml}
                </td></tr>
                <tr><td style="padding-top:20px;text-align:center;color:{MutedText};font-size:12px;line-height:18px;">
                  {footerNote ?? "You're receiving this because it relates to your AI Support Platform account."}
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;

    public static string Heading(string text) =>
        $"""<h1 style="margin:0 0 16px;color:{LineText};font-size:18px;font-weight:600;">{text}</h1>""";

    public static string Paragraph(string text) =>
        $"""<p style="margin:0 0 16px;color:{MutedText};font-size:14px;line-height:22px;">{text}</p>""";

    public static string Button(string text, string url) => $"""
        <table role="presentation" cellpadding="0" cellspacing="0" style="margin:24px 0;">
          <tr><td style="border-radius:8px;background-color:{TealAccent};">
            <a href="{url}" style="display:inline-block;padding:11px 24px;color:#ffffff;font-size:14px;font-weight:600;text-decoration:none;border-radius:8px;">{text}</a>
          </td></tr>
        </table>
        """;

    public static string LinkFallback(string url) => $"""
        <p style="margin:0 0 16px;color:{MutedText};font-size:12px;line-height:20px;word-break:break-all;">
          Or copy and paste this link: <a href="{url}" style="color:{MintText};">{url}</a>
        </p>
        """;

    public static string WarningBox(string text) => $"""
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:8px 0 16px;">
          <tr><td style="border:1px solid {CoralText}66;background-color:{CoralText}1a;border-radius:8px;padding:12px 14px;color:{CoralText};font-size:13px;line-height:19px;">
            {text}
          </td></tr>
        </table>
        """;

    public static string MetaLine(string text) =>
        $"""<p style="margin:0 0 4px;color:{MutedText};font-size:12px;line-height:18px;">{text}</p>""";
}
