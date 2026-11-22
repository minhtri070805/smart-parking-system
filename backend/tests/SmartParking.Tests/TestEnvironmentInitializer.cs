using System.Runtime.CompilerServices;

namespace SmartParking.Tests;

/// <summary>
/// Core khởi động bắt buộc có ConnectionStrings:AiRecognitionConnection. Test không kết nối DB thật,
/// nên cấp một chuỗi giả (EF/Npgsql chỉ mở kết nối khi truy vấn) cho mọi WebApplicationFactory.
/// Không ghi đè nếu máy chạy test đã cấu hình sẵn.
/// </summary>
internal static class TestEnvironmentInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        const string key = "ConnectionStrings__AiRecognitionConnection";
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            Environment.SetEnvironmentVariable(key, "Host=127.0.0.1;Database=recognition_test;Username=test;Password=unused");
    }
}
