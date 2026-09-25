using System.Text.Json.Serialization;

namespace Paymob_Integration_Demo.Models;

public class CreateIntentionResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";                         // Intention ID
    [JsonPropertyName("intention_order_id")] public long IntentionOrderId { get; set; }    // Paymob Order ID
    [JsonPropertyName("client_secret")] public string ClientSecret { get; set; } = "";
    [JsonPropertyName("special_reference")] public string? SpecialReference { get; set; }  // echoed back
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("confirmed")] public bool Confirmed { get; set; }
    [JsonPropertyName("created")] public string? Created { get; set; }
}


