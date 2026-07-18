using Api.Domain.Entities;
using Api.Infrastructure.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Identity;

public interface IAuthEmailService
{
    Task SendVerificationEmailAsync(Agent agent, string rawToken, CancellationToken ct = default);
    Task SendWelcomeEmailAsync(Agent agent, CancellationToken ct = default);
    Task SendPasswordResetEmailAsync(Agent agent, string rawToken, CancellationToken ct = default);
    Task SendPasswordChangedEmailAsync(Agent agent, CancellationToken ct = default);
    Task SendNewSignInEmailAsync(Agent agent, string ipAddress, CancellationToken ct = default);
    Task SendAccountLockedEmailAsync(Agent agent, TimeSpan lockoutDuration, CancellationToken ct = default);
}

/// <summary>
/// Auth hardening: every account-lifecycle email in one place, each built from
/// the shared AuthEmailTemplates chrome. Failures here are logged but never
/// thrown past this service — a Brevo hiccup shouldn't turn a successful
/// registration/password-change into a 500 for the agent (see each call site
/// in AuthController: the DB write already succeeded by the time these run).
/// </summary>
public class AuthEmailService : IAuthEmailService
{
    private const string SenderName  = "AI Support Platform";
    private const string SenderEmail = "globaljobhubplatform@gmail.com";

    private readonly IBrevoEmailClient _email;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthEmailService> _logger;

    public AuthEmailService(IBrevoEmailClient email, IConfiguration config, ILogger<AuthEmailService> logger)
    {
        _email  = email;
        _config = config;
        _logger = logger;
    }

