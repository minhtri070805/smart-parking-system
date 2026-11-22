using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartParking.Application.Common.Interfaces;
using SmartParking.Application.Common.Security;
using SmartParking.Persistence.Data;

namespace SmartParking.Infrastructure.Services;

/// <summary>Core → Recognition Service: GET internal/recognition/{id} với khóa nội bộ.</summary>
public sealed class HttpExitRecognitionLookup(HttpClient client, ILogger<HttpExitRecognitionLookup> logger) : IExitRecognitionLookup
{
    public async Task<ExitRecognitionLookupResult> FindAsync(long recognitionId, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync($"internal/recognition/{recognitionId}", ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new ExitRecognitionLookupResult(true, null);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Recognition Service trả {Status} khi tra cứu nhận diện {Id}", (int)response.StatusCode, recognitionId);
                return new ExitRecognitionLookupResult(false, null);
            }
            var record = await response.Content.ReadFromJsonAsync<ExitRecognitionRecord>(cancellationToken: ct);
            return new ExitRecognitionLookupResult(record is not null, record);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            if (ct.IsCancellationRequested) throw;
            logger.LogWarning(ex, "Không gọi được Recognition Service khi tra cứu nhận diện {Id}", recognitionId);
            return new ExitRecognitionLookupResult(false, null);
        }
    }
}

/// <summary>
/// TẠM THỜI: đọc thẳng DB nhận diện khi chưa cấu hình RecognitionService:BaseUrl.
/// Vi phạm ranh giới service; chỉ để chạy demo cho tới khi Recognition Service được triển khai.
/// </summary>
public sealed class SharedDatabaseExitRecognitionLookup(AiRecognitionDbContext db) : IExitRecognitionLookup
{
    public async Task<ExitRecognitionLookupResult> FindAsync(long recognitionId, CancellationToken ct)
    {
        var record = await db.RecognitionRecords.AsNoTracking()
            .Where(x => x.Id == recognitionId)
            .Select(x => new ExitRecognitionRecord(x.Id, x.Purpose, x.ParkingLotId, x.StaffUserId,
                x.MatchedParkingSessionId, x.ConfirmedPlateNumber, x.ConfirmedAt))
            .FirstOrDefaultAsync(ct);
        return new ExitRecognitionLookupResult(true, record);
    }
}

/// <summary>
/// Core → Operations Service: POST api/internal/shift-duty/check, ký HMAC (X-Ops-Timestamp / X-Ops-Signature).
/// Mọi lỗi, timeout hoặc phản hồi không hợp lệ đều trả false (fail-closed).
/// </summary>
public sealed class OperationsShiftDutyClient(
    HttpClient client, string sharedSecret, ILogger<OperationsShiftDutyClient> logger) : IStaffShiftDutyClient
{
    public const string TimestampHeader = "X-Ops-Timestamp";
    public const string SignatureHeader = "X-Ops-Signature";
    public const string Path = "api/internal/shift-duty/check";

    private sealed record DutyCheckResponse(bool Allowed, string? ShiftId, string? Reason);

    public async Task<bool> IsOnDutyAsync(long staffId, int lotId, string duty, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { staffId, lotId, duty, at = DateTime.UtcNow },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(TimestampHeader, timestamp.ToString());
        request.Headers.Add(SignatureHeader, InternalRequestSignature.Compute(sharedSecret, timestamp, body));
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Operations Service trả {Status} khi kiểm ca của staff {StaffId} tại bãi {LotId}", (int)response.StatusCode, staffId, lotId);
                return false;
            }
            var result = await response.Content.ReadFromJsonAsync<DutyCheckResponse>(cancellationToken: ct);
            return result?.Allowed == true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            if (ct.IsCancellationRequested) throw;
            logger.LogWarning(ex, "Không gọi được Operations Service khi kiểm ca của staff {StaffId} tại bãi {LotId}", staffId, lotId);
            return false;
        }
    }
}
