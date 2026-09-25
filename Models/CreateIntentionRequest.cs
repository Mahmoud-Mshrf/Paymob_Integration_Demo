using System.Text.Json.Serialization;

namespace Paymob_Integration_Demo.Models;

public class CreateIntentionRequest
{
    [JsonPropertyName("amount")] public long Amount { get; set; }        // smallest currency unit!
    [JsonPropertyName("currency")] public string Currency { get; set; } = "EGP";
    [JsonPropertyName("payment_methods")] public List<object> PaymentMethods { get; set; } = new();
    [JsonPropertyName("items")] public List<IntentionItem> Items { get; set; } = new();
    [JsonPropertyName("billing_data")] public BillingData BillingData { get; set; } = new();
    [JsonPropertyName("special_reference")] public string SpecialReference { get; set; } = "";  // YOUR order id
    [JsonPropertyName("notification_url")] public string? NotificationUrl { get; set; }
    [JsonPropertyName("redirection_url")] public string? RedirectionUrl { get; set; }
}


