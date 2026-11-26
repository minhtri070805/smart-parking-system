using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ParkingOperations.Application.UseCases.ShiftManagement;

namespace ParkingOperations.WebAPI.Controllers;

/// <summary>
/// Endpoint server-to-server để Core kiểm tra Staff có đang ở trong ca trực hay không.
/// Không dùng JWT của người dùng; request phải có HMAC X-Ops-Timestamp/X-Ops-Signature.
/// </summary>
[ApiController]
[Route("api/internal/shift-duty")]
public sealed class InternalShiftDutyController(
    IShiftDutyService shiftDutyService,
    IConfiguration configuration) : ControllerBase
{
    private const string TimestampHeader = "X-Ops-Timestamp";
    private const string SignatureHeader = "X-Ops-Signature";
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    [HttpPost("check")]
    [Produces("application/json")]
    public async Task<IActionResult> Check(CancellationToken ct)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var secret = configuration["OperationsApi:SharedSecret"];
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { allowed = false, reason = "Operations internal secret is not configured." });

        if (!long.TryParse(Request.Headers[TimestampHeader].FirstOrDefault(), out var timestamp) ||
            Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp) > MaxClockSkew.TotalSeconds)
            return Unauthorized(new { allowed = false, reason = "Invalid or expired internal timestamp." });

        var suppliedSignature = Request.Headers[SignatureHeader].FirstOrDefault();
        var expectedSignature = ComputeSignature(secret, timestamp, rawBody);
        if (string.IsNullOrWhiteSpace(suppliedSignature) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(suppliedSignature),
                Encoding.ASCII.GetBytes(expectedSignature)))
            return Unauthorized(new { allowed = false, reason = "Invalid internal signature." });

        ShiftDutyCheckRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<ShiftDutyCheckRequest>(rawBody, JsonOptions);
        }
        catch (JsonException)
        {
            return BadRequest(new { allowed = false, reason = "Invalid shift duty payload." });
        }

        if (request is null || request.StaffId <= 0 || request.LotId <= 0 || string.IsNullOrWhiteSpace(request.Duty))
            return BadRequest(new { allowed = false, reason = "staffId, lotId and duty are required." });

        var result = await shiftDutyService.CheckAsync(request, ct);
        return Ok(new { allowed = result.Allowed, shiftId = result.ShiftId, reason = result.Reason });
    }

    private static string ComputeSignature(string secret, long timestamp, string body)
    {
        var payload = Encoding.UTF8.GetBytes($"{timestamp}.{body}");
        var key = Encoding.UTF8.GetBytes(secret);
        return Convert.ToHexString(HMACSHA256.HashData(key, payload)).ToLowerInvariant();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
