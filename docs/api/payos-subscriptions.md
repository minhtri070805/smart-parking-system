# PayOS monthly subscription payments

Subscription purchases use monthly manual payment links. A user pays each billing period; returning from PayOS does not activate access. The Payment Service activates access only after verifying a PayOS webhook and Core confirms the payment.

## Client API

`POST /api/payments/payos/subscriptions` is routed by YARP to the Payment Service. It requires a `USER` access token and an `Idempotency-Key` header (1–100 characters).

```json
{
  "planId": 12,
  "vehicleId": 34
}
```

The authenticated user, plan price, currency, term, vehicle ownership, lot scope, and billing period are obtained or validated server-side. The response contains `orderCode`, `checkoutUrl`, `amount`, `currency`, billing period, and payment status. Do not accept a user ID, amount, or entitlement dates from the client.

`GET /api/payments/payos/subscriptions/{orderCode}` returns the caller's attempt status. Attempts belonging to another user return 404.

PayOS sends webhooks to `POST /api/payments/payos/webhook`. This endpoint is public and verifies the raw body with the official PayOS SDK. Repeated successful webhooks are idempotent. A verified payment that cannot reach Core remains `PAID_PENDING_CORE_CONFIRMATION`; a background worker retries Core confirmation. Core separately checks pass `ends_at_utc` each minute and marks passes whose period has ended as `EXPIRED`.

Core has no public pass-purchase route. Customers create checkout links on Payment Service; Core creates or renews passes through the signed internal preparation/confirmation/cancellation routes under `/api/internal/subscription-payments/**`, which are blocked by the public gateway.

## Configuration

Set these values through deployment secrets or environment variables; do not commit credentials:

```text
PayOS__ClientId
PayOS__ApiKey
PayOS__ChecksumKey
PayOS__ReturnUrl
PayOS__CancelUrl
Jwt__SecretKey
Jwt__Issuer
Jwt__Audience
PaymentService__SharedSecret
Services__Core__BaseUrl
ConnectionStrings__DefaultConnection
```

For local Payment Service development, copy `payment-service/.env.example` to `payment-service/.env`. The Payment API and its EF design-time factory load that file. Environment variables defined by the operating system or launch profile take precedence over `.env` values.

The confirmation retry worker polls every 60 seconds by default. Configure `PayOSWorker__ConfirmationPollIntervalSeconds` from 15 to 300 seconds to tune the tradeoff between confirmation retry latency and database polling. PayOS webhooks still trigger immediate Core confirmation; this interval only affects retry polling.

`Jwt__SecretKey` and issuer/audience must match Core. `PaymentService__SharedSecret` must be identical in Payment and Core and contain at least 32 bytes. Set the PayOS webhook URL to the externally reachable `/api/payments/payos/webhook` route. Development YARP targets Payment Service at `http://localhost:5183/`.

## Code boundaries

- API owns the HTTP controller, request DTO, authentication, and hosted retry worker.
- Application owns the subscription payment use case, status model, and ports for Core, PayOS, and payment-attempt persistence.
- Infrastructure implements those ports with the PayOS SDK, signed Core HTTP calls, and the isolated EF Core repository and DbContext.
- Domain and the existing billing flow remain independent of the PayOS feature.

## Migrations

Two separate migrations are maintained:

- Core: `AddSubscriptionPaymentIntents` on `SmartParkingDbContext`.
- Payment: `AddPayOSSubscriptionPaymentAttempts` on `PayOSPaymentDbContext`, with its own `__PayOSSubscriptionMigrationsHistory` table.

Core migrations are applied through the Core deployment process. Payment Service applies its PayOS migration automatically in Development against the runtime connection string; apply it through the deployment's database migration process in other environments before enabling subscription sales. The Payment feature uses a dedicated DbContext and table; it does not modify the existing billing tables or payment entities.
