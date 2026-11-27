namespace ParkingOperations.Application.UseCases.ShiftManagement;

public sealed record ShiftDutyCheckRequest(long StaffId, int LotId, string Duty, DateTime At);

public sealed record ShiftDutyCheckResult(bool Allowed, Guid? ShiftId, string? Reason);

public interface IShiftDutyService
{
    Task<ShiftDutyCheckResult> CheckAsync(ShiftDutyCheckRequest request, CancellationToken ct);
}
