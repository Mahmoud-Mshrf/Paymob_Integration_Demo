using System.Security.Cryptography; // Provides HMAC-SHA512 for authenticating Paymob webhook payloads.
using System.Text; // Converts HMAC input and the configured secret to bytes.
using System.Text.Json; // Reads the raw webhook JSON without requiring a separate DTO.
using Microsoft.AspNetCore.Mvc; // Supplies controller, route, binding, and HTTP response types.
using Microsoft.AspNetCore.WebUtilities; // Safely adds the order ID to the configured frontend URL.
using Microsoft.EntityFrameworkCore; // Supplies async database queries and atomic bulk updates.
using Microsoft.Extensions.Options; // Injects the bound Paymob configuration.
using Paymob_Integration_Demo.Models; // Contains the Paymob intention request and billing models.
using Paymob_Integration_Demo.Options; // Contains Paymob API and credential settings.
using Paymob_Integration_Demo.Services; // Contains the Paymob API abstraction used by checkout.
using Paymob_Integration_Demo.Storage; // Contains the persisted order model and database context.

namespace Paymob_Integration_Demo.Controllers; // Groups HTTP endpoints that handle payment workflows.

// Represents the customer and product details accepted when starting checkout.
public record CreateCheckoutRequest(
    string ProductName, long AmountInPiastres, string CustomerEmail, string CustomerPhone); // Amount is in the smallest EGP unit.
 
[ApiController] // Enables API-specific model binding and automatic validation responses.
[Route("api/payments")] // Places all payment actions under the same URL prefix.
public class PaymentsController : ControllerBase // Handles checkout, webhooks, status reads, and browser returns.
{
    private readonly IPaymobService _paymob; // Makes Paymob API calls without coupling actions to HTTP details.
    private readonly PaymobOptions _options; // Holds Paymob credentials, integration IDs, and public API URL.
    private readonly OrdersDbContext _orders; // Reads and updates the application's durable order records.
    private readonly IConfiguration _configuration; // Reads the frontend payment-result URL.
 
    // Receives the services required by every payment endpoint through dependency injection.
    public PaymentsController(
        IPaymobService paymob, // Provides the Paymob intention and checkout operations.
        IOptions<PaymobOptions> options, // Provides the Paymob settings bound during application startup.
        OrdersDbContext orders, // Provides access to orders persisted before checkout.
        IConfiguration configuration) // Provides application-level URLs such as the frontend result page.
    {
        _paymob = paymob; // Retains the Paymob service for the checkout action.
        _options = options.Value; // Uses the configured Paymob values rather than the options wrapper.
        _orders = orders; // Retains the database context for payment-order operations.
        _configuration = configuration; // Retains configuration for the browser return destination.
    }
 
    [HttpPost("checkout")] // Starts a payment and returns a URL for the customer to open.
    public async Task<IActionResult> CreateCheckout([FromBody] CreateCheckoutRequest req) // Binds checkout details from the JSON request body.
    {
        // 1) Create YOUR OWN order record first (status: Pending) and keep its id.
        //    This is the value that becomes "special_reference" below.
        var myOrderId = $"ORD-{Guid.NewGuid():N}"[..24]; // Creates an opaque merchant reference that links callbacks to this order.
        var order = new PaymentOrder // Builds the local record that remains the authoritative order state.
        {
            Id = myOrderId, // Uses the merchant reference as the local primary key.
            ProductName = req.ProductName, // Keeps the purchased product with the order.
            AmountInPiastres = req.AmountInPiastres, // Stores the charged amount in the currency's minor unit.
            CustomerEmail = req.CustomerEmail, // Stores the email supplied for billing.
            CustomerPhone = req.CustomerPhone, // Stores the phone supplied for billing.
            Status = PaymentOrder.Pending, // Marks the order unpaid until a verified webhook updates it.
            CreatedAtUtc = DateTime.UtcNow, // Records when checkout was initiated in UTC.
            UpdatedAtUtc = DateTime.UtcNow, // Initializes the last-modified timestamp in UTC.
        };
        _orders.PaymentOrders.Add(order); // Tracks the new order for insertion into the database.
        await _orders.SaveChangesAsync(); // Persists the order before Paymob can send a callback for it.
 
        var intentionRequest = new CreateIntentionRequest // Prepares Paymob's server-side payment-intention payload.
        {
            Amount = req.AmountInPiastres, // Sends the requested charge amount in piastres.
            Currency = "EGP", // Declares the currency for the payment intention.
            PaymentMethods = _options.IntegrationIds.Cast<object>().ToList(), // Limits payment methods to configured Paymob integrations.
            Items = new() // Includes line-item details required by the intention API.
            {
                new IntentionItem // Describes the single product represented by this checkout.
                {
                    Name = req.ProductName, // Sets the product's display name.
                    Amount = req.AmountInPiastres, // Sets this item's total amount in piastres.
                    Description = req.ProductName, // Supplies a short description for the item.
                    Quantity = 1, // This request creates one unit of the product.
                },
            },
            BillingData = new BillingData // Supplies customer billing fields to Paymob.
            {
                Email = req.CustomerEmail, // Associates the checkout with the customer's email.
                PhoneNumber = req.CustomerPhone, // Associates the checkout with the customer's phone number.
            },
            SpecialReference = myOrderId, // Lets the verified webhook identify this local order.
            NotificationUrl = $"{_options.PublicApiBaseUrl.TrimEnd('/')}/api/payments/webhook", // Gives Paymob the server-to-server callback URL.
            RedirectionUrl = $"{_options.PublicApiBaseUrl.TrimEnd('/')}/api/payments/return", // Gives the customer's browser return URL.
        };
 
        var intention = await _paymob.CreateIntentionAsync(intentionRequest); // Creates the payment intention using Paymob's API.
        var checkoutUrl = _paymob.BuildCheckoutUrl(intention.ClientSecret); // Builds the hosted checkout URL from Paymob's client secret.
 
        order.PaymobOrderId = intention.IntentionOrderId; // Saves Paymob's order identifier for reconciliation.
        order.IntentionId = intention.Id; // Saves Paymob's intention identifier for support and tracing.
        order.UpdatedAtUtc = DateTime.UtcNow; // Refreshes the last-modified time after Paymob creates the intention.
        await _orders.SaveChangesAsync(); // Persists Paymob's identifiers beside the local order.
 
        return Ok(new // Returns checkout details to the caller without exposing Paymob secret credentials.
        {
            checkoutUrl, // URL the frontend should open for hosted payment.
            myOrderId, // Local reference the frontend can use to request order status.
            paymobOrderId = intention.IntentionOrderId, // Paymob's order identifier for diagnostics.
            intentionId = intention.Id, // Paymob's intention identifier for diagnostics.
        });
    }
     