    private string FrontendBaseUrl => (_config["Widget:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/');

    public Task SendVerificationEmailAsync(Agent agent, string rawToken, CancellationToken ct = default)
    {
        var url = $"{FrontendBaseUrl}/verify-email?token={Uri.EscapeDataString(rawToken)}";

        var body = AuthEmailTemplates.Heading($"Verify your email, {FirstName(agent.Name)}")
            + AuthEmailTemplates.Paragraph("One click and you're fully set up — this confirms it's really you and unlocks email notifications for your account.")
            + AuthEmailTemplates.Button("Verify email address", url)
            + AuthEmailTemplates.LinkFallback(url)
            + AuthEmailTemplates.MetaLine("This link expires in 24 hours.");

        return SendSafelyAsync(new BrevoOutboundEmail(
            SenderName, SenderEmail, agent.Email, agent.Name,
            Subject: "Verify your email address",
            TextContent: $"Hi {FirstName(agent.Name)},\n\nVerify your email by visiting: {url}\n\nThis link expires in 24 hours.",
            HtmlContent: AuthEmailTemplates.Wrap("Verify your email address", body)), ct);
    }

    public Task SendWelcomeEmailAsync(Agent agent, CancellationToken ct = default)
    {
        var dashboardUrl = $"{FrontendBaseUrl}/";

        var body = AuthEmailTemplates.Heading($"You're verified, {FirstName(agent.Name)} 🎉")
            + AuthEmailTemplates.Paragraph("Your email is confirmed and your account is fully active. Here's what's worth doing next if you haven't already:")
            + AuthEmailTemplates.Paragraph("• Connect a channel (WhatsApp, web chat, email, or Telegram)<br/>• Add a few FAQ entries to your knowledge base<br/>• Send yourself a test message from the Sandbox")
            + AuthEmailTemplates.Button("Go to dashboard", dashboardUrl);

        return SendSafelyAsync(new BrevoOutboundEmail(
            SenderName, SenderEmail, agent.Email, agent.Name,
            Subject: "You're all set — welcome aboard",
            TextContent: $"Hi {FirstName(agent.Name)},\n\nYour email is verified and your account is fully active. Visit {dashboardUrl} to get started.",
            HtmlContent: AuthEmailTemplates.Wrap("You're verified — welcome aboard", body)), ct);
    }

    public Task SendPasswordResetEmailAsync(Agent agent, string rawToken, CancellationToken ct = default)
    {
        var url = $"{FrontendBaseUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}";

        var body = AuthEmailTemplates.Heading("Reset your password")
            + AuthEmailTemplates.Paragraph($"We received a request to reset the password for {agent.Email}. Click below to choose a new one.")
            + AuthEmailTemplates.Button("Reset password", url)
            + AuthEmailTemplates.LinkFallback(url)
            + AuthEmailTemplates.WarningBox("This link expires in 1 hour and can only be used once. If you didn't request this, you can safely ignore this email — your password won't change.");

        return SendSafelyAsync(new BrevoOutboundEmail(
            SenderName, SenderEmail, agent.Email, agent.Name,
            Subject: "Reset your password",
            TextContent: $"Reset your password by visiting: {url}\n\nThis link expires in 1 hour. If you didn't request this, ignore this email.",
            HtmlContent: AuthEmailTemplates.Wrap("Reset your password", body)), ct);
    }

    public Task SendPasswordChangedEmailAsync(Agent agent, CancellationToken ct = default)
    {
        var body = AuthEmailTemplates.Heading("Your password was changed")
            + AuthEmailTemplates.Paragraph($"The password for {agent.Email} was just changed. You've been signed out of every other device — sign in again there with your new password.")
            + AuthEmailTemplates.WarningBox("If this wasn't you, your account may be compromised — reset your password immediately and contact support.");

        return SendSafelyAsync(new BrevoOutboundEmail(
            SenderName, SenderEmail, agent.Email, agent.Name,
            Subject: "Your password was changed",
            TextContent: $"The password for {agent.Email} was just changed. If this wasn't you, reset your password immediately.",
            HtmlContent: AuthEmailTemplates.Wrap("Your password was changed", body)), ct);
    }

    public Task SendNewSignInEmailAsync(Agent agent, string ipAddress, CancellationToken ct = default)
    {
        var body = AuthEmailTemplates.Heading("New sign-in to your account")
            + AuthEmailTemplates.Paragraph($"Your account was just signed into from a new location.")
            + AuthEmailTemplates.MetaLine($"IP address: {ipAddress}")
            + AuthEmailTemplates.MetaLine($"Time: {DateTime.UtcNow:dd MMM yyyy, HH:mm} UTC")
            + AuthEmailTemplates.WarningBox("Wasn't you? Change your password right away from Settings > Security.");

        return SendSafelyAsync(new BrevoOutboundEmail(
            SenderName, SenderEmail, agent.Email, agent.Name,
            Subject: "New sign-in to your account",
            TextContent: $"Your account was just signed into from a new location (IP: {ipAddress}) at {DateTime.UtcNow:u}. Wasn't you? Change your password immediately.",
            HtmlContent: AuthEmailTemplates.Wrap("New sign-in to your account", body)), ct);
    }

    public Task SendAccountLockedEmailAsync(Agent agent, TimeSpan lockoutDuration, CancellationToken ct = default)
    {
        var minutes = Math.Ceiling(lockoutDuration.TotalMinutes);

        var body = AuthEmailTemplates.Heading("Your account was temporarily locked")
            + AuthEmailTemplates.Paragraph($"There were too many failed sign-in attempts on {agent.Email}. To protect your account, sign-in has been temporarily disabled.")
            + AuthEmailTemplates.MetaLine($"You can try again in about {minutes:0} minutes.")
            + AuthEmailTemplates.WarningBox("If this wasn't you attempting to sign in, someone may be trying to guess your password — consider resetting it once you're back in.");

        return SendSafelyAsync(new BrevoOutboundEmail(
            SenderName, SenderEmail, agent.Email, agent.Name,
            Subject: "Your account was temporarily locked",
            TextContent: $"Too many failed sign-in attempts on {agent.Email}. Sign-in is disabled for about {minutes:0} minutes. If this wasn't you, consider resetting your password.",
            HtmlContent: AuthEmailTemplates.Wrap("Your account was temporarily locked", body)), ct);
    }

    private async Task SendSafelyAsync(BrevoOutboundEmail email, CancellationToken ct)
    {
        try
        {
            await _email.SendAsync(email, ct);
        }
        catch (Exception ex)
        {
            // Never let an email provider hiccup fail the auth action that triggered
            // it — the account state change (registered, verified, password reset)
            // already succeeded in the database by the time this runs.
            _logger.LogError(ex, "Auth email send failed | to={To} subject={Subject}", email.ToEmail, email.Subject);
        }
    }

    private static string FirstName(string fullName) =>
        string.IsNullOrWhiteSpace(fullName) ? "there" : fullName.Split(' ', 2)[0];
}
