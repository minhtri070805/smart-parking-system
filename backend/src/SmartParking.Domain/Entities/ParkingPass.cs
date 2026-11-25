namespace SmartParking.Domain.Entities;

public sealed class ParkingPass
{
    public long Id { get; set; }
    public long OwnerUserId { get; set; }
    public User Owner { get; set; } = null!;
    public long VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;
    public long PlanId { get; set; }
    public SubscriptionPlan Plan { get; set; } = null!;
    public int PlanVersion { get; set; }
    public int LotId { get; set; }
    public int ZoneId { get; set; }
    public long? SlotId { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public string Status { get; set; } = "PENDING"; // PENDING, SCHEDULED, ACTIVE, EXPIRED, SUSPENDED, CANCELLED
    public decimal PriceAmount { get; set; }
    public string Currency { get; set; } = "VND";
    public Guid? PaymentInvoiceId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public long Revision { get; set; }
}
