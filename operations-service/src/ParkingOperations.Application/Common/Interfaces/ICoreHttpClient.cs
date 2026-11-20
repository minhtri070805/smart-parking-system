using ParkingOperations.Application.DTOs.Core;

namespace ParkingOperations.Application.Common.Interfaces;

public interface ICoreHttpClient
{
    Task<List<AssignedLotSummaryDto>> GetMyAssignedLotsAsync(
        string bearerToken,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task<ValidationContextResult> ValidateStaffAssignmentAsync(
        string bearerToken,
        int lotId,
        string? requiredDuty = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}
