using Microsoft.EntityFrameworkCore;
using SmartParking.Application.Common.Interfaces;

namespace SmartParking.Application.Services;

public sealed class ParkingPassExpirationService(IApplicationDbContext db) : IParkingPassExpirationService
{
    private const int BatchSize = 500;

    public async Task<int> ExpireDuePassesAsync(CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var duePassIds = await db.ParkingPasses.AsNoTracking()
            .Where(pass => pass.Status == "ACTIVE" && pass.EndsAtUtc <= nowUtc)
            .OrderBy(pass => pass.EndsAtUtc)
            .Select(pass => pass.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (duePassIds.Count == 0)
            return 0;

        return await db.ParkingPasses
            .Where(pass => duePassIds.Contains(pass.Id) && pass.Status == "ACTIVE" && pass.EndsAtUtc <= nowUtc)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(pass => pass.Status, "EXPIRED")
                .SetProperty(pass => pass.UpdatedAtUtc, nowUtc)
                .SetProperty(pass => pass.Revision, pass => pass.Revision + 1), ct);
    }
}
