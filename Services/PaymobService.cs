using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Paymob_Integration_Demo.Options;

namespace Paymob_Integration_Demo.Services;

public interface IPaymobService
{
    Task<string> CreatePaymentAsync(object payload, CancellationToken cancellationToken = default);
}

public sealed class PaymobService : IPaymobService
{
    private readonly HttpClient _httpClient;
    private readonly PaymobOptions _options;

    public PaymobService(HttpClient httpClient, IOptions<PaymobOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<string> CreatePaymentAsync(object payload, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "payments");
        request.Headers.Add("Authorization", $"Bearer {_options.ApiKey}");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
