# Paymob Integration Demo

A small ASP.NET Core 10 demo showing a hosted Paymob checkout flow. The API creates and stores a local order, creates a Paymob payment intention, and returns the hosted checkout URL. A static result page polls the local order status while a signed Paymob webhook provides the authoritative payment result.

## Requirements

- .NET 10 SDK
- A Paymob account with an enabled integration
- The integration ID, API secret key, public key, and HMAC secret from Paymob
- ngrok (or another HTTPS tunnel) to receive Paymob callbacks during local testing
- VS Code REST Client is optional for sending the included `request.http` request

## Configure Paymob

Do not commit API keys or HMAC secrets to `appsettings.json`. The project already has a .NET User Secrets ID, so set credentials from the repository directory:

```powershell
dotnet user-secrets set "Paymob:SecretKey" "<paymob-secret-key>"
dotnet user-secrets set "Paymob:PublicKey" "<paymob-public-key>"
dotnet user-secrets set "Paymob:HmacSecret" "<paymob-hmac-secret>"
```

Set the integration ID in `appsettings.json` under `Paymob:IntegrationIds` using the numeric ID for the integration you want to test. The sample value is not guaranteed to belong to your Paymob account.

The app also needs two public URLs. `Paymob:PublicApiBaseUrl` is the root URL Paymob uses to send the webhook and the customer browser return. `Frontend:PaymentResultUrl` is the result page the browser visits after returning to the API. During local testing, both should use the current HTTPS URL supplied by your tunnel:

```powershell
dotnet user-secrets set "Paymob:PublicApiBaseUrl" "https://<your-current-tunnel-host>"
dotnet user-secrets set "Frontend:PaymentResultUrl" "https://<your-current-tunnel-host>/payment-result"
```

Replace the sample tunnel host whenever ngrok gives you a new one, then restart the app. User Secrets override the corresponding `appsettings.json` values in the Development environment.

## Run Locally

1. Restore dependencies:

   ```powershell
   dotnet restore
   ```

2. Start a tunnel to the HTTP profile's local port in a separate terminal and leave it running:

   ```powershell
   ngrok http 5091
   ```

3. Copy the HTTPS forwarding URL shown by ngrok and set the two public URL settings above.
4. Start the API from the repository directory:

   ```powershell
   dotnet run --launch-profile http
   ```

The API listens at `http://localhost:5091`. On startup, it creates `paymob-orders.db` in the working directory if it does not already exist. SQLite database files are local development data and are ignored by Git.

The result page is served by the same app at:

```text
http://localhost:5091/payment-result
```

For a real Paymob browser return, use the public HTTPS tunnel URL configured as `Frontend:PaymentResultUrl`.

## Test Checkout

Send the included request in [request.http](request.http) using VS Code REST Client, or send an equivalent request:

```http
POST http://localhost:5091/api/payments/checkout
Content-Type: application/json

{
  "productName": "Cairo-Derby Ticket",
  "amountInPiastres": 10000,
  "customerEmail": "customer@example.com",
  "customerPhone": "01111111111"
}
```

The response contains:

- `checkoutUrl`: open this URL in a browser to use Paymob's hosted checkout.
- `myOrderId`: the local merchant order ID used by the result page and status endpoint.
- `paymobOrderId` and `intentionId`: Paymob identifiers useful for troubleshooting.

Complete or cancel the payment in the hosted checkout. Paymob redirects the browser through `/api/payments/return`; that endpoint does not trust the browser's `success` query parameter. It redirects to the configured result page with the local order ID. The page polls the status endpoint every two seconds for up to 90 seconds, then offers a manual retry if the order is still pending.

The order status can also be queried directly:

```http
GET http://localhost:5091/api/payments/orders/<myOrderId>/status
```

Possible states are `Pending`, `Paid`, and `Failed`. `Pending` means a verified callback has not yet updated the local order. Only the HMAC-verified server-to-server webhook changes the payment status; do not mark an order paid based on a browser redirect or a client-side request.

## API Routes

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/api/payments/checkout` | Creates a local order and Paymob intention; returns checkout details. |
| `POST` | `/api/payments/webhook?hmac=...` | Verifies the Paymob signature and updates the local order idempotently. |
| `GET` | `/api/payments/return?merchant_order_id=...` | Validates the order reference and redirects the browser to the configured result page. |
| `GET` | `/api/payments/orders/{myOrderId}/status` | Returns the order ID, status, and last-updated timestamp for polling. |
| `GET` | `/payment-result` | Serves the static payment result page. |

## Troubleshooting

- **Checkout creation fails:** verify the Paymob secret key, public key, integration ID, and that the Paymob account is enabled for the selected environment.
- **The browser returns but status remains pending:** verify the tunnel is running, `Paymob:PublicApiBaseUrl` points to its current HTTPS address, and the Paymob webhook can reach `/api/payments/webhook`.
- **The browser cannot reach the result page:** verify `Frontend:PaymentResultUrl` uses the current tunnel URL and ends with `/payment-result`.
- **The webhook returns `401`:** verify `Paymob:HmacSecret` matches the integration that sent the webhook.
- **The status endpoint returns `404`:** confirm the `myOrderId` came from this checkout response and was not replaced with Paymob's order ID.

## Demo Limitations

This repository is for integration learning and local testing, not a production-ready store. Before production use, replace the sample JWT signing key, keep all secrets in a managed secret store, use a production database with migrations and backups, validate checkout input and prices against server-side product data, and add appropriate order access controls, logging, monitoring, and retry handling. The SQLite database and `EnsureCreated` startup behavior are intended for this demo.
