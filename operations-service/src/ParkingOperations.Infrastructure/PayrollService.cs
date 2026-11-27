using Microsoft.EntityFrameworkCore;
using ParkingOperations.Application.Common.Interfaces;
using ParkingOperations.Application.UseCases.Payroll;
using ParkingOperations.Domain.Entities;
using ParkingOperations.Persistence.Data;

namespace ParkingOperations.Infrastructure;

public sealed class PayrollService(OperationsDbContext db, ICoreStaffAccessClient coreAccess, TimeProvider? timeProvider = null) : IPayrollService
{
    private DateTime UtcNow => (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<PayrollBatchDto>> ListBatchesAsync(int lotId, int? year, int? month, string token, CancellationToken ct)
    {
        if (lotId > 0 && !await coreAccess.IsCallerAssignedToLotAsync(token, lotId, ct))
            throw new UnauthorizedAccessException("Bạn không có quyền xem bảng lương bãi này.");

        var query = db.PayrollBatches.AsNoTracking().Include(x => x.Items).AsQueryable();
        if (lotId > 0) query = query.Where(x => x.LotId == lotId);
        if (year.HasValue) query = query.Where(x => x.Year == year.Value);
        if (month.HasValue) query = query.Where(x => x.Month == month.Value);

        var batches = await query.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToListAsync(ct);
        return batches.Select(PayrollBatchDto.From).ToList();
    }

    public async Task<PayrollOperationResult> GenerateDraftAsync(GeneratePayrollRequest request, long managerId, string token, CancellationToken ct)
    {
        if (!await coreAccess.IsCallerAssignedToLotAsync(token, request.LotId, ct))
            return Fail(403, "Bạn không được phân công quản lý bãi này.");

        var existing = await db.PayrollBatches.Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.LotId == request.LotId && x.Year == request.Year && x.Month == request.Month, ct);

        if (existing is not null && existing.Status != PayrollStatuses.Draft)
            return Fail(409, "Bảng lương tháng này đã được gửi duyệt hoặc khóa, không thể tính lại.");

        var fromDate = new DateOnly(request.Year, request.Month, 1);
        var toDate = fromDate.AddMonths(1).AddDays(-1);

        var verifiedAssignments = await db.ShiftAssignments.AsNoTracking()
            .Include(x => x.Shift)
            .Where(x => x.Shift.LotId == request.LotId
                     && x.Shift.BusinessDate >= fromDate
                     && x.Shift.BusinessDate <= toDate
                     && x.Status == ShiftAssignmentStatuses.Verified
                     && x.ClockInAtUtc != null
                     && x.ClockOutAtUtc != null)
            .ToListAsync(ct);

        var now = UtcNow;
        if (existing is null)
        {
            existing = new PayrollBatch
            {
                Id = Guid.NewGuid(),
                LotId = request.LotId,
                Year = request.Year,
                Month = request.Month,
                HourlyRate = request.HourlyRate,
                Status = PayrollStatuses.Draft,
                CreatedByManagerId = managerId,
                CreatedAtUtc = now,
                Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()
            };
            db.PayrollBatches.Add(existing);
        }
        else
        {
            existing.HourlyRate = request.HourlyRate;
            existing.Note = string.IsNullOrWhiteSpace(request.Note) ? existing.Note : request.Note.Trim();
            db.PayrollItems.RemoveRange(existing.Items);
            existing.Items.Clear();
        }

        var grouped = verifiedAssignments.GroupBy(x => x.StaffId);
        foreach (var group in grouped)
        {
            var totalHours = Math.Round((decimal)group.Sum(x => (x.ClockOutAtUtc!.Value - x.ClockInAtUtc!.Value).TotalHours), 2);
            var basePay = Math.Round(totalHours * request.HourlyRate, 0);
            var adjustment = group.Sum(x => x.AdjustmentAmount ?? 0m);
            var netPay = Math.Max(0m, basePay + adjustment);

            existing.Items.Add(new PayrollItem
            {
                Id = Guid.NewGuid(),
                PayrollBatchId = existing.Id,
                StaffId = group.Key,
                TotalShifts = group.Count(),
                TotalHours = totalHours,
                BasePay = basePay,
                AdjustmentAmount = adjustment,
                NetPay = netPay
            });
        }

        await db.SaveChangesAsync(ct);
        return Ok(existing, "Đã tổng hợp bảng lương nháp.");
    }

