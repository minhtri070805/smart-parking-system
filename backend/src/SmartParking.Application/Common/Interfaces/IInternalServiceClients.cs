namespace SmartParking.Application.Common.Interfaces;

/// <summary>Kết quả nhận diện biển số do Recognition Service sở hữu; Core chỉ đọc qua API.</summary>
public sealed record ExitRecognitionRecord(
    long Id,
    string? Purpose,
    int? ParkingLotId,
    long StaffUserId,
    long? MatchedParkingSessionId,
    string? ConfirmedPlateNumber,
    DateTime? ConfirmedAt);

/// <param name="ServiceAvailable">false khi không hỏi được Recognition Service (lỗi mạng, timeout, sai khóa…).</param>
/// <param name="Record">null khi service trả lời nhưng không có bản ghi.</param>
public sealed record ExitRecognitionLookupResult(bool ServiceAvailable, ExitRecognitionRecord? Record);

/// <summary>Tra cứu kết quả nhận diện lúc xe ra. Core không truy vấn DB của Recognition Service.</summary>
public interface IExitRecognitionLookup
{
    Task<ExitRecognitionLookupResult> FindAsync(long recognitionId, CancellationToken ct);
}

/// <summary>
/// Hỏi Operations Service xem nhân viên có đang trong ca trực đúng nhiệm vụ tại bãi hay không.
/// Chỉ được đăng ký khi cấu hình StaffDuty:Source = OperationsShifts. Mọi lỗi phải trả false (fail-closed).
/// </summary>
public interface IStaffShiftDutyClient
{
    Task<bool> IsOnDutyAsync(long staffId, int lotId, string duty, CancellationToken ct);
}
