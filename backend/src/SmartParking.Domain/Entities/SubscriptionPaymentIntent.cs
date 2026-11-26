namespace SmartParking.Domain.Entities;

public sealed class SubscriptionPaymentIntent
{
    public long Id { get; set; }
    public long OrderCode { get; set; }
    public long OwnerUserId { get; set; }
    public long VehicleId { get; set; }
    public long PlanId { get; set; }
    public int LotId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public int TermMonths { get; set; }
    public DateTime BillingPeriodStartUtc { get; set; }
    public DateTime BillingPeriodEndUtc { get; set; }
    public string Status { get; set; } = "PENDING_PAYMENT";
    public string? ProviderReference { get; set; }
    public long? ParkingPassId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
}
