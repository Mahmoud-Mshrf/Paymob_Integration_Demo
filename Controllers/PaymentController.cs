using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Paymob_Integration_Demo.Models;
using Paymob_Integration_Demo.Options;
using Paymob_Integration_Demo.Services;

namespace Paymob_Integration_Demo.Controllers;

public record CreateCheckoutRequest(
    string ProductName, long AmountInPiastres, string CustomerEmail, string CustomerPhone);
 
[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymobService _paymob;
    private readonly PaymobOptions _options;
    // In a real app, inject your DbContext / order repository here too.
 
    public PaymentsController(IPaymobService paymob, IOptions<PaymobOptions> options)
    {
        _paymob = paymob;
        _options = options.Value;
    }
 
    [HttpPost("checkout")]
    public async Task<IActionResult> CreateCheckout([FromBody] CreateCheckoutRequest req)
    {
        // 1) Create YOUR OWN order record first (status: Pending) and keep its id.
        //    This is the value that becomes "special_reference" below.
        var myOrderId = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..24];
 
        var intentionRequest = new CreateIntentionRequest
        {
            Amount = req.AmountInPiastres,     // smallest currency unit — see Section 3.3
            Currency = "EGP",
            PaymentMethods = _options.IntegrationIds.Cast<object>().ToList(),
            Items = new()
            {
                new IntentionItem
                {
                    Name = req.ProductName,
                    Amount = req.AmountInPiastres,
                    Description = req.ProductName,
                    Quantity = 1,
                },
            },
            BillingData = new BillingData
            {
                Email = req.CustomerEmail,
                PhoneNumber = req.CustomerPhone,
            },
            SpecialReference = myOrderId,                          // YOUR order id
            NotificationUrl = "https://yourapi.com/api/payments/webhook",
            RedirectionUrl = "https://yourapi.com/api/payments/return",
        };
 
        var intention = await _paymob.CreateIntentionAsync(intentionRequest);
        var checkoutUrl = _paymob.BuildCheckoutUrl(intention.ClientSecret);
 
        // 2) Save intention.Id (Intention ID) and intention.IntentionOrderId (Paymob Order ID)
        //    next to myOrderId in your database now, so the webhook can find this order later.
 
        return Ok(new
        {
            checkoutUrl,
            myOrderId,
            paymobOrderId = intention.IntentionOrderId,
            intentionId = intention.Id,
        });
    }
}
