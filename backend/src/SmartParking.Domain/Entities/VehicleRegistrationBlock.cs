namespace SmartParking.Domain.Entities;

public class VehicleRegistrationBlock
{
    public long Id { get; set; }
    private string _plateNumber = string.Empty;
    public string PlateNumber
    {
        get => _plateNumber;
        set => _plateNumber = value?.Trim().ToUpperInvariant() ?? string.Empty;
    }
    public DateTime BlockedUntilUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
