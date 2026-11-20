using System.ComponentModel.DataAnnotations;
using ParkingOperations.Domain.Entities;

namespace ParkingOperations.Application.UseCases.ShiftManagement;

public sealed class CreateShiftRequest : IValidatableObject
{
    [Range(1, int.MaxValue)] public int LotId { get; init; }
    public DateOnly BusinessDate { get; init; }
    [Required, MaxLength(20)] public string ShiftCode { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BusinessDate == default)
            yield return new ValidationResult("businessDate phải có giá trị.", [nameof(BusinessDate)]);
        if (!ParkingShiftCodes.IsValid(ShiftCode))
            yield return new ValidationResult("shiftCode phải là MORNING, AFTERNOON hoặc NIGHT.", [nameof(ShiftCode)]);
    }
}

public sealed class AssignStaffToShiftRequest
{
    [Range(1, long.MaxValue)] public long StaffId { get; init; }
    [Required, MaxLength(32)] public string DutyCode { get; init; } = string.Empty;
}

public sealed record ShiftAssignmentDto(Guid Id, long StaffId, string DutyCode, long AssignedByManagerId, DateTime AssignedAtUtc);
public sealed record StaffOptionDto(long UserId, string UserFullName);
public sealed record ParkingShiftDto(Guid Id, int LotId, DateOnly BusinessDate, string Code, DateTime StartsAtUtc, DateTime EndsAtUtc,
    string Status, long CreatedByManagerId, DateTime CreatedAtUtc, DateTime? StartedAtUtc, DateTime? CompletedAtUtc,
    int Version, IReadOnlyList<ShiftAssignmentDto> Assignments)
{
    public static ParkingShiftDto From(ParkingShift shift) => new(
        shift.Id, shift.LotId, shift.BusinessDate, shift.Code, shift.StartsAtUtc, shift.EndsAtUtc,
        shift.Status, shift.CreatedByManagerId, shift.CreatedAtUtc, shift.StartedAtUtc,
        shift.CompletedAtUtc, shift.Version, shift.Assignments.OrderBy(x => x.StaffId)
            .Select(x => new ShiftAssignmentDto(x.Id, x.StaffId, x.DutyCode, x.AssignedByManagerId, x.AssignedAtUtc)).ToList());
}
public sealed record ShiftWorkspaceDto(IReadOnlyList<ParkingShiftDto> Shifts, IReadOnlyList<StaffOptionDto> Staff);
public sealed record ShiftOperationResult(bool Success, int StatusCode, string Message, ParkingShiftDto? Data = null);

public interface IParkingShiftService
{
    Task<ShiftWorkspaceDto> LoadManagerWorkspaceAsync(int lotId, string token, CancellationToken ct);
    Task<ShiftOperationResult> CreateAsync(CreateShiftRequest request, long managerId, string token, CancellationToken ct);
    Task<ShiftOperationResult> AssignAsync(Guid shiftId, AssignStaffToShiftRequest request, long managerId, string token, CancellationToken ct);
    Task<ShiftOperationResult> StartAsync(Guid shiftId, string token, CancellationToken ct);
    Task<ShiftOperationResult> CompleteAsync(Guid shiftId, string token, CancellationToken ct);
}
