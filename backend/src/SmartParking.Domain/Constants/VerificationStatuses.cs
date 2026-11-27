namespace SmartParking.Domain.Constants;

/// <summary>Giá trị cột VerificationStatus của ParkingSession (cột chuỗi, không có check constraint).</summary>
public static class VerificationStatuses
{
    /// <summary>
    /// Xe vào bằng vé tháng: Staff đã đối chiếu biển số với vé còn hiệu lực lúc vào. Không có vé giấy, không gán ô,
    /// không thu phí; xe ra không cần Payment clearance (PAID) nhưng vẫn phải xác nhận biển số tại cổng ra.
    /// Chỉ ParkingOperationsService.EnterWithPass được đặt giá trị này.
    /// </summary>
    public const string MonthlyPass = "MONTHLY_PASS";
}
