using System.Text.Json.Serialization;

namespace Paymob_Integration_Demo.Models;

public class IntentionItem
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("amount")] public long Amount { get; set; }        // smallest currency unit
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("quantity")] public int Quantity { get; set; } = 1;
}


