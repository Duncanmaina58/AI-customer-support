using Api.Infrastructure.Security;

namespace Api.Tests;

/// <summary>
/// Unit tests for MetaWebhookSignature — the Sprint 8 security-hardening
/// utility that verifies WhatsApp/Messenger webhook authenticity. Also covers
/// FixedTimeEquals, which the Telegram webhook's secret_token check reuses.
/// </summary>
public class MetaWebhookSignatureTests
{
    private const string AppSecret = "test-app-secret";

    [Fact]
    public void Verify_accepts_a_correctly_computed_signature()
    {
        const string body = """{"object":"whatsapp_business_account","entry":[]}""";
        var validSignature = "sha256=" + MetaWebhookSignature.Compute(body, AppSecret);

        Assert.True(MetaWebhookSignature.Verify(body, validSignature, AppSecret));
    }

    [Fact]
    public void Verify_rejects_a_tampered_body()
    {
        const string originalBody = """{"object":"whatsapp_business_account","entry":[]}""";
        const string tamperedBody = """{"object":"whatsapp_business_account","entry":["injected"]}""";
        var signatureForOriginal = "sha256=" + MetaWebhookSignature.Compute(originalBody, AppSecret);

        Assert.False(MetaWebhookSignature.Verify(tamperedBody, signatureForOriginal, AppSecret));
    }

    [Fact]
    public void Verify_rejects_signature_computed_with_wrong_secret()
    {
        const string body = """{"object":"whatsapp_business_account","entry":[]}""";
        var signatureFromWrongSecret = "sha256=" + MetaWebhookSignature.Compute(body, "not-the-real-secret");

        Assert.False(MetaWebhookSignature.Verify(body, signatureFromWrongSecret, AppSecret));
    }

    [Fact]
    public void Verify_rejects_missing_header()
    {
        Assert.False(MetaWebhookSignature.Verify("{}", null, AppSecret));
        Assert.False(MetaWebhookSignature.Verify("{}", "", AppSecret));
    }

    [Fact]
    public void Verify_rejects_header_without_sha256_prefix()
    {
        var hex = MetaWebhookSignature.Compute("{}", AppSecret);
        Assert.False(MetaWebhookSignature.Verify("{}", hex, AppSecret)); // missing "sha256=" prefix
    }

    [Fact]
    public void Compute_is_deterministic()
    {
        var a = MetaWebhookSignature.Compute("same body", AppSecret);
        var b = MetaWebhookSignature.Compute("same body", AppSecret);
        Assert.Equal(a, b);
    }

    [Fact]
    public void FixedTimeEquals_true_for_identical_strings()
    {
        Assert.True(MetaWebhookSignature.FixedTimeEquals("abc123", "abc123"));
    }

    [Fact]
    public void FixedTimeEquals_false_for_different_strings()
    {
        Assert.False(MetaWebhookSignature.FixedTimeEquals("abc123", "abc124"));
    }

    [Fact]
    public void FixedTimeEquals_false_for_different_lengths()
    {
        Assert.False(MetaWebhookSignature.FixedTimeEquals("short", "much-longer-string"));
    }
}
