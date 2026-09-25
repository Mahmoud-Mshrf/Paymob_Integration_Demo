namespace Paymob_Integration_Demo.Storage;

public class PaymentOrder
{
    public const string Pending = "Pending";
    public const string Paid = "Paid";
    public const string Failed = "Failed";

    public string Id { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long AmountInPiastres { get; set; }
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string Status { get; set; } = Pending;
    public long? PaymobOrderId { get; set; }
    public string? IntentionId { get; set; }
    public long? TransactionId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}