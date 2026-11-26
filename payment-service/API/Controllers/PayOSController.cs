using System.Security.Claims;
using System.Text;
using API.Contracts.PayOS;
using Application.Exceptions.PayOS;
using Application.Interfaces.PayOS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/payments/payos")]
public sealed class PayOSController(IPayOSSubscriptionPaymentService payments, ILogger<PayOSController> logger) : ControllerBase
{
    [HttpPost("subscriptions")]
    [Authorize(Policy = "USER")]
    public async Task<IActionResult> CreateSubscription([FromBody] CreateSubscriptionPaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized(new { code = "INVALID_USER_CLAIM" });
        try { return Ok(await payments.CreateAsync(userId.Value, request.PlanId, request.VehicleId, idempotencyKey, ct)); }
        catch (PayOSRequestException ex) { return StatusCode(ex.StatusCode, new { code = ex.Code }); }
    }

    [HttpGet("subscriptions/{orderCode:long}")]
    [Authorize(Policy = "USER")]
    public async Task<IActionResult> GetSubscriptionPayment(long orderCode, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized(new { code = "INVALID_USER_CLAIM" });
        var result = await payments.GetAsync(userId.Value, orderCode, ct);
        return result is null ? NotFound(new { code = "PAYMENT_NOT_FOUND" }) : Ok(result);
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var rawBody = await reader.ReadToEndAsync(ct);
        try
        {
            if (!await payments.HandleWebhookAsync(rawBody, ct))
                return BadRequest(new { code = "WEBHOOK_PAYMENT_MISMATCH" });
            return Ok(new { success = true });
        }
        catch (PayOSRequestException ex) { return StatusCode(ex.StatusCode, new { code = ex.Code }); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PayOS webhook verification or processing failed.");
            return BadRequest(new { code = "INVALID_PAYOS_WEBHOOK" });
        }
    }

    private long? CurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return long.TryParse(claim, out var userId) && userId > 0 ? userId : null;
    }
}

