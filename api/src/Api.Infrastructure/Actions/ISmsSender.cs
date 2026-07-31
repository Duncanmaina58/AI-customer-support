using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Actions;

public sealed record SmsSendResult(bool Success, string? ErrorMessage = null);

/// <summary>
/// Sprint 9 Action Engine: abstracts "send an SMS" so OtpService doesn't know
/// or care which provider delivers it. Per the Phase 2 build plan, the real
/// Africa's Talking integration is Sprint 10's job (it's grouped with M-Pesa
/// under "Sprint 10 — OTP + M-Pesa" specifically) — this interface and its
/// placeholder implementation exist now so Sprint 10 only has to write one
/// class (an AfricasTalkingSmsSender implementing this interface + a DI
/// registration swap) rather than also design the OTP state machine.
/// </summary>
public interface ISmsSender
{
    Task<SmsSendResult> SendAsync(string phoneNumber, string message, CancellationToken ct = default);
}

/// <summary>
/// Sprint 9 placeholder — logs the OTP instead of delivering it, so the whole
/// verification flow (send -> customer replies -> verify -> action executes)
/// is genuinely testable end-to-end before Sprint 10 wires in a real provider.
///
/// Logged at Warning (not Debug/Information) specifically so it's impossible
/// to miss in any log viewer, and the message is unambiguous about why: this
/// must never be mistaken for normal behaviour, and must never ship to a real
/// pilot client's production traffic without Sprint 10's real sender replacing
/// this registration first (see DependencyInjection.cs's registration comment).
/// </summary>
public class LoggingSmsSender : ISmsSender
{
    private readonly ILogger<LoggingSmsSender> _logger;

    public LoggingSmsSender(ILogger<LoggingSmsSender> logger)
    {
        _logger = logger;
    }

    public Task<SmsSendResult> SendAsync(string phoneNumber, string message, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "🔧 SMS provider not yet configured (Sprint 10 wires in Africa's Talking) — " +
            "would have sent to {PhoneNumber}: {Message}",
            phoneNumber, message);

        return Task.FromResult(new SmsSendResult(true));
    }
}