    [HttpPost("webhook")] // Receives Paymob's server-to-server payment notification.
    public async Task<IActionResult> Webhook([FromQuery] string hmac) // Binds the signature Paymob sends in the query string.
    {
        using var reader = new StreamReader(Request.Body); // Opens the raw request body containing Paymob's JSON payload.
        var rawBody = await reader.ReadToEndAsync(); // Reads the payload exactly as received for JSON parsing.
 
        using var doc = JsonDocument.Parse(rawBody); // Parses the Paymob webhook document.
        var obj = doc.RootElement.GetProperty("obj"); // Selects the transaction object covered by Paymob's HMAC scheme.
 
        var calculated = ComputeHmac(obj, _options.HmacSecret); // Recomputes the expected signature with the private HMAC secret.
        if (!string.Equals(calculated, hmac, StringComparison.OrdinalIgnoreCase)) // Compares signatures without case sensitivity.
            return Unauthorized(); // Rejects any payload that cannot be authenticated.
 
        bool success = obj.GetProperty("success").GetBoolean(); // Reads the transaction outcome only after the signature is valid.
        string myOrderId = obj.GetProperty("order").TryGetProperty("merchant_order_id", out var mo) // Looks for the merchant reference attached to the Paymob order.
            ? mo.GetString() ?? "" : ""; // Uses an empty value when the reference is absent or null.
        long transactionId = obj.GetProperty("id").GetInt64(); // Reads the Paymob transaction ID for idempotency and audit tracing.
 
        if (string.IsNullOrWhiteSpace(myOrderId)) // Ensures the callback can be linked to a local order.
            return BadRequest("Webhook does not contain a merchant order id."); // Rejects a signed but unusable callback.

        var orderExists = await _orders.PaymentOrders // Starts a database lookup in the local orders table.
            .AnyAsync(order => order.Id == myOrderId); // Checks for the exact merchant reference without loading the full row.
        if (!orderExists) // Detects callbacks that do not match an order created by this application.
            return NotFound(); // Reports the unknown order so the callback is not acknowledged as processed.

        var newStatus = success ? PaymentOrder.Paid : PaymentOrder.Failed; // Maps Paymob's authenticated outcome to the local status values.
        await _orders.PaymentOrders // Applies the status change directly in the database.
            .Where(order => order.Id == myOrderId // Limits the update to this merchant order.
                && order.Status != PaymentOrder.Paid // Prevents any later callback from downgrading a paid order.
                && order.TransactionId != transactionId) // Makes a repeated notification for this transaction a no-op.
            .ExecuteUpdateAsync(updates => updates // Updates the matching row atomically without a read-modify-save race.
                .SetProperty(order => order.Status, newStatus) // Stores the authenticated payment outcome.
                .SetProperty(order => order.TransactionId, transactionId) // Stores the transaction that produced this status.
                .SetProperty(order => order.UpdatedAtUtc, DateTime.UtcNow)); // Records when the callback changed the order.
 
        return Ok(); // Acknowledges successful handling so Paymob does not need to retry this callback.
    }
 
