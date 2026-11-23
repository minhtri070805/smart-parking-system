using SmartParking.Application.Common.Interfaces;
using SmartParking.Domain.Entities;

namespace SmartParking.Application.Common.Helpers;

public static class ParkingPolicyResolver
{
    // Operational policies are temporarily disabled during the demo; stored policies remain intact.
    public static Task<ParkingPolicy?> ResolveAsync(IApplicationDbContext db, int lotId,
        int? zoneId, DateTime at, CancellationToken ct = default) => Task.FromResult<ParkingPolicy?>(null);
}
