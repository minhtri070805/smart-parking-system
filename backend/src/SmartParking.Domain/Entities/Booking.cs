namespace SmartParking.Domain.Entities;

public class Booking
{
    public long Id { get; set; }

    public int LotId { get; set; }
    public ParkingLot Lot { get; set; } = null!;

    public long UserId { get; set; }
    public User User { get; set; } = null!;

    public long VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;

    public long? ParkingSlotId { get; set; }
    public ParkingSlot? ParkingSlot { get; set; }

    private string _plateNumber = string.Empty;
    public string PlateNumber
    {
        get => _plateNumber;
        set => _plateNumber = value?.Trim().ToUpperInvariant() ?? string.Empty;
    }

    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }

    public string Status { get; set; } = "PENDING"; // PENDING, CHECKED_IN, CANCELLED, EXPIRED

    public string PaymentMode { get; set; } = "PAY_AT_COUNTER"; // PREPAY, PAY_AT_COUNTER
    public string PaymentStatus { get; set; } = "PAY_AT_COUNTER"; // PENDING, PAID, PAY_AT_COUNTER, FAILED
    public decimal? PrepaidAmount { get; set; }
    public string? PaymentInvoiceId { get; set; }
    public string? PaymentTransactionRef { get; set; }
    public DateTime? PaymentPaidAt { get; set; }

    public ParkingSession? Session { get; set; }

    public DateTime? CheckedInAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }

    public string? Notes { get; set; }
    public long? PolicyId { get; set; }
    public ParkingPolicy? Policy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
