using Paymob_Integration_Demo.Models;

namespace Paymob_Integration_Demo.Services;

public interface IPaymobService
{
    Task<CreateIntentionResponse> CreateIntentionAsync(CreateIntentionRequest request, CancellationToken ct = default);
    string BuildCheckoutUrl(string clientSecret);
}