    // Builds Paymob's canonical HMAC input and returns its SHA-512 signature.
    private static string ComputeHmac(JsonElement obj, string secret) // Static because signing depends only on the payload and secret.
    {
        string Field(string path) // Reads one named field, including nested paths such as order.id.
        {
            var parts = path.Split('.'); // Splits nested JSON paths into property names.
            var cur = obj; // Starts traversing at the signed transaction object.
            foreach (var part in parts) cur = cur.GetProperty(part); // Follows each property in the requested path.
            return cur.ValueKind switch // Converts JSON values into Paymob's expected text representation.
            {
                JsonValueKind.String => cur.GetString() ?? "", // Uses the string contents rather than JSON quotes.
                JsonValueKind.True => "true", // Uses lowercase text for a JSON true value.
                JsonValueKind.False => "false", // Uses lowercase text for a JSON false value.
                JsonValueKind.Number => cur.GetRawText(), // Preserves the number's JSON text representation.
                JsonValueKind.Null => "", // Represents null as an empty string in the canonical input.
                _ => cur.GetRawText(), // Uses raw JSON for any other value kind.
            };
        }
 
        string[] orderedFields = // Keeps Paymob's required field names in the exact HMAC concatenation order.
        {
            "amount_cents", "created_at", "currency", "error_occured", "has_parent_transaction", // Amount, creation time, currency, error flag, and parent-transaction flag.
            "id", "integration_id", "is_3d_secure", "is_auth", "is_capture", "is_refunded", // Transaction and integration IDs plus security and lifecycle flags.
            "is_standalone_payment", "is_voided", "order.id", "owner", "pending", // Standalone/void state, Paymob order ID, owner, and pending flag.
            "source_data.pan", "source_data.sub_type", "source_data.type", "success", // Masked source details and final transaction outcome.
        };
 
        var concatenated = string.Concat(orderedFields.Select(Field)); // Joins field values without separators as Paymob's signature scheme requires.
 
        using var hmacSha512 = new HMACSHA512(Encoding.UTF8.GetBytes(secret)); // Creates the keyed SHA-512 hash using the configured secret.
        var hash = hmacSha512.ComputeHash(Encoding.UTF8.GetBytes(concatenated)); // Computes the expected digest over the canonical field values.
        return Convert.ToHexString(hash).ToLowerInvariant(); // Formats the digest as lowercase hexadecimal for comparison.
    }
    [HttpGet("orders/{myOrderId}/status")] // Exposes the local payment state for the frontend result page to poll.
    public async Task<IActionResult> GetOrderStatus(string myOrderId) // Receives the local merchant order ID from the URL path.
    {
        var order = await _orders.PaymentOrders // Starts a database query for the requested order.
            .AsNoTracking() // Avoids tracking because this endpoint only reads data.
            .Where(order => order.Id == myOrderId) // Selects only the order matching the supplied ID.
            .Select(order => new { order.Id, order.Status, order.UpdatedAtUtc }) // Returns status metadata without customer or payment details.
            .SingleOrDefaultAsync(); // Returns the result, or null when the order does not exist.

        return order is null ? NotFound() : Ok(order); // Distinguishes an unknown order from a valid status response.
    }

    [HttpGet("return")] // Receives the customer's browser redirect after hosted checkout.
    public async Task<IActionResult> Return([FromQuery(Name = "merchant_order_id")] string? merchantOrderId) // Reads the merchant reference included in Paymob's return URL.
    {
        if (string.IsNullOrWhiteSpace(merchantOrderId)) // Requires an order reference before forwarding the customer.
            return BadRequest("Missing merchant order id."); // Rejects returns that cannot be associated with an order.

        var orderExists = await _orders.PaymentOrders // Checks the local database without loading customer data.
            .AsNoTracking() // Keeps this existence-only query read-only.
            .AnyAsync(order => order.Id == merchantOrderId); // Confirms the reference belongs to an order created here.
        if (!orderExists) // Prevents redirecting with an unknown or tampered order reference.
            return NotFound(); // Reports that the referenced local order does not exist.

        var paymentResultUrl = _configuration["Frontend:PaymentResultUrl"]; // Reads the trusted frontend destination from server configuration.
        if (string.IsNullOrWhiteSpace(paymentResultUrl)) // Checks that the destination is configured before redirecting.
            return Problem("The frontend payment result URL is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable); // Reports a deployment configuration problem.

        var redirectUrl = QueryHelpers.AddQueryString(paymentResultUrl, "orderId", merchantOrderId); // Adds the local order ID with correct URL encoding.
        return Redirect(redirectUrl); // Sends the browser to the result page, which must read status from this API.
    }

}
