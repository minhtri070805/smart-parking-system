namespace ParkingOperations.Domain.Entities;

public static class PayrollStatuses
{
    public const string Draft = "DRAFT";
    public const string Submitted = "SUBMITTED";
    public const string Approved = "APPROVED";
    public const string Locked = "LOCKED";
}

public sealed class PayrollBatch
{
    public Guid Id { get; set; }
    public int LotId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal HourlyRate { get; set; } = 30000m;
    public string Status { get; set; } = PayrollStatuses.Draft;
    public long CreatedByManagerId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public long? SubmittedByManagerId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public long? ApprovedByAdminId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? LockedAtUtc { get; set; }
    public string? Note { get; set; }
    public ICollection<PayrollItem> Items { get; set; } = new List<PayrollItem>();
}

public sealed class PayrollItem
{
    public Guid Id { get; set; }
    public Guid PayrollBatchId { get; set; }
    public PayrollBatch PayrollBatch { get; set; } = null!;
    public long StaffId { get; set; }
    public int TotalShifts { get; set; }
    public decimal TotalHours { get; set; }
    public decimal BasePay { get; set; }
    public decimal AdjustmentAmount { get; set; }
    public decimal NetPay { get; set; }
    public string? Note { get; set; }
}
