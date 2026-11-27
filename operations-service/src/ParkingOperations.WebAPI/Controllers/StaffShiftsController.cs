using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingOperations.Application.UseCases.ShiftManagement;

namespace ParkingOperations.WebAPI.Controllers;

[ApiController]
[Route("api/v1/operations/staff")]
[Produces("application/json")]
[Authorize(Roles = "STAFF")]
public sealed class StaffShiftsController(IParkingShiftService shiftService) : ControllerBase
{
    [HttpGet("my-shifts")]
    public async Task<IActionResult> GetMyShifts(CancellationToken ct)
    {
        if (!TryActor(out var staffId))
            return Unauthorized(new { message = "Không xác định được người dùng từ access token." });

        var shifts = await shiftService.GetMyShiftsAsync(staffId, ct);
        return Ok(new { data = shifts });
    }

    [HttpGet("shifts/{shiftId:guid}")]
    public async Task<IActionResult> GetMyShiftDetail(Guid shiftId, CancellationToken ct)
    {
        if (!TryActor(out var staffId))
            return Unauthorized(new { message = "Không xác định được người dùng từ access token." });

        var result = await shiftService.GetMyShiftDetailAsync(shiftId, staffId, ct);
        return ToActionResult(result);
    }

    [HttpPost("shifts/{shiftId:guid}/clock-in")]
    public async Task<IActionResult> ClockIn(Guid shiftId, CancellationToken ct)
    {
        if (!TryActor(out var staffId))
            return Unauthorized(new { message = "Không xác định được người dùng từ access token." });

        var result = await shiftService.ClockInAsync(shiftId, staffId, ct);
        return ToActionResult(result);
    }

    [HttpPost("shifts/{shiftId:guid}/clock-out")]
    public async Task<IActionResult> ClockOut(Guid shiftId, [FromBody] ClockOutShiftRequest? request, CancellationToken ct)
    {
        if (!TryActor(out var staffId))
            return Unauthorized(new { message = "Không xác định được người dùng từ access token." });

        var result = await shiftService.ClockOutAsync(shiftId, staffId, request ?? new ClockOutShiftRequest(), ct);
        return ToActionResult(result);
    }

    private IActionResult ToActionResult(StaffShiftOperationResult result) =>
        !result.Success
            ? StatusCode(result.StatusCode, new { message = result.Message })
            : Ok(new { message = result.Message, data = result.Data });

    private bool TryActor(out long actorId)
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return long.TryParse(value, out actorId) && actorId > 0;
    }
}
