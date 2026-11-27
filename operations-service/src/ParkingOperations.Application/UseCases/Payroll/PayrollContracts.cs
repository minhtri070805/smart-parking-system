using System.ComponentModel.DataAnnotations;
using ParkingOperations.Domain.Entities;

namespace ParkingOperations.Application.UseCases.Payroll;

public sealed class GeneratePayrollRequest
{
    [Range(1, int.MaxValue)] public int LotId { get; init; }
    [Range(2020, 2100)] public int Year { get; init; }
    [Range(1, 12)] public int Month { get; init; }
    [Range(1000, 10_000_000)] public decimal HourlyRate { get; init; } = 30000m;
    [MaxLength(500)] public string? Note { get; init; }
}

public sealed record PayrollItemDto(
    Guid Id,
    long StaffId,
    int TotalShifts,
    decimal TotalHours,
    decimal BasePay,
    decimal AdjustmentAmount,
    decimal NetPay,
    string? Note);

public sealed record PayrollBatchDto(
    Guid Id,
    int LotId,
    int Year,
    int Month,
    decimal HourlyRate,
    string Status,
    long CreatedByManagerId,
    DateTime CreatedAtUtc,
    long? SubmittedByManagerId,
    DateTime? SubmittedAtUtc,
    long? ApprovedByAdminId,
    DateTime? ApprovedAtUtc,
    DateTime? LockedAtUtc,
    string? Note,
    IReadOnlyList<PayrollItemDto> Items)
{
    public static PayrollBatchDto From(PayrollBatch batch) => new(
        batch.Id, batch.LotId, batch.Year, batch.Month, batch.HourlyRate, batch.Status,
        batch.CreatedByManagerId, batch.CreatedAtUtc, batch.SubmittedByManagerId, batch.SubmittedAtUtc,
        batch.ApprovedByAdminId, batch.ApprovedAtUtc, batch.LockedAtUtc, batch.Note,
        batch.Items.OrderBy(x => x.StaffId)
            .Select(x => new PayrollItemDto(x.Id, x.StaffId, x.TotalShifts, x.TotalHours, x.BasePay, x.AdjustmentAmount, x.NetPay, x.Note))
            .ToList());
}

public sealed record PayslipDto(
    Guid BatchId,
    int LotId,
    int Year,
    int Month,
    string BatchStatus,
    int TotalShifts,
    decimal TotalHours,
    decimal BasePay,
    decimal AdjustmentAmount,
    decimal NetPay,
    string? Note);

public sealed record PayrollOperationResult(bool Success, int StatusCode, string Message, PayrollBatchDto? Data = null);

public interface IPayrollService
{
    Task<IReadOnlyList<PayrollBatchDto>> ListBatchesAsync(int lotId, int? year, int? month, string token, CancellationToken ct);
    Task<PayrollOperationResult> GenerateDraftAsync(GeneratePayrollRequest request, long managerId, string token, CancellationToken ct);
    Task<PayrollOperationResult> SubmitAsync(Guid batchId, long managerId, string token, CancellationToken ct);
    Task<PayrollOperationResult> ApproveAsync(Guid batchId, long adminId, CancellationToken ct);
    Task<PayrollOperationResult> LockAsync(Guid batchId, long adminId, CancellationToken ct);
    Task<IReadOnlyList<PayslipDto>> GetMyPayslipsAsync(long staffId, int? year, int? month, CancellationToken ct);
}
