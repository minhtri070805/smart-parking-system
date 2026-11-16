using System.ComponentModel.DataAnnotations;
using ParkingOperations.Application.UseCases.ShiftManagement;
using ParkingOperations.Domain.Entities;

namespace ParkingOperations.Application.UseCases.CashHandovers;

public sealed class CreateCashHandoverRequest : IValidatableObject
{
    public Guid OutgoingShiftId { get; init; }
    public Guid IncomingShiftId { get; init; }
    [Range(1, long.MaxValue)] public long OutgoingStaffId { get; init; }
    [Range(1, long.MaxValue)] public long IncomingStaffId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OutgoingShiftId == Guid.Empty || IncomingShiftId == Guid.Empty || OutgoingShiftId == IncomingShiftId)
            yield return new ValidationResult("Chọn hai ca khác nhau để bàn giao.", [nameof(OutgoingShiftId), nameof(IncomingShiftId)]);
        if (OutgoingStaffId == IncomingStaffId)
            yield return new ValidationResult("Nhân viên giao và nhận ca phải là hai người khác nhau.", [nameof(IncomingStaffId)]);
    }
}

public sealed class ConfirmCashHandoverRequest
{
    [Range(typeof(decimal), "0", "999999999999.99")] public decimal CountedAmount { get; init; }
}

public sealed class DisputeCashHandoverRequest
{
    [Required, StringLength(500, MinimumLength = 3)] public string Reason { get; init; } = string.Empty;
}

public sealed record CashHandoverDto(Guid Id, int LotId, DateOnly BusinessDate, string FromShiftCode, string ToShiftCode,
    Guid? OutgoingShiftId, Guid? IncomingShiftId, long OutgoingStaffId, long IncomingStaffId, long CreatedByManagerId,
    DateTime CreatedAtUtc, decimal? OutgoingCountedAmount, DateTime? OutgoingConfirmedAtUtc,
    decimal? IncomingCountedAmount, DateTime? IncomingConfirmedAtUtc, decimal? Variance, string? DisputeReason,
    string Status, int Version)
{
    public static CashHandoverDto From(CashHandover entity) => new(
        entity.Id, entity.LotId, entity.BusinessDate, entity.FromShiftCode, entity.ToShiftCode,
        entity.OutgoingShiftId, entity.IncomingShiftId, entity.OutgoingStaffId, entity.IncomingStaffId,
        entity.CreatedByManagerId, entity.CreatedAtUtc, entity.OutgoingCountedAmount, entity.OutgoingConfirmedAtUtc,
        entity.IncomingCountedAmount, entity.IncomingConfirmedAtUtc,
        entity.OutgoingCountedAmount.HasValue && entity.IncomingCountedAmount.HasValue
            ? entity.IncomingCountedAmount.Value - entity.OutgoingCountedAmount.Value : null,
        entity.DisputeReason, entity.Status, entity.Version);
}

public sealed record CashHandoverOperationResult(bool Success, int StatusCode, string Message, CashHandoverDto? Data = null);
public sealed record HandoverWorkspaceDto(IReadOnlyList<CashHandoverDto> Handovers, IReadOnlyList<ParkingShiftDto> Shifts, int TotalCount);

public interface ICashHandoverService
{
    Task<CashHandoverOperationResult> CreateAsync(CreateCashHandoverRequest request, long managerId, string token, CancellationToken ct);
    Task<HandoverWorkspaceDto> LoadWorkspaceAsync(int lotId, string token, long actorId, string role, CancellationToken ct);
    Task<CashHandoverOperationResult> ConfirmAsync(Guid id, decimal countedAmount, long actorId, string role, string token, CancellationToken ct);
    Task<CashHandoverOperationResult> DisputeAsync(Guid id, string reason, long actorId, string role, string token, CancellationToken ct);
}
