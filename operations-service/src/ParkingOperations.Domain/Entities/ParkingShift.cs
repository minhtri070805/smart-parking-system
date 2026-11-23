namespace ParkingOperations.Domain.Entities;

public static class ParkingShiftCodes
{
    public const string Morning = "MORNING";       // 05:00–13:00
    public const string Afternoon = "AFTERNOON";   // 13:00–21:00
    public const string Night = "NIGHT";           // 21:00–05:00

    public static bool IsValid(string? value) => value is Morning or Afternoon or Night;

    public static bool IsNext(string from, string to) =>
        (from, to) is (Morning, Afternoon) or (Afternoon, Night) or (Night, Morning);
}

public static class ParkingShiftStatuses
{
    public const string Planned = "PLANNED";
    public const string InProgress = "IN_PROGRESS";
    public const string Completed = "COMPLETED";
}

public static class ParkingShiftDuties
{
    public const string Gate = "GATE";
    public const string SlotVerifier = "SLOT_VERIFIER";
    public const string Both = "BOTH";

    public static bool IsValid(string? value) => value is Gate or SlotVerifier or Both;
}

public sealed class ParkingShift
{
    public Guid Id { get; set; }
    public int LotId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public string Status { get; set; } = ParkingShiftStatuses.Planned;
    public long CreatedByManagerId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int Version { get; set; }
    public ICollection<ShiftAssignment> Assignments { get; set; } = new List<ShiftAssignment>();
}

public sealed class ShiftAssignment
{
    public Guid Id { get; set; }
    public Guid ShiftId { get; set; }
    public ParkingShift Shift { get; set; } = null!;
    public long StaffId { get; set; }
    public string DutyCode { get; set; } = ParkingShiftDuties.Gate;
    public long AssignedByManagerId { get; set; }
    public DateTime AssignedAtUtc { get; set; }
}
