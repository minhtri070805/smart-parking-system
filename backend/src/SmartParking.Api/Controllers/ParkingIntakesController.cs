using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartParking.Application.Common.Interfaces;
using SmartParking.Application.DTOs.Operations;
using SmartParking.Application.DTOs.Sessions;
using SmartParking.Application.UseCases.ParkingOperations;
using SmartParking.Domain.Constants;

namespace SmartParking.Api.Controllers;

[ApiController]
[Route("api/v1/parking-intakes")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Staff}")]
public sealed class ParkingIntakesController(
    IParkingSessionService sessions,
    ParkingOperationsService operations) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] ParkingIntakeRequestDto request, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        var actorRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (string.Equals(request.Source, "WALK_IN", StringComparison.OrdinalIgnoreCase))
        {
            var result = await operations.Enter(
                request.LotId!.Value,
                new EntryRequest(
                    request.EventId!.Value,
                    request.ZoneId!.Value,
                    request.TicketCode!.Trim(),
                    request.ImageId,
                    request.ManualReason?.Trim() ?? "Tiếp nhận xe gửi bình thường"),
                actorId ?? 0,
                actorRole,
                ct);

            return Created($"/api/v1/parking-sessions/{result.Id}", new
            {
                message = "Đã tiếp nhận xe vãng lai và tạo phiên chờ xác minh.",
                source = "WALK_IN",
                data = result
            });
        }

        var bookingResult = await sessions.CheckInBookingAtGateAsync(
            request.BookingId!.Value,
            new CheckInBookingRequestDto
            {
                PlateNumber = request.PlateNumber!.Trim(),
                CheckInImageUrl = request.CheckInImageUrl!.Trim()
            },
            actorId,
            actorRole,
            clientIp,
            ct);

        if (!bookingResult.Success)
        {
            var status = bookingResult.Message.Contains("quyền", StringComparison.OrdinalIgnoreCase)
                ? StatusCodes.Status403Forbidden
                : bookingResult.Message.Contains("không tìm thấy", StringComparison.OrdinalIgnoreCase)
                    ? StatusCodes.Status404NotFound
                    : bookingResult.Message.Contains("không còn", StringComparison.OrdinalIgnoreCase)
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status400BadRequest;
            return StatusCode(status, new { message = bookingResult.Message });
        }

        return Created($"/api/v1/parking-sessions/{bookingResult.Data!.Id}", new
        {
            message = "Đã xác nhận booking tại cổng và tạo phiên chờ điều phối.",
            source = "BOOKING",
            data = bookingResult.Data
        });
    }

    private long? GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return long.TryParse(value, out var id) && id > 0 ? id : null;
    }
}
