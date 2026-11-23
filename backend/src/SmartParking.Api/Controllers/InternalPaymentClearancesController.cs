using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartParking.Application.Common.Interfaces;
using SmartParking.Application.Common.Security;

namespace SmartParking.Api.Controllers;

public class PaymentServiceClearanceDto
{
    [Range(1, long.MaxValue)] public long SessionId { get; set; }
    [Range(0, 100_000_000)] public decimal Amount { get; set; }
    [Range(0, 100_000_000)] public decimal? TotalFee { get; set; }
    [Required, MaxLength(100)] public string TransactionRef { get; set; } = string.Empty;
    [MaxLength(50)] public string? PaymentMethod { get; set; }
    public DateTime? PaidAt { get; set; }
}

/// <summary>
/// Receiver server-to-server cho Payment Service. Xác thực bằng HMAC dùng chung (PaymentService:SharedSecret),
/// KHÔNG chấp nhận JWT. Đây là đường duy nhất được phép đặt session sang PAID.
/// Contract: xem docs/qa/S5_PAYMENT_CLEARANCE_CONTRACT.md
/// </summary>
[ApiController]
[Route("api/internal/payment-clearances")]
[AllowAnonymous]
public class InternalPaymentClearancesController : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly IParkingSessionService _sessions;
    private readonly IBookingService _bookings;
    private readonly IConfiguration _config;

    public InternalPaymentClearancesController(IParkingSessionService sessions, IBookingService bookings, IConfiguration config)
    {
        _sessions = sessions;
        _bookings = bookings;
        _config = config;
    }

    /// <summary>Đọc body thô và kiểm chữ ký HMAC của Payment Service. Trả lỗi (503/401) hoặc body đã xác thực.</summary>
    private async Task<(IActionResult? Error, string Raw)> ReadSignedBodyAsync(CancellationToken ct)
    {
        var secret = _config[$"{PaymentServiceSignature.SectionName}:SharedSecret"];
        if (!PaymentServiceSignature.IsSecretUsable(secret))
            return (StatusCode(503, new { code = "PAYMENT_INTEGRATION_NOT_CONFIGURED", message = "Core chưa cấu hình PaymentService:SharedSecret." }), "");

        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync(ct);
        if (!PaymentServiceSignature.Verify(secret,
                Request.Headers[PaymentServiceSignature.TimestampHeader], Request.Headers[PaymentServiceSignature.SignatureHeader],
                raw, DateTimeOffset.UtcNow))
            return (Unauthorized(new { code = "INVALID_SIGNATURE", message = "Chữ ký Payment Service không hợp lệ hoặc hết hạn." }), "");
        return (null, raw);
    }

    /// <summary>
    /// Payment lấy dữ kiện tính phí của một phiên (chỉ đọc). Ký giống endpoint clearance; body: { "sessionId": 123 }.
    /// </summary>
    [HttpPost("/api/internal/payment-billing-context")]
    public async Task<IActionResult> BillingContext(CancellationToken ct)
    {
        var (error, raw) = await ReadSignedBodyAsync(ct);
        if (error != null) return error;

        long sessionId;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("sessionId", out var idElement) || !idElement.TryGetInt64(out sessionId) || sessionId <= 0)
                return BadRequest(new { code = "INVALID_BODY", message = "Cần sessionId (số nguyên dương)." });
        }
        catch (JsonException) { return BadRequest(new { code = "INVALID_BODY", message = "Body không hợp lệ." }); }

        var context = await _sessions.GetPaymentBillingContextAsync(sessionId, ct);
        return context is null
            ? NotFound(new { code = "SESSION_NOT_FOUND", message = $"Không tìm thấy phiên gửi xe với ID: {sessionId}." })
            : Ok(context);
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        var (signatureError, raw) = await ReadSignedBodyAsync(ct);
        if (signatureError != null) return signatureError;

        PaymentServiceClearanceDto? dto;
        try { dto = JsonSerializer.Deserialize<PaymentServiceClearanceDto>(raw, Json); }
        catch (JsonException) { return BadRequest(new { code = "INVALID_BODY", message = "Body không hợp lệ." }); }
        string? error = null;
        if (dto == null || !TryValidate(dto, out error) || (dto.Amount == 0 && !dto.TotalFee.HasValue))
            return BadRequest(new { code = "INVALID_BODY", message = error ?? "Thanh toán 0 đồng cần kèm tổng phí để đối chiếu khoản cọc." });

        var (ok, message, data) = await _sessions.RecordPaymentClearanceAsync(
            dto.SessionId, dto.Amount, dto.TransactionRef, dto.PaymentMethod, null,
            PaymentServiceSignature.ServiceActorRole, HttpContext.Connection.RemoteIpAddress?.ToString(),
            isTrustedPaymentServerFlow: true, ct, dto.TotalFee);

        if (ok) return Ok(new { message, data });
        if (message.Contains("Không tìm thấy")) return NotFound(new { code = "SESSION_NOT_FOUND", message });
        if (message.Contains("đã kết thúc") || message.Contains("đã bị hủy") || message.Contains("đã được sử dụng") || message.Contains("giao dịch khác"))
            return Conflict(new { code = "CLEARANCE_CONFLICT", message });
        return BadRequest(new { code = "CLEARANCE_REJECTED", message });
    }

    [HttpPost("booking")]
    public async Task<IActionResult> ReceiveBookingClearance(CancellationToken ct)
    {
        var (error, raw) = await ReadSignedBodyAsync(ct);
        if (error != null) return error;
        BookingPaymentClearanceDto? dto;
        try { dto = JsonSerializer.Deserialize<BookingPaymentClearanceDto>(raw, Json); }
        catch (JsonException) { return BadRequest(new { code = "INVALID_BODY", message = "Body không hợp lệ." }); }
        if (dto == null || dto.BookingId <= 0 || dto.Amount <= 0 || string.IsNullOrWhiteSpace(dto.TransactionRef))
            return BadRequest(new { code = "INVALID_BODY", message = "Thông tin thanh toán đặt chỗ không hợp lệ." });
        var (ok, message) = await _bookings.RecordBookingPaymentAsync(
            dto.BookingId, dto.Amount, dto.TransactionRef, dto.PaymentMethod, dto.PaidAt, ct);
        if (ok) return Ok(new { message });
        return message.Contains("Không tìm thấy", StringComparison.OrdinalIgnoreCase)
            ? NotFound(new { code = "BOOKING_NOT_FOUND", message })
            : Conflict(new { code = "BOOKING_PAYMENT_REJECTED", message });
    }

    private static bool TryValidate(PaymentServiceClearanceDto dto, out string error)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(dto, new ValidationContext(dto), results, true)) { error = ""; return true; }
        error = string.Join("; ", results.Select(r => r.ErrorMessage));
        return false;
    }
}

public sealed class BookingPaymentClearanceDto
{
    public long BookingId { get; set; }
    public decimal Amount { get; set; }
    public string TransactionRef { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public DateTime? PaidAt { get; set; }
}
