using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Paymob_Integration_Demo.Models;
using Paymob_Integration_Demo.Options;
using Paymob_Integration_Demo.Services;
using Paymob_Integration_Demo.Storage;

namespace Paymob_Integration_Demo.Controllers;

public record CreateCheckoutRequest(
    string ProductName, long AmountInPiastres, string CustomerEmail, string CustomerPhone);
 
[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymobService _paymob;
    private readonly PaymobOptions _options;
    private readonly OrdersDbContext _orders;
 
    public PaymentsController(
        IPaymobService paymob,
        IOptions<PaymobOptions> options,
        OrdersDbContext orders)
    {
        _paymob = paymob;
        _options = options.Value;
        _orders = orders;
    }
 
    [HttpPost("checkout")]
    public async Task<IActionResult> CreateCheckout([FromBody] CreateCheckoutRequest req)
    {
        // 1) Create YOUR OWN order record first (status: Pending) and keep its id.
        //    This is the value that becomes "special_reference" below.
        var myOrderId = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..24];
        var order = new PaymentOrder
        {
            Id = myOrderId,
            ProductName = req.ProductName,
            AmountInPiastres = req.AmountInPiastres,
            CustomerEmail = req.CustomerEmail,
            CustomerPhone = req.CustomerPhone,
            Status = PaymentOrder.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        _orders.PaymentOrders.Add(order);
        await _orders.SaveChangesAsync();
 
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
 
        order.PaymobOrderId = intention.IntentionOrderId;
        order.IntentionId = intention.Id;
        order.UpdatedAtUtc = DateTime.UtcNow;
        await _orders.SaveChangesAsync();
 
        return Ok(new
        {
            checkoutUrl,
            myOrderId,
            paymobOrderId = intention.IntentionOrderId,
            intentionId = intention.Id,
        });
    }
     
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook([FromQuery] string hmac)
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync();
 
        using var doc = JsonDocument.Parse(rawBody);
        var obj = doc.RootElement.GetProperty("obj");
 
        var calculated = ComputeHmac(obj, _options.HmacSecret);
        if (!string.Equals(calculated, hmac, StringComparison.OrdinalIgnoreCase))
            return Unauthorized(); // signature mismatch — do NOT trust this payload
 
        bool success = obj.GetProperty("success").GetBoolean();
        string myOrderId = obj.GetProperty("order").TryGetProperty("merchant_order_id", out var mo)
            ? mo.GetString() ?? "" : "";
        long transactionId = obj.GetProperty("id").GetInt64();
 
        if (string.IsNullOrWhiteSpace(myOrderId))
            return BadRequest("Webhook does not contain a merchant order id.");

        var orderExists = await _orders.PaymentOrders
            .AnyAsync(order => order.Id == myOrderId);
        if (!orderExists)
            return NotFound();

        var newStatus = success ? PaymentOrder.Paid : PaymentOrder.Failed;
        await _orders.PaymentOrders
            .Where(order => order.Id == myOrderId
                && order.Status != PaymentOrder.Paid
                && order.TransactionId != transactionId)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(order => order.Status, newStatus)
                .SetProperty(order => order.TransactionId, transactionId)
                .SetProperty(order => order.UpdatedAtUtc, DateTime.UtcNow));
 
        return Ok();
    }
 
    private static string ComputeHmac(JsonElement obj, string secret)
    {
        string Field(string path)
        {
            var parts = path.Split('.');
            var cur = obj;
            foreach (var part in parts) cur = cur.GetProperty(part);
            return cur.ValueKind switch
            {
                JsonValueKind.String => cur.GetString() ?? "",
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => cur.GetRawText(),
                JsonValueKind.Null => "",
                _ => cur.GetRawText(),
            };
        }
 
        string[] orderedFields =
        {
            "amount_cents", "created_at", "currency", "error_occured", "has_parent_transaction",
            "id", "integration_id", "is_3d_secure", "is_auth", "is_capture", "is_refunded",
            "is_standalone_payment", "is_voided", "order.id", "owner", "pending",
            "source_data.pan", "source_data.sub_type", "source_data.type", "success",
        };
 
        var concatenated = string.Concat(orderedFields.Select(Field));
 
        using var hmacSha512 = new HMACSHA512(Encoding.UTF8.GetBytes(secret));
        var hash = hmacSha512.ComputeHash(Encoding.UTF8.GetBytes(concatenated));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
    [HttpGet("return")]
    public IActionResult Return([FromQuery] string? success, [FromQuery] string? merchant_order_id)
    {
        // Purely cosmetic — the real confirmation already happened in the webhook above.
        return Redirect(success == "true" ? "/thank-you" : "/payment-failed");
    }

}
