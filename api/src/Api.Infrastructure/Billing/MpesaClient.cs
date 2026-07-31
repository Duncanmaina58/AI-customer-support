using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Billing;

public record StkPushResult(bool Success, string? CheckoutRequestId, string? MerchantRequestId, string? ErrorMessage);

/// <summary>
/// Sprint 7: Safaricom Daraja API client — OAuth2 (client-credentials, Basic
/// Auth) then STK Push. Pure REST via a plain HttpClient, no library needed,
/// per the Phase 1 doc.
///
/// Sandbox by default (developer.safaricom.co.ke — free registration, no real
/// money moves). Switch Mpesa:Environment to "production" once a company has
/// real Daraja production credentials from Safaricom.
/// </summary>
public interface IMpesaClient
{
    Task<StkPushResult> InitiateStkPushAsync(
        string phoneNumber, decimal amountKes, string accountReference, string transactionDescription,
        CancellationToken ct = default);
}

public class MpesaClient : IMpesaClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MpesaClient> _logger;

    public MpesaClient(HttpClient httpClient, IConfiguration configuration, ILogger<MpesaClient> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    private string BaseUrl =>
        _configuration["Mpesa:Environment"] == "production"
            ? "https://api.safaricom.co.ke"
            : "https://sandbox.safaricom.co.ke";

    public async Task<StkPushResult> InitiateStkPushAsync(
        string phoneNumber, decimal amountKes, string accountReference, string transactionDescription,
        CancellationToken ct = default)
    {
        var consumerKey    = _configuration["Mpesa:ConsumerKey"];
        var consumerSecret = _configuration["Mpesa:ConsumerSecret"];
        var shortcode      = _configuration["Mpesa:Shortcode"];
        var passkey        = _configuration["Mpesa:Passkey"];
        var callbackUrl    = _configuration["Mpesa:CallbackUrl"];

        if (string.IsNullOrEmpty(consumerKey) || string.IsNullOrEmpty(consumerSecret)
            || string.IsNullOrEmpty(shortcode) || string.IsNullOrEmpty(passkey) || string.IsNullOrEmpty(callbackUrl))
        {
            return new StkPushResult(false, null, null,
                "M-Pesa isn't configured on this server yet (Mpesa:ConsumerKey/ConsumerSecret/Shortcode/Passkey/CallbackUrl).");
        }

        string accessToken;
        try
        {
            accessToken = await GetAccessTokenAsync(consumerKey, consumerSecret, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "M-Pesa OAuth token request failed");
            return new StkPushResult(false, null, null, "Couldn't authenticate with M-Pesa. Try again shortly.");
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var password  = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{shortcode}{passkey}{timestamp}"));

        var normalizedPhone = NormalizePhoneNumber(phoneNumber);

        var requestBody = new StkPushRequest(
            BusinessShortCode: shortcode,
            Password:          password,
            Timestamp:         timestamp,
            TransactionType:   "CustomerPayBillOnline",
            Amount:            (int)Math.Ceiling(amountKes), // Daraja sandbox rejects decimal amounts
            PartyA:            normalizedPhone,
            PartyB:             shortcode,
            PhoneNumber:       normalizedPhone,
            CallBackURL:       callbackUrl,
            AccountReference:  accountReference,
            TransactionDesc:   transactionDescription);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/mpesa/stkpush/v1/processrequest")
        {
            Content = JsonContent.Create(requestBody),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("M-Pesa STK push rejected: {Body}", body);
            return new StkPushResult(false, null, null, "M-Pesa rejected the payment request. Check the phone number and try again.");
        }

        var payload = System.Text.Json.JsonSerializer.Deserialize<StkPushResponse>(body);
        if (payload is null || payload.ResponseCode != "0")
        {
            return new StkPushResult(false, null, null, payload?.ResponseDescription ?? "M-Pesa didn't accept the request.");
        }

        return new StkPushResult(true, payload.CheckoutRequestID, payload.MerchantRequestID, null);
    }

    private async Task<string> GetAccessTokenAsync(string consumerKey, string consumerSecret, CancellationToken ct)
    {
        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{consumerKey}:{consumerSecret}"));

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{BaseUrl}/oauth/v1/generate?grant_type=client_credentials");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken: ct);
        return payload?.AccessToken ?? throw new InvalidOperationException("M-Pesa OAuth response had no access_token.");
    }

    /// <summary>Daraja expects 2547XXXXXXXX — normalize the common local formats (07..., +2547..., 2547...).</summary>
    private static string NormalizePhoneNumber(string phoneNumber)
    {
        var digitsOnly = new string(phoneNumber.Where(char.IsDigit).ToArray());

        if (digitsOnly.StartsWith("0")) return "254" + digitsOnly[1..];
        if (digitsOnly.StartsWith("254")) return digitsOnly;
        if (digitsOnly.StartsWith("7") || digitsOnly.StartsWith("1")) return "254" + digitsOnly;

        return digitsOnly;
    }

    private record OAuthTokenResponse([property: JsonPropertyName("access_token")] string? AccessToken);

    private record StkPushRequest(
        [property: JsonPropertyName("BusinessShortCode")] string BusinessShortCode,
        [property: JsonPropertyName("Password")]          string Password,
        [property: JsonPropertyName("Timestamp")]         string Timestamp,
        [property: JsonPropertyName("TransactionType")]   string TransactionType,
        [property: JsonPropertyName("Amount")]            int    Amount,
        [property: JsonPropertyName("PartyA")]            string PartyA,
        [property: JsonPropertyName("PartyB")]            string PartyB,
        [property: JsonPropertyName("PhoneNumber")]       string PhoneNumber,
        [property: JsonPropertyName("CallBackURL")]       string CallBackURL,
        [property: JsonPropertyName("AccountReference")]  string AccountReference,
        [property: JsonPropertyName("TransactionDesc")]   string TransactionDesc);

    private record StkPushResponse(
        [property: JsonPropertyName("MerchantRequestID")]    string? MerchantRequestID,
        [property: JsonPropertyName("CheckoutRequestID")]    string? CheckoutRequestID,
        [property: JsonPropertyName("ResponseCode")]         string? ResponseCode,
        [property: JsonPropertyName("ResponseDescription")]  string? ResponseDescription);
}