    public async Task<PayrollOperationResult> SubmitAsync(Guid batchId, long managerId, string token, CancellationToken ct)
    {
        var batch = await db.PayrollBatches.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == batchId, ct);
        if (batch is null) return Fail(404, "Không tìm thấy bảng lương.");
        if (!await coreAccess.IsCallerAssignedToLotAsync(token, batch.LotId, ct))
            return Fail(403, "Bạn không được phân công quản lý bãi này.");
        if (batch.Status != PayrollStatuses.Draft)
            return Fail(409, "Chỉ bảng lương nháp mới có thể gửi duyệt.");

        batch.Status = PayrollStatuses.Submitted;
        batch.SubmittedByManagerId = managerId;
        batch.SubmittedAtUtc = UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(batch, "Đã gửi bảng lương chờ Admin duyệt.");
    }

    public async Task<PayrollOperationResult> ApproveAsync(Guid batchId, long adminId, CancellationToken ct)
    {
        var batch = await db.PayrollBatches.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == batchId, ct);
        if (batch is null) return Fail(404, "Không tìm thấy bảng lương.");
        if (batch.Status != PayrollStatuses.Submitted)
            return Fail(409, "Chỉ bảng lương đã gửi mới có thể duyệt.");

        batch.Status = PayrollStatuses.Approved;
        batch.ApprovedByAdminId = adminId;
        batch.ApprovedAtUtc = UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(batch, "Admin đã duyệt bảng lương.");
    }

    public async Task<PayrollOperationResult> LockAsync(Guid batchId, long adminId, CancellationToken ct)
    {
        var batch = await db.PayrollBatches.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == batchId, ct);
        if (batch is null) return Fail(404, "Không tìm thấy bảng lương.");
        if (batch.Status != PayrollStatuses.Approved)
            return Fail(409, "Chỉ bảng lương đã duyệt mới có thể khóa.");

        batch.Status = PayrollStatuses.Locked;
        batch.LockedAtUtc = UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(batch, "Đã khóa bảng lương.");
    }

    public async Task<IReadOnlyList<PayslipDto>> GetMyPayslipsAsync(long staffId, int? year, int? month, CancellationToken ct)
    {
        var query = db.PayrollItems.AsNoTracking()
            .Include(x => x.PayrollBatch)
            .Where(x => x.StaffId == staffId
                     && (x.PayrollBatch.Status == PayrollStatuses.Approved || x.PayrollBatch.Status == PayrollStatuses.Locked));

        if (year.HasValue) query = query.Where(x => x.PayrollBatch.Year == year.Value);
        if (month.HasValue) query = query.Where(x => x.PayrollBatch.Month == month.Value);

        var items = await query
            .OrderByDescending(x => x.PayrollBatch.Year)
            .ThenByDescending(x => x.PayrollBatch.Month)
            .ToListAsync(ct);

        return items.Select(x => new PayslipDto(
            x.PayrollBatchId,
            x.PayrollBatch.LotId,
            x.PayrollBatch.Year,
            x.PayrollBatch.Month,
            x.PayrollBatch.Status,
            x.TotalShifts,
            x.TotalHours,
            x.BasePay,
            x.AdjustmentAmount,
            x.NetPay,
            x.Note)).ToList();
    }

    private static PayrollOperationResult Ok(PayrollBatch batch, string message) => new(true, 200, message, PayrollBatchDto.From(batch));
    private static PayrollOperationResult Fail(int status, string message) => new(false, status, message);
}
