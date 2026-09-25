namespace Paymob_Integration_Demo.Options;

public class PaymobOptions
{
    public const string SectionName = "Paymob";

    public string BaseUrl { get; set; } = "https://accept.paymob.com/api";
    public string ApiKey { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string HmacSecret { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}
