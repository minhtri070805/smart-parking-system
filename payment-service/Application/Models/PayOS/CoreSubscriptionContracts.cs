namespace Application.Models.PayOS;

public sealed record CorePrepareResponse(
    long PaymentIntentId,
    long OrderCode,
    long OwnerUserId,
    long PlanId,
    long VehicleId,
    long LotId,
    decimal Amount,
    string Currency,
    int TermMonths,
    DateTime BillingPeriodStartUtc,
    DateTime BillingPeriodEndUtc,
    DateTime ExpiresAtUtc,
    bool Replay);

public sealed record CoreConfirmResponse(
    long OrderCode,
    long? ParkingPassId,
    string Status,
    DateTime? ConfirmedAtUtc,
    bool Replay);
