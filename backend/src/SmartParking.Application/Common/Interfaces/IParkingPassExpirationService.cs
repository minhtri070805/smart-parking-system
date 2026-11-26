namespace SmartParking.Application.Common.Interfaces;

public interface IParkingPassExpirationService
{
    Task<int> ExpireDuePassesAsync(CancellationToken ct = default);
}
