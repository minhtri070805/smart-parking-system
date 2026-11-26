namespace API.Contracts.PayOS;

public sealed record CreateSubscriptionPaymentRequest(long PlanId, long VehicleId);
