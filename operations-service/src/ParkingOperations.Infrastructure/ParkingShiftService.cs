using Microsoft.EntityFrameworkCore;
using ParkingOperations.Application.Common.Interfaces;
using ParkingOperations.Application.UseCases.ShiftManagement;
using ParkingOperations.Domain.Entities;
using ParkingOperations.Persistence.Data;

namespace ParkingOperations.Infrastructure;

public sealed class ParkingShiftService(OperationsDbContext db, ICoreStaffAccessClient coreAccess, TimeProvider? timeProvider = null) : IParkingShiftService
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private DateTime UtcNow => (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public async Task<ShiftWorkspaceDto> LoadManagerWorkspaceAsync(int lotId, DateOnly? from, DateOnly? to, string token, CancellationToken ct)
    {
        if (!await coreAccess.IsCallerAssignedToLotAsync(token, lotId, ct))
            throw new UnauthorizedAccessException("Bạn không có quyền xem ca tại bãi này.");

        var query = db.ParkingShifts.AsNoTracking().Include(x => x.Assignments)
            .Where(x => x.LotId == lotId);

        if (from.HasValue)
            query = query.Where(x => x.BusinessDate >= from.Value);
        if (to.HasValue)
            query = query.Where(x => x.BusinessDate <= to.Value);

        var ordered = query.OrderByDescending(x => x.BusinessDate).ThenBy(x => x.StartsAtUtc);
        var shifts = from.HasValue || to.HasValue
            ? await ordered.ToListAsync(ct)
            : await ordered.Take(100).ToListAsync(ct);

        var staff = await coreAccess.GetActiveStaffAtLotAsync(token, lotId, ct);
        return new ShiftWorkspaceDto(shifts.Select(ParkingShiftDto.From).ToList(), staff);
    }

    public async Task<ShiftOperationResult> CreateAsync(CreateShiftRequest request, long managerId, string token, CancellationToken ct)
    {
        if (!await coreAccess.IsCallerAssignedToLotAsync(token, request.LotId, ct))
            return Fail(403, "Bạn không được phân công quản lý bãi này.");

        var now = UtcNow;
        var (start, end) = GetShiftWindow(request.BusinessDate, request.ShiftCode);
        if (end <= now)
            return Fail(409, "Không thể tạo ca cho thời gian đã qua.");

        var shift = new ParkingShift
        {
            Id = Guid.NewGuid(),
            LotId = request.LotId,
            BusinessDate = request.BusinessDate,
            Code = request.ShiftCode,
            StartsAtUtc = start,
            EndsAtUtc = end,
            Status = ParkingShiftStatuses.Planned,
            CreatedByManagerId = managerId,
            CreatedAtUtc = now,
            Version = 1
        };

        db.ParkingShifts.Add(shift);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Fail(409, "Ca này đã tồn tại cho bãi và ngày nghiệp vụ đã chọn.");
        }
        return Ok(ParkingShiftDto.From(shift), "Đã tạo ca làm việc.");
    }

    public async Task<ShiftOperationResult> AssignAsync(Guid shiftId, AssignStaffToShiftRequest request, long managerId, string token, CancellationToken ct)
    {
        var shift = await db.ParkingShifts.Include(x => x.Assignments).FirstOrDefaultAsync(x => x.Id == shiftId, ct);
        if (shift is null)
            return Fail(404, "Không tìm thấy ca làm việc.");
        if (shift.Status != ParkingShiftStatuses.Planned)
            return Fail(409, "Chỉ được thay đổi phân công khi ca đang được lên lịch.");
        var now = UtcNow;
        if (shift.EndsAtUtc <= now)
            return Fail(409, "Không thể phân công nhân viên vào ca đã qua.");
        if (!ParkingShiftDuties.IsValid(request.DutyCode))
            return Fail(400, "dutyCode phải là GATE, SLOT_VERIFIER hoặc BOTH.");
        if (!await coreAccess.IsCallerAssignedToLotAsync(token, shift.LotId, ct))
            return Fail(403, "Bạn không được phân công quản lý bãi này.");
        if (!await coreAccess.IsActiveStaffAtLotAsync(token, shift.LotId, request.StaffId, ct))
            return Fail(400, "Nhân viên phải có phân công STAFF đang hiệu lực tại bãi này trong Core.");
        if (shift.Assignments.Any(x => x.StaffId == request.StaffId))
            return Fail(409, "Nhân viên đã được xếp vào ca này.");

        shift.Assignments.Add(new ShiftAssignment
        {
            Id = Guid.NewGuid(),
            ShiftId = shift.Id,
            StaffId = request.StaffId,
            DutyCode = request.DutyCode,
            AssignedByManagerId = managerId,
            AssignedAtUtc = DateTime.UtcNow
        });
        shift.Version++;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail(409, "Ca vừa được cập nhật bởi yêu cầu khác. Hãy tải lại dữ liệu.");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Fail(409, "Nhân viên vừa được xếp vào ca này bởi yêu cầu khác.");
        }
        return Ok(ParkingShiftDto.From(shift), "Đã phân công nhân viên vào ca.");
    }

    public async Task<ShiftOperationResult> StartAsync(Guid shiftId, string token, CancellationToken ct)
    {
        var shift = await db.ParkingShifts.Include(x => x.Assignments).FirstOrDefaultAsync(x => x.Id == shiftId, ct);
        if (shift is null) return Fail(404, "Không tìm thấy ca làm việc.");
        if (!await coreAccess.IsCallerAssignedToLotAsync(token, shift.LotId, ct))
            return Fail(403, "Bạn không được phân công quản lý bãi này.");
        if (shift.Status != ParkingShiftStatuses.Planned)
            return Fail(409, "Chỉ ca đang được lên lịch mới có thể bắt đầu.");
        if (shift.Assignments.Count == 0)
            return Fail(409, "Hãy phân công ít nhất một nhân viên trước khi bắt đầu ca.");

        var now = UtcNow;
        if (now < shift.StartsAtUtc)
            return Fail(409, "Chưa đến giờ bắt đầu ca. Chỉ có thể bắt đầu khi nằm trong khung giờ ca.");
        if (now >= shift.EndsAtUtc)
            return Fail(409, "Ca làm việc đã quá giờ kết thúc, không thể bắt đầu.");

        shift.Status = ParkingShiftStatuses.InProgress;
        shift.StartedAtUtc = now;
        shift.Version++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Fail(409, "Ca vừa được cập nhật bởi yêu cầu khác. Hãy tải lại dữ liệu."); }
        return Ok(ParkingShiftDto.From(shift), "Ca đã bắt đầu.");
    }

    public async Task<ShiftOperationResult> CompleteAsync(Guid shiftId, string token, CancellationToken ct)
    {
        var shift = await db.ParkingShifts.Include(x => x.Assignments).FirstOrDefaultAsync(x => x.Id == shiftId, ct);
        if (shift is null) return Fail(404, "Không tìm thấy ca làm việc.");
        if (!await coreAccess.IsCallerAssignedToLotAsync(token, shift.LotId, ct))
            return Fail(403, "Bạn không được phân công quản lý bãi này.");
        if (shift.Status != ParkingShiftStatuses.InProgress)
            return Fail(409, "Chỉ ca đang diễn ra mới có thể chốt.");

        shift.Status = ParkingShiftStatuses.Completed;
        shift.CompletedAtUtc = DateTime.UtcNow;
        shift.Version++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Fail(409, "Ca vừa được cập nhật bởi yêu cầu khác. Hãy tải lại dữ liệu."); }
        return Ok(ParkingShiftDto.From(shift), "Ca đã được chốt.");
    }

    public async Task<IReadOnlyList<StaffShiftDto>> GetMyShiftsAsync(long staffId, CancellationToken ct)
    {
        var assignments = await db.ShiftAssignments.AsNoTracking()
            .Include(x => x.Shift)
            .Where(x => x.StaffId == staffId)
            .OrderByDescending(x => x.Shift.BusinessDate)
            .ThenBy(x => x.Shift.StartsAtUtc)
            .Take(60)
            .ToListAsync(ct);

        return assignments.Select(ToStaffShiftDto).ToList();
    }

    public async Task<StaffShiftOperationResult> GetMyShiftDetailAsync(Guid shiftId, long staffId, CancellationToken ct)
    {
        var assignment = await db.ShiftAssignments.AsNoTracking()
            .Include(x => x.Shift)
            .FirstOrDefaultAsync(x => x.ShiftId == shiftId && x.StaffId == staffId, ct);

        return assignment is null
            ? new StaffShiftOperationResult(false, 404, "Không tìm thấy ca trực của bạn.")
            : new StaffShiftOperationResult(true, 200, "Lấy thông tin ca trực thành công.", ToStaffShiftDto(assignment));
    }

    public async Task<StaffShiftOperationResult> ClockInAsync(Guid shiftId, long staffId, CancellationToken ct)
    {
        var assignment = await db.ShiftAssignments
            .Include(x => x.Shift)
            .FirstOrDefaultAsync(x => x.ShiftId == shiftId && x.StaffId == staffId, ct);

        if (assignment is null)
            return new StaffShiftOperationResult(false, 404, "Bạn không được phân công vào ca trực này.");
        if (assignment.Shift.Status == ParkingShiftStatuses.Completed)
            return new StaffShiftOperationResult(false, 409, "Ca trực đã kết thúc, không thể Clock-in.");
        if (assignment.ClockInAtUtc.HasValue)
            return new StaffShiftOperationResult(false, 409, "Bạn đã Clock-in cho ca trực này rồi.");

        var now = UtcNow;
        if (now >= assignment.Shift.EndsAtUtc)
            return new StaffShiftOperationResult(false, 409, "Ca trực đã quá giờ kết thúc, không thể Clock-in.");

        assignment.ClockInAtUtc = now;
        assignment.Status = ShiftAssignmentStatuses.ClockedIn;
        await db.SaveChangesAsync(ct);

        return new StaffShiftOperationResult(true, 200, "Clock-in thành công.", ToStaffShiftDto(assignment));
    }

    public async Task<StaffShiftOperationResult> ClockOutAsync(Guid shiftId, long staffId, ClockOutShiftRequest request, CancellationToken ct)
    {
        var assignment = await db.ShiftAssignments
            .Include(x => x.Shift)
            .FirstOrDefaultAsync(x => x.ShiftId == shiftId && x.StaffId == staffId, ct);

        if (assignment is null)
            return new StaffShiftOperationResult(false, 404, "Bạn không được phân công vào ca trực này.");
        if (!assignment.ClockInAtUtc.HasValue)
            return new StaffShiftOperationResult(false, 409, "Bạn chưa Clock-in nên không thể Clock-out.");
        if (assignment.ClockOutAtUtc.HasValue)
            return new StaffShiftOperationResult(false, 409, "Bạn đã Clock-out cho ca trực này rồi.");

        assignment.ClockOutAtUtc = UtcNow;
        assignment.HandoverCashAmount = request.HandoverCashAmount;
        assignment.Status = ShiftAssignmentStatuses.PendingReconciliation;
        await db.SaveChangesAsync(ct);

        return new StaffShiftOperationResult(true, 200, "Clock-out thành công. Ca đã chuyển sang chờ đối soát.", ToStaffShiftDto(assignment));
    }

    public async Task<StaffShiftOperationResult> ReconcileAssignmentAsync(
        Guid shiftId, Guid assignmentId, ReconcileAssignmentRequest request, long managerId, string token, CancellationToken ct)
    {
        var assignment = await db.ShiftAssignments
            .Include(x => x.Shift)
            .FirstOrDefaultAsync(x => x.Id == assignmentId && x.ShiftId == shiftId, ct);

        if (assignment is null)
            return new StaffShiftOperationResult(false, 404, "Không tìm thấy phân công trong ca trực.");
        if (!await coreAccess.IsCallerAssignedToLotAsync(token, assignment.Shift.LotId, ct))
            return new StaffShiftOperationResult(false, 403, "Bạn không được phân công quản lý bãi này.");
        if (assignment.Status != ShiftAssignmentStatuses.PendingReconciliation && assignment.Status != ShiftAssignmentStatuses.Verified)
            return new StaffShiftOperationResult(false, 409, "Nhân viên chưa Clock-out nên chưa thể đối soát.");

        assignment.SystemCashAmount = request.SystemCashAmount;
        assignment.AdjustmentAmount = request.AdjustmentAmount ?? 0m;
        assignment.AdjustmentNote = string.IsNullOrWhiteSpace(request.AdjustmentNote) ? null : request.AdjustmentNote.Trim();
        assignment.Status = ShiftAssignmentStatuses.Verified;
        assignment.VerifiedBy = managerId;
        assignment.VerifiedAt = UtcNow;
        await db.SaveChangesAsync(ct);

        return new StaffShiftOperationResult(true, 200, "Đã đối soát và xác nhận ca của nhân viên.", ToStaffShiftDto(assignment));
    }

    private static StaffShiftDto ToStaffShiftDto(ShiftAssignment a) => new(
        a.ShiftId,
        a.Shift.LotId,
        a.Shift.BusinessDate,
        a.Shift.Code,
        a.Shift.StartsAtUtc,
        a.Shift.EndsAtUtc,
        a.Shift.Status,
        a.Id,
        a.DutyCode,
        a.Status,
        a.ClockInAtUtc,
        a.ClockOutAtUtc,
        a.HandoverCashAmount,
        a.SystemCashAmount,
        a.AdjustmentAmount,
        a.AdjustmentNote,
        a.VerifiedBy,
        a.VerifiedAt);

    private static (DateTime StartUtc, DateTime EndUtc) GetShiftWindow(DateOnly businessDate, string code)
    {
        var (startHour, endHour, endDayOffset) = code switch
        {
            ParkingShiftCodes.Morning => (5, 13, 0),
            ParkingShiftCodes.Afternoon => (13, 21, 0),
            ParkingShiftCodes.Night => (21, 5, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(code))
        };
        var localStart = new DateTimeOffset(businessDate.ToDateTime(new TimeOnly(startHour, 0)), VietnamOffset);
        var localEndDate = businessDate.AddDays(endDayOffset);
        var localEnd = new DateTimeOffset(localEndDate.ToDateTime(new TimeOnly(endHour, 0)), VietnamOffset);
        return (localStart.UtcDateTime, localEnd.UtcDateTime);
    }

    private static ShiftOperationResult Ok(ParkingShiftDto item, string message) => new(true, 200, message, item);
    private static ShiftOperationResult Fail(int status, string message) => new(false, status, message);
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };
}
