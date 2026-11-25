namespace SmartParking.Domain.Entities;

public sealed class SubscriptionPlan
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public int LotId { get; set; }
    public ParkingLot Lot { get; set; } = null!;
    public int ZoneId { get; set; }
    public ParkingZone Zone { get; set; } = null!;
    public long? SlotId { get; set; }
    public ParkingSlot? Slot { get; set; }
    public int TermMonths { get; set; } = 1;
    public decimal PriceAmount { get; set; }
    public string Currency { get; set; } = "VND";
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public string Status { get; set; } = "DRAFT"; // DRAFT, APPROVED, RETIRED
    public long CreatedBy { get; set; }
    public long? ApprovedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAtUtc { get; set; }
}
