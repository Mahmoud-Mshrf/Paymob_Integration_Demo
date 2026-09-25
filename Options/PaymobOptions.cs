namespace Paymob_Integration_Demo.Options;

public class PaymobOptions
{
    public const string SectionName = "Paymob";

    public string BaseUrl { get; set; } = "https://accept.paymob.com/api";
    public string SecretKey { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string HmacSecret { get; set; } = string.Empty;
    public string ApiBaseUrl {get;set;} = "https://accept.paymob.com/";
    public string CheckoutBaseUrl {get;set;} = "https://eg.checkout.paymob.com/";
    public List<int> IntegrationIds {get;set;} = [];
}
