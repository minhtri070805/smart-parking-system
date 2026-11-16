using ParkingOperations.Application.UseCases.ShiftManagement;

namespace ParkingOperations.Application.Common.Interfaces;

public interface ICoreStaffAccessClient
{
    Task<bool> IsCallerAssignedToLotAsync(string bearerToken, int lotId, CancellationToken ct);
    Task<bool> IsActiveStaffAtLotAsync(string bearerToken, int lotId, long staffId, CancellationToken ct);
    Task<bool> AreActiveStaffAtLotAsync(string bearerToken, int lotId, long firstStaffId, long secondStaffId, CancellationToken ct);
    Task<IReadOnlyList<StaffOptionDto>> GetActiveStaffAtLotAsync(string bearerToken, int lotId, CancellationToken ct);
}
