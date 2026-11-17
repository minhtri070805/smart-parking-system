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
    [Range(0.01, 100_000_000)] public decimal Amount { get; set; }
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
    private readonly IConfiguration _config;

    public InternalPaymentClearancesController(IParkingSessionService sessions, IConfiguration config)
    {
        _sessions = sessions;
        _config = config;
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        var secret = _config[$"{PaymentServiceSignature.SectionName}:SharedSecret"];
        if (!PaymentServiceSignature.IsSecretUsable(secret))
            return StatusCode(503, new { code = "PAYMENT_INTEGRATION_NOT_CONFIGURED", message = "Core chưa cấu hình PaymentService:SharedSecret." });

        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync(ct);
        if (!PaymentServiceSignature.Verify(secret,
                Request.Headers[PaymentServiceSignature.TimestampHeader], Request.Headers[PaymentServiceSignature.SignatureHeader],
                raw, DateTimeOffset.UtcNow))
            return Unauthorized(new { code = "INVALID_SIGNATURE", message = "Chữ ký Payment Service không hợp lệ hoặc hết hạn." });

        PaymentServiceClearanceDto? dto;
        try { dto = JsonSerializer.Deserialize<PaymentServiceClearanceDto>(raw, Json); }
        catch (JsonException) { return BadRequest(new { code = "INVALID_BODY", message = "Body không hợp lệ." }); }
        string? error = null;
        if (dto == null || !TryValidate(dto, out error)) return BadRequest(new { code = "INVALID_BODY", message = error ?? "Body trống." });

        var (ok, message, data) = await _sessions.RecordPaymentClearanceAsync(
            dto.SessionId, dto.Amount, dto.TransactionRef, dto.PaymentMethod, null,
            PaymentServiceSignature.ServiceActorRole, HttpContext.Connection.RemoteIpAddress?.ToString(),
            isTrustedPaymentServerFlow: true, ct);

        if (ok) return Ok(new { message, data });
        if (message.Contains("Không tìm thấy")) return NotFound(new { code = "SESSION_NOT_FOUND", message });
        if (message.Contains("đã kết thúc") || message.Contains("đã bị hủy") || message.Contains("đã được sử dụng") || message.Contains("giao dịch khác"))
            return Conflict(new { code = "CLEARANCE_CONFLICT", message });
        return BadRequest(new { code = "CLEARANCE_REJECTED", message });
    }

    private static bool TryValidate(PaymentServiceClearanceDto dto, out string error)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(dto, new ValidationContext(dto), results, true)) { error = ""; return true; }
        error = string.Join("; ", results.Select(r => r.ErrorMessage));
        return false;
    }
}
