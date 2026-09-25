using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Paymob_Integration_Demo.Models;
using Paymob_Integration_Demo.Options;

namespace Paymob_Integration_Demo.Services;

public class PaymobService : IPaymobService
{
    private readonly HttpClient _http;
    private readonly PaymobOptions _options;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
 
    public PaymobService(HttpClient http, IOptions<PaymobOptions> options)
    {
        _options = options.Value;
        _http = http;
        _http.BaseAddress = new Uri(_options.ApiBaseUrl);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Token", _options.SecretKey); // <-- SECRET KEY goes here
    }
 
    public async Task<CreateIntentionResponse> CreateIntentionAsync(
        CreateIntentionRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("v1/intention/", request, JsonOpts, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
 
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Paymob returned {(int)response.StatusCode}: {raw}");
 
        var result = JsonSerializer.Deserialize<CreateIntentionResponse>(raw, JsonOpts);
        return result ?? throw new InvalidOperationException("Empty response from Paymob.");
    }
 
    public string BuildCheckoutUrl(string clientSecret) =>
        $"{_options.CheckoutBaseUrl}?publicKey={_options.PublicKey}&clientSecret={clientSecret}";
        // ^ PUBLIC KEY (safe to expose) + CLIENT SECRET (one-time, this payment only)
}
