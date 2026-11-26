namespace Application.Models.PayOS;

public sealed record PayOSSubscriptionPaymentResponse(
    long OrderCode,
    long PlanId,
    long VehicleId,
    decimal Amount,
    string Currency,
    DateTime BillingPeriodStartUtc,
    DateTime BillingPeriodEndUtc,
    string Status,
    string? CheckoutUrl,
    bool Replay);
