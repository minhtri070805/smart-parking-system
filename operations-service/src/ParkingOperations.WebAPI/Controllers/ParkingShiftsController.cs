using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingOperations.Application.UseCases.ShiftManagement;

namespace ParkingOperations.WebAPI.Controllers;

[ApiController]
[Route("api/v1/operations/shifts")]
[Produces("application/json")]
public sealed class ParkingShiftsController(IParkingShiftService shiftService, ILogger<ParkingShiftsController> logger) : ControllerBase
{
    [HttpGet("workspace")]
    [Authorize(Roles = "MANAGER")]
    public async Task<IActionResult> Workspace([FromQuery] int lotId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (lotId <= 0) return BadRequest(new { message = "lotId không hợp lệ." });
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            return BadRequest(new { message = "Khoảng thời gian from/to không hợp lệ." });
        try
        {
            return Ok(new { data = await shiftService.LoadManagerWorkspaceAsync(lotId, from, to, AccessToken(), ct) });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Core authorization check failed while listing shifts for lot {LotId}", lotId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Không xác minh được quyền với Core. Hãy thử lại sau." });
        }
    }

    [HttpPost]
    [Authorize(Roles = "MANAGER")]
    public async Task<IActionResult> Create([FromBody] CreateShiftRequest request, CancellationToken ct)
    {
        if (!TryActor(out var managerId))
            return Unauthorized(new { message = "Không xác định được người dùng từ access token." });
        try { return ToActionResult(await shiftService.CreateAsync(request, managerId, AccessToken(), ct), created: true); }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Core authorization check failed while creating shift for lot {LotId}", request.LotId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Không xác minh được quyền với Core. Hãy thử lại sau." });
        }
    }

    [HttpPost("{id:guid}/assignments")]
    [Authorize(Roles = "MANAGER")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignStaffToShiftRequest request, CancellationToken ct)
    {
        if (!TryActor(out var managerId))
            return Unauthorized(new { message = "Không xác định được người dùng từ access token." });
        try { return ToActionResult(await shiftService.AssignAsync(id, request, managerId, AccessToken(), ct)); }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Core authorization check failed while assigning staff to shift {ShiftId}", id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Không xác minh được phân công nhân viên với Core. Hãy thử lại sau." });
        }
    }

    [HttpPost("{id:guid}/start")]
    [Authorize(Roles = "MANAGER")]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct)
    {
        try { return ToActionResult(await shiftService.StartAsync(id, AccessToken(), ct)); }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Core authorization check failed while starting shift {ShiftId}", id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Không xác minh được quyền với Core. Hãy thử lại sau." });
        }
    }

    [HttpPost("{id:guid}/complete")]
    [Authorize(Roles = "MANAGER")]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
    {
        try { return ToActionResult(await shiftService.CompleteAsync(id, AccessToken(), ct)); }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Core authorization check failed while completing shift {ShiftId}", id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Không xác minh được quyền với Core. Hãy thử lại sau." });
        }
    }

    [HttpPost("{id:guid}/assignments/{assignmentId:guid}/reconcile")]
    [Authorize(Roles = "MANAGER")]
    public async Task<IActionResult> ReconcileAssignment(Guid id, Guid assignmentId, [FromBody] ReconcileAssignmentRequest request, CancellationToken ct)
    {
        if (!TryActor(out var managerId))
            return Unauthorized(new { message = "Không xác định được người dùng từ access token." });
        try
        {
            var result = await shiftService.ReconcileAssignmentAsync(id, assignmentId, request, managerId, AccessToken(), ct);
            return !result.Success
                ? StatusCode(result.StatusCode, new { message = result.Message })
                : Ok(new { message = result.Message, data = result.Data });
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Core authorization check failed while reconciling assignment {AssignmentId}", assignmentId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Không xác minh được quyền với Core. Hãy thử lại sau." });
        }
    }

    private IActionResult ToActionResult(ShiftOperationResult result, bool created = false) =>
        !result.Success ? StatusCode(result.StatusCode, new { message = result.Message })
        : created ? StatusCode(StatusCodes.Status201Created, new { message = result.Message, data = result.Data })
        : Ok(new { message = result.Message, data = result.Data });

    private string AccessToken()
    {
        var authorization = Request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization[7..].Trim()
            : string.Empty;
    }

    private bool TryActor(out long actorId)
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return long.TryParse(value, out actorId) && actorId > 0;
    }
}
