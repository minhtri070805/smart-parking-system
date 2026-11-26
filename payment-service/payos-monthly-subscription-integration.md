# PayOS Monthly Subscription Payment Integration

## 1. Goal

Implement PayOS payment for monthly subscriptions in the Smart Parking microservices system.

**Isolation requirement:** add a separate PayOS integration module/controller. Do not edit, rename, refactor, or reuse the existing Payment Service controllers, DTOs, entities, repositories, or business logic owned by another teammate. Before implementation, inspect the project structure and choose new, clearly namespaced files.

The Payment Service and Core Service run as separate applications. They must communicate through HTTP APIs (or the project's existing internal messaging mechanism), never by directly accessing each other's database.

## 2. Important payment behaviour

PayOS creates a payment link for a payment request. Do not assume that creating a monthly subscription payment link automatically charges the customer every month.

For this implementation, use **monthly manual renewal**:

1. A customer chooses a monthly subscription plan.
2. The system creates a PayOS payment link for the current billing period.
3. The customer pays using the PayOS checkout page / bank transfer.
4. PayOS sends a webhook to the Payment Service.
5. The Payment Service verifies the webhook and notifies Core Service.
6. Core Service activates or extends the subscription.
7. Before the next billing period, the customer must complete another payment.

If the product requirement later changes to automatic recurring card/direct-debit billing, confirm PayOS support and the required payment method separately. Do not simulate recurring billing by trusting a frontend redirect.

## 3. Service responsibilities

### Payment Service

Owns the PayOS integration and payment transaction lifecycle:

- Exposes new PayOS-specific endpoints.
- Creates PayOS payment links.
- Stores a separate payment-attempt record in a new PayOS-specific table/repository, if persistence is needed.
- Receives and verifies PayOS webhooks.
- Handles duplicate webhook delivery idempotently.
- Calls Core Service after a verified successful payment.
- Does not decide subscription entitlement rules.

### Core Service

Remains the source of truth for:

- Users and their identity.
- Subscription plans and prices (or the authoritative plan catalogue).
- Subscription status and expiry.
- Whether a user is allowed to start, renew, or change a subscription.
- Activating/extending a subscription after confirmed payment.

Core Service should expose a private/internal endpoint for the Payment Service to report a verified payment. It should validate the plan, user, amount, currency, billing period, and payment reference before changing subscription state.

### API Gateway (YARP)

- Routes public client requests to the new Payment Service endpoints.
- Routes should be added without removing or changing existing routes.
- The PayOS webhook must be publicly reachable by PayOS. Route it to the Payment Service without requiring a customer JWT; webhook authenticity is checked using PayOS signature verification.
- Do not expose the Core Service's internal payment-confirmation endpoint as a public client route.

## 4. Suggested isolated file structure

Adapt namespaces and folders to the actual repository conventions. Keep all new files separate from the existing payment resources.

```text
Payment Service
└── Features
    └── PayOS
        ├── Controllers
        │   └── PayOSController.cs
        ├── Contracts
        │   ├── CreateSubscriptionPaymentRequest.cs
        │   ├── CreateSubscriptionPaymentResponse.cs
        │   └── PayOSWebhookRequest.cs
        ├── Services
        │   ├── IPayOSPaymentService.cs
        │   ├── PayOSPaymentService.cs
        │   └── ICoreSubscriptionClient.cs
        ├── Clients
        │   └── CoreSubscriptionClient.cs
        ├── Persistence
        │   ├── PayOSPayment.cs
        │   ├── PayOSPaymentRepository.cs
        │   └── PayOSPaymentConfiguration.cs
        └── Options
            └── PayOSOptions.cs
```

If the project uses a different architecture (Clean Architecture, vertical slices, etc.), follow its conventions while preserving the `PayOS` feature boundary.

**Database isolation:** if a new payment-attempt table is required, add it through a new migration owned by this feature. Do not modify existing payment tables or existing payment entities. If the team has a strict shared migration process, coordinate before applying the migration.

## 5. Configuration and package

Install the official .NET SDK:

```bash
dotnet add package payOS
```

Add configuration through environment variables / deployment secrets. Do not commit real credentials.

```json
{
  "PayOS": {
    "ClientId": "",
    "ApiKey": "",
    "ChecksumKey": "",
    "ReturnUrl": "https://your-frontend-domain/payment/result",
    "CancelUrl": "https://your-frontend-domain/payment/cancel"
  },
  "Services": {
    "Core": {
      "BaseUrl": "https://your-core-service"
    }
  }
}
```

Recommended environment variables:

```text
PayOS__ClientId
PayOS__ApiKey
PayOS__ChecksumKey
PayOS__ReturnUrl
PayOS__CancelUrl
Services__Core__BaseUrl
```

Register `PayOSOptions`, `PayOSClient`, the PayOS feature services, and a typed `HttpClient` for Core Service in dependency injection. Use the exact constructor/API supported by the installed SDK version. Never log API keys, checksum keys, access tokens, or full sensitive webhook data.

## 6. API contract

Use a new controller, for example `PayOSController`. The route prefix below is illustrative; align it with the existing API versioning convention.

### 6.1 Create a subscription payment link

```http
POST /api/payments/payos/subscriptions
Authorization: Bearer <customer-access-token>
Content-Type: application/json
```

Example request:

```json
{
  "planId": "MONTHLY_STANDARD"
}
```

The client should not submit an authoritative `userId`, amount, subscription status, or expiry date. Obtain the authenticated user identity from the validated JWT claims. Obtain plan price and eligibility from Core Service.

Example response:

```json
{
  "orderCode": 123456789012,
  "paymentLinkId": "payos-payment-link-id",
  "checkoutUrl": "https://pay.payos.vn/...",
  "amount": 99000,
  "currency": "VND",
  "status": "PENDING"
}
```

Implementation steps:

1. Authenticate the customer.
2. Read the stable user identifier from the JWT `sub` (or the exact claim configured by the authentication system). Do not assume the claim name without checking the issuer's token.
3. Call Core Service's internal API to validate the user, plan, current subscription, and current plan price.
4. Generate a unique PayOS `orderCode` (numeric and within PayOS's supported range). Do not use a small sequential value that can collide across service instances.
5. Create a local pending payment attempt containing at least:
   - `OrderCode`
   - `UserId`
   - `PlanId`
   - `Amount`
   - `Currency`
   - `BillingPeriodStart`
   - `BillingPeriodEnd`
   - `Status`
   - `CreatedAt`
   - `PayOSPaymentLinkId` (when returned)
6. Call `PaymentRequests.CreateAsync()` using the server-side amount and generated order code.
7. Save the returned payment-link ID and checkout URL.
8. Return the checkout URL to the frontend.

Example SDK usage (adapt property types to the installed SDK version):

```csharp
var request = new CreatePaymentLinkRequest
{
    OrderCode = orderCode,
    Amount = amountInVnd,
    Description = $"SUB{orderCode}",
    ReturnUrl = options.ReturnUrl,
    CancelUrl = options.CancelUrl
};

var paymentLink = await payOS.PaymentRequests.CreateAsync(request);
```

Keep the description short and within PayOS's documented constraints. The local database record is created before or safely coordinated with the PayOS call so failed external calls can be reconciled. Handle the case where PayOS creates a link but the local save fails.

### 6.2 PayOS webhook

```http
POST /api/payments/payos/webhook
```

This endpoint is called by PayOS, not by the browser.

Processing requirements:

1. Read the incoming request body.
2. Verify it using the official SDK's webhook verification method. Prefer verifying the unmodified raw request body when supported by the installed SDK.
3. Reject invalid signatures.
4. Use the verified webhook data, not unverified fields from the request.
5. Find the local payment attempt by `orderCode`.
6. Check that the payment is still eligible for processing.
7. Verify the received amount and currency against the locally stored payment attempt.
8. Ensure the payment has not already been processed.
9. Mark the payment as paid/processing in a transaction or with an atomic state transition.
10. Call Core Service's internal confirmation endpoint.
11. Persist the Core Service result and final local payment status.
12. Return a 2xx response for successfully handled or already-processed valid webhook events. Return an appropriate non-2xx response when processing genuinely fails so the event can be retried, according to the webhook retry behaviour.

SDK verification example:

```csharp
var rawBody = await new StreamReader(Request.Body).ReadToEndAsync();
var verifiedData = await payOS.Webhooks.VerifyAsync(rawBody);
```

Check the installed SDK's exact method signature. Do not manually trust `success`, `code`, or `amount` before signature verification.

### 6.3 Return and cancel pages

`ReturnUrl` and `CancelUrl` are browser navigation destinations. They are useful for showing a pending/success/cancelled UI, but **they are not proof of payment**.

The frontend should query the backend for payment status after returning from PayOS. The backend status must be based on the verified webhook (or a server-to-server PayOS verification/reconciliation flow), not query-string values supplied by the browser.

### 6.4 Optional payment status endpoint

```http
GET /api/payments/payos/subscriptions/{orderCode}
Authorization: Bearer <customer-access-token>
```

Return only a payment attempt belonging to the authenticated user. Never allow a user to query another user's payment by guessing an order code.

## 7. Payment Service ↔ Core Service communication

Because these services run independently, use HTTP service-to-service communication. Do not share repositories, DbContexts, or database tables.

### Suggested Core internal endpoints

Agree on the exact route and DTO with the Core Service owner before implementation. Example contracts:

```http
GET /internal/subscription-plans/{planId}
```

Returns the authoritative plan ID, price, currency, duration, and active state.

```http
POST /internal/subscriptions/payments/confirm
Authorization: Bearer <service-to-service-token>
Content-Type: application/json
```

Example body:

```json
{
  "paymentReference": "123456789012",
  "userId": "user-id-from-validated-payment-attempt",
  "planId": "MONTHLY_STANDARD",
  "amount": 99000,
  "currency": "VND",
  "paidAt": "2026-11-26T06:30:00Z",
  "billingPeriodStart": "2026-11-26T06:30:00Z",
  "billingPeriodEnd": "2026-12-26T06:30:00Z",
  "provider": "PAYOS"
}
```

Example response:

```json
{
  "success": true,
  "subscriptionId": "subscription-id",
  "status": "ACTIVE",
  "expiresAt": "2026-12-26T06:30:00Z"
}
```

Core Service must make this operation idempotent using `paymentReference` (or a unique provider transaction reference). Receiving the same confirmation more than once must not extend the subscription more than once.

### Service authentication

Do not trust a `userId` sent by the frontend. The Payment Service derives it from the authenticated request and stores it with the payment attempt.

For the Payment Service → Core Service call, use the project's existing service authentication mechanism if one exists. Otherwise agree on a service-to-service credential (for example, a short-lived client-credentials JWT or a securely managed internal API key over HTTPS). Do not forward a customer's access token as the only service credential unless the architecture explicitly requires delegated user authorization.

Core Service should authorize the calling service and validate all payment confirmation fields against its own plan and subscription data. Network location alone is not authentication.

## 8. Subscription period rules

Agree with the Core Service owner on the exact business rules before coding:

- Define whether a month means a calendar month or a fixed number of days.
- Define the timezone used for billing boundaries (prefer UTC for stored timestamps).
- If the user has no active subscription, start the period at the confirmed payment time.
- If the user renews before expiry, decide whether the new period starts at the current expiry or immediately.
- If the subscription has expired, define whether renewal starts at payment time.
- Define what happens if the user changes plan.
- Define whether pending payment attempts expire and how they are reconciled.
- Do not activate a subscription when a payment is cancelled, expired, underpaid, or has an invalid signature.

A suggested renewal rule is: if the current subscription is active, extend from its existing `ExpiresAt`; otherwise start from the verified payment time. Core Service must implement this rule because it owns subscription state.

## 9. Idempotency, reliability, and security

- Add a unique database constraint for `OrderCode`.
- Store the provider transaction reference where available and make it unique when appropriate.
- Process webhook events idempotently; PayOS may deliver the same event more than once.
- Use an atomic state transition so concurrent webhook requests cannot activate twice.
- If Core Service is temporarily unavailable, do not discard the verified payment. Persist a `PAID_PENDING_CORE_CONFIRMATION` state and retry the Core confirmation safely (background worker/outbox pattern recommended).
- Do not mark the subscription active in Payment Service.
- Validate the payment amount, order code, currency, and expected local status.
- Do not expose PayOS secrets in source control or API responses.
- Apply reasonable request size limits and structured logging.
- Rate-limit customer payment-link creation to reduce abuse.
- Use HTTPS for public and internal service communication.
- Keep webhook endpoint unauthenticated by customer JWT, but protected by PayOS signature verification.
- Never activate a subscription from the frontend return URL.
- Add reconciliation for payments that remain pending for too long.

## 10. YARP / deployment configuration

Add routes for the new public Payment Service endpoints only. Example (adapt to the current YARP configuration format):

```json
{
  "ReverseProxy": {
    "Routes": {
      "payos-create-subscription": {
        "ClusterId": "payment-service",
        "Match": {
          "Path": "/api/payments/payos/subscriptions"
        }
      },
      "payos-payment-status": {
        "ClusterId": "payment-service",
        "Match": {
          "Path": "/api/payments/payos/subscriptions/{**catch-all}"
        }
      },
      "payos-webhook": {
        "ClusterId": "payment-service",
        "Match": {
          "Path": "/api/payments/payos/webhook"
        }
      }
    }
  }
}
```

Check route ordering and existing route patterns so the new routes do not shadow existing Payment Service routes. Configure the webhook URL in the PayOS merchant dashboard to point to the publicly reachable gateway route (or directly to the Payment Service if that is the deployment design).

Configure the Core Service base URL and service credential in the Payment Service deployment environment. Each service must use its own database connection string and deployment configuration.

## 11. Implementation order

- [ ] Inspect existing Payment Service structure and identify files that must remain untouched.
- [ ] Confirm authentication claim used for the customer ID.
- [ ] Agree on Core Service plan lookup and payment-confirmation contracts.
- [ ] Agree on monthly renewal and plan-change rules.
- [ ] Add the official `payOS` NuGet package.
- [ ] Add PayOS options and secret environment variables.
- [ ] Add isolated PayOS controller, contracts, service, and typed Core client.
- [ ] Add isolated payment-attempt persistence and migration if required.
- [ ] Implement authenticated payment-link creation.
- [ ] Implement raw-body webhook signature verification.
- [ ] Implement amount/order validation and idempotent processing.
- [ ] Implement Core Service confirmation and retry/outbox handling.
- [ ] Add YARP routes without changing existing routes.
- [ ] Configure the PayOS webhook URL.
- [ ] Test using PayOS sandbox/test credentials before production.
- [ ] Verify production HTTPS, secrets, logs, and webhook reachability.

## 12. Test scenarios

| Scenario | Expected result |
|---|---|
| Authenticated customer creates a payment | A pending payment attempt and checkout URL are returned |
| Unauthenticated customer creates a payment | 401 |
| Customer submits an unknown/inactive plan | Rejected; no PayOS link created |
| Customer tampers with amount or user ID | Server ignores client-supplied authoritative values |
| Valid successful webhook | Payment is verified and Core is asked to activate/extend subscription |
| Invalid webhook signature | Rejected; no subscription change |
| Webhook amount differs from local amount | Flag/reject; no subscription change |
| Duplicate successful webhook | No duplicate subscription extension |
| Core Service is unavailable | Payment remains safely recorded for retry |
| Customer opens success URL without paying | Subscription remains unchanged |
| Cancelled/expired payment | Subscription remains unchanged |
| Customer requests another user's payment status | 403 or 404 |
| Early renewal | Period follows the agreed renewal rule |
| Payment after subscription expiry | New period follows the agreed expiry rule |

## 13. Definition of done

- Existing teammate-owned Payment Service resources are unchanged.
- PayOS integration is isolated in its own feature/controller.
- Payment Service and Core Service communicate over an authenticated service-to-service API.
- Core Service remains the only service that activates or extends subscriptions.
- A verified webhook is the source of payment confirmation.
- Duplicate webhooks and Core Service retries cannot extend a subscription twice.
- Monthly renewal behaviour is documented and tested.
- YARP routes and deployment secrets are configured without breaking existing routes.

## 14. Official references

- [payOS .NET SDK documentation](https://payos.vn/docs/sdks/back-end/net/)
- [Official payOS .NET SDK repository](https://github.com/payOSHQ/payos-lib-dotnet)
- [Official .NET demo](https://github.com/payOSHQ/payos-demo-dotnet-core)
- [payOS webhook documentation](https://payos.vn/docs/du-lieu-tra-ve/webhook/)
- [payOS API documentation](https://payos.vn/docs/api/)
