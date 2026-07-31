using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Api.Infrastructure.Security;

namespace Api.Filters;

/// <summary>
/// Sprint 8 security hardening: verifies that an inbound WhatsApp/Messenger
/// webhook POST really came from Meta, by recomputing the HMAC-SHA256 of the
/// raw request body (using the platform's single shared Meta App Secret —
/// this SaaS uses one Meta App for every company's WhatsApp/Messenger
/// connection, so the secret is platform-wide config, not per-connection) and
/// comparing it to the X-Hub-Signature-256 header Meta sends on every call.
///
/// Runs as an IAsyncResourceFilter — BEFORE model binding — because it needs
/// the exact raw bytes of the body; by the time an action filter or the action
/// method itself runs, [FromBody] has already parsed (and effectively
/// consumed) the stream into an object, and re-serializing that object would
/// not reliably reproduce Meta's exact byte-for-byte payload.
///
/// Fails OPEN (logs a loud warning, lets the request through) only when
/// Meta:AppSecret genuinely isn't configured — this keeps local/dev setup
/// working without it. Once it IS configured, a missing/invalid signature is
/// always rejected with 401. **Meta:AppSecret must be set in production
/// before onboarding real pilot clients** — see docs/security-audit.md.
/// </summary>
public class VerifyMetaSignatureAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var config   = services.GetRequiredService<IConfiguration>();
        var logger   = services.GetRequiredService<ILoggerFactory>().CreateLogger("VerifyMetaSignature");
        var request  = context.HttpContext.Request;

        var appSecret = config["Meta:AppSecret"];
        if (string.IsNullOrEmpty(appSecret))
        {
            logger.LogWarning(
                "Meta:AppSecret is not configured — accepting webhook request UNVERIFIED at {Path}. " +
                "Set Meta:AppSecret before onboarding real clients (see docs/security-audit.md).",
                request.Path);
            await next();
            return;
        }

        request.EnableBuffering();

        string rawBody;
        using (var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync();
        }
        request.Body.Position = 0; // rewind so [FromBody] model binding can still read it

        var signatureHeader = request.Headers["X-Hub-Signature-256"].ToString();
        if (!MetaWebhookSignature.Verify(rawBody, signatureHeader, appSecret))
        {
            logger.LogWarning("Meta webhook: missing or invalid X-Hub-Signature-256 — rejecting at {Path}", request.Path);
            context.Result = new UnauthorizedResult();
            return;
        }

        await next();
    }
}
