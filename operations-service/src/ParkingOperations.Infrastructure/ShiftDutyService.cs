using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParkingOperations.Application.UseCases.ShiftManagement;
using ParkingOperations.Domain.Entities;
using ParkingOperations.Persistence.Data;

namespace ParkingOperations.Infrastructure;

public sealed class ShiftDutyService(OperationsDbContext db, ILogger<ShiftDutyService> logger) : IShiftDutyService
{
    public async Task<ShiftDutyCheckResult> CheckAsync(ShiftDutyCheckRequest request, CancellationToken ct)
    {
        var atUtc = request.At.Kind == DateTimeKind.Utc ? request.At : request.At.ToUniversalTime();
        var duty = request.Duty.Trim().ToUpperInvariant();
        var shift = await db.ParkingShifts.AsNoTracking()
            .Where(x => x.LotId == request.LotId &&
                        x.Status == ParkingShiftStatuses.InProgress &&
                        x.StartsAtUtc <= atUtc && atUtc < x.EndsAtUtc &&
                        x.Assignments.Any(a => a.StaffId == request.StaffId && DutyMatches(a.DutyCode, duty)))
            .OrderByDescending(x => x.StartedAtUtc)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(ct);

        var allowed = shift is not null;
        if (!allowed)
            logger.LogInformation("Staff {StaffId} bị từ chối vì chưa có ca {Duty} đang chạy tại bãi {LotId}.", request.StaffId, duty, request.LotId);

        return new ShiftDutyCheckResult(
            allowed,
            shift?.Id,
            allowed ? null : "Staff chưa được phân vào ca đang chạy với nhiệm vụ phù hợp.");
    }

    private static bool DutyMatches(string assignedDuty, string requiredDuty)
    {
        var assigned = assignedDuty.Trim().ToUpperInvariant();
        if (assigned == ParkingShiftDuties.Both) return true;
        if (requiredDuty is "GATE" or "GATE_IN" or "GATE_OUT")
            return assigned == ParkingShiftDuties.Gate || assigned is "GATE_IN" or "GATE_OUT";
        if (requiredDuty == ParkingShiftDuties.SlotVerifier)
            return assigned == ParkingShiftDuties.SlotVerifier;
        return assigned == requiredDuty;
    }
}
