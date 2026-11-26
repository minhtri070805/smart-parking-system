using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartParking.Application.Common.Exceptions;
using SmartParking.Application.Common.Interfaces;
using SmartParking.Application.Common.Models;
using SmartParking.Application.DTOs.StaffAssignments;
using SmartParking.Domain.Constants;
using SmartParking.Infrastructure.Services;

namespace SmartParking.Api.Controllers;

[ApiController]
[Route("api/v1/staff-assignments")]
[Produces("application/json")]
public class StaffAssignmentsController : ControllerBase
{
    private readonly IStaffLotAssignmentService _assignmentService;
    private readonly ILogger<StaffAssignmentsController> _logger;
    private readonly IStaffDetectionService _detectionService;

    public StaffAssignmentsController(
        IStaffLotAssignmentService assignmentService,
        ILogger<StaffAssignmentsController> logger,
        IStaffDetectionService detectionService)
    {
        _assignmentService = assignmentService;
        _logger = logger;
        _detectionService = detectionService;
    }

    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> GetLotAssignments([FromQuery] StaffAssignmentFilterDto filter, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue) return Unauthorized(new { message = "Không xác định được tài khoản." });
        var actorRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        var result = await _assignmentService.GetAssignmentsAsync(filter, actorId.Value, actorRole, ct);
        return Ok(new { message = "Lấy danh sách phân công thành công.", data = result });
    }

    [HttpGet("candidates")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> GetAssignmentCandidates([FromQuery] StaffCandidateFilterDto filter, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue) return Unauthorized(new { message = "Không xác định được tài khoản." });
        var result = await _assignmentService.GetCandidatesAsync(filter, actorId.Value, ct);
        return Ok(new { message = "Lấy danh sách nhân sự thành công.", data = result });
    }

    [HttpGet("locations/{lotId:int}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> GetAssignmentLocations(int lotId, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue) return Unauthorized(new { message = "Không xác định được tài khoản." });
        var result = await _assignmentService.GetLocationsAsync(lotId, actorId.Value, ct);
        return result is null
            ? NotFound(new { message = "Không tìm thấy bãi hoặc bạn không có quyền truy cập." })
            : Ok(new { message = "Lấy tầng và khu vực thành công.", data = result });
    }

    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> CreateLotAssignment([FromBody] CreateStaffAssignmentRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue) return Unauthorized(new { message = "Không xác định được tài khoản." });
        var result = await _assignmentService.CreateAssignmentAsync(request, actorId.Value,
            HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return result.Success
            ? CreatedAtAction(nameof(GetLotAssignments), new { lotId = request.LotId }, new { message = result.Message, data = result.Data })
            : Conflict(new { message = result.Message });
    }

    [HttpPost("{id:long}/revoke")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> RevokeLotAssignment(long id, [FromBody] RevokeStaffAssignmentRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue) return Unauthorized(new { message = "Không xác định được tài khoản." });
        var result = await _assignmentService.RevokeAssignmentAsync(id, request, actorId.Value,
            HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return result.Success ? Ok(new { message = result.Message }) : Conflict(new { message = result.Message });
    }

    [HttpPost("/api/v1/staff/detect")]
    [Authorize(Roles = $"{Roles.Staff},{Roles.Admin}")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Detect(IFormFile? file, [FromQuery] int lotId, [FromQuery] string purpose = "ENTRY", CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "A non-empty image file is required in the 'file' field." });

        if (lotId <= 0 || purpose is not ("ENTRY" or "EXIT"))
            return BadRequest(new { message = "A valid lotId and purpose (ENTRY or EXIT) are required." });

        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized(new { message = "Không xác định được danh tính người dùng." });

        try
        {
            if (!await IsAssignedToLot(actorId.Value, lotId, ct)
                && !string.Equals(User.FindFirst(ClaimTypes.Role)?.Value, Roles.Admin, StringComparison.Ordinal))
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You are not assigned to gate duty at this parking lot." });
            await using var stream = file.OpenReadStream();
            var result = await _detectionService.DetectAsync(
                stream, file.ContentType, file.Length, actorId.Value, lotId, purpose,
                Request.Query["captureId"].ToString(), Request.Query["imageId"].ToString(),
                Request.Query["eventId"].ToString(), Request.Headers["Idempotency-Key"].ToString(),
                Request.Headers["X-Correlation-Id"].ToString(), ct);
            return Ok(new { message = "Image recognized successfully.", data = result });
        }
        catch (ImageValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (AiServiceException exception)
        {
            return StatusCode((int)exception.StatusCode, new { message = exception.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "AI service is unavailable." });
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(exception,
                "Failed to persist AI recognition result for staff user {StaffUserId}.", actorId.Value);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "AI recognition database is unavailable." });
        }
    }

    [HttpPost("/api/v1/staff/detections/{id:long}/confirm")]
    [Authorize(Roles = $"{Roles.Staff},{Roles.Admin}")]
    [ProducesResponseType(typeof(StaffDetectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmDetection(
        long id, [FromBody] ConfirmStaffDetectionRequestDto request, CancellationToken ct)
    {
        var plateNumber = request.PlateNumber?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(plateNumber) || plateNumber.Length > 32
            || string.IsNullOrWhiteSpace(request.Note) && !System.Text.RegularExpressions.Regex.IsMatch(
                plateNumber, "^[A-Z0-9.-]+$"))
            return BadRequest(new { message = "A plate number between 1 and 32 characters is required." });

        var actorId = GetCurrentUserId();
        if (!actorId.HasValue)
            return Unauthorized(new { message = "Cannot identify the current user." });

        try
        {
            if (request.LotId <= 0) return BadRequest(new { message = "A valid lotId is required." });
            if (!await IsAssignedToLot(actorId.Value, request.LotId, ct)
                && !string.Equals(User.FindFirst(ClaimTypes.Role)?.Value, Roles.Admin, StringComparison.Ordinal))
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You are not assigned to gate duty at this parking lot." });
            var result = await _detectionService.ConfirmAsync(id, plateNumber,
                request.Note?.Trim(), actorId.Value, request.LotId, null, null, null,
                Request.Headers["Idempotency-Key"].ToString(), Request.Headers["X-Correlation-Id"].ToString(), ct);
            return result is null ? NotFound(new { message = "Recognition record was not found." }) : Ok(new { data = result });
        }
        catch (ImageValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/api/v1/staff/detections/{id:long}/confirm-exit")]
    [Authorize(Roles = $"{Roles.Staff},{Roles.Admin}")]
    public async Task<IActionResult> ConfirmExitDetection(
        long id, [FromBody] ConfirmExitStaffDetectionRequestDto request, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue) return Unauthorized(new { message = "Cannot identify the current user." });
        var plate = request.PlateNumber?.Trim().ToUpperInvariant();
        if (request.LotId <= 0 || request.SessionId <= 0 || string.IsNullOrWhiteSpace(plate) || plate.Length > 32)
            return BadRequest(new { message = "Valid lotId, sessionId and plateNumber are required." });
        if (!await IsAssignedToLot(actorId.Value, request.LotId, ct)
            && !string.Equals(User.FindFirst(ClaimTypes.Role)?.Value, Roles.Admin, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "You are not assigned to this parking lot." });
        try
        {
            var result = await _detectionService.ConfirmExitAsync(id, request.SessionId, plate,
                request.Note?.Trim(), actorId.Value, request.LotId, null, null,
                Request.Headers["Idempotency-Key"].ToString(), Request.Headers["X-Correlation-Id"].ToString(), ct);
            return result is null ? NotFound(new { message = "Recognition record was not found." }) : Ok(new { data = result });
        }
        catch (ImageValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (AiServiceException exception)
        {
            return StatusCode((int)exception.StatusCode, new { message = exception.Message });
        }
    }

    /// <summary>
    /// Trả về toàn bộ danh sách nhân viên vai trò STAFF để phân công ca trực tự do tại bãi xe.
    /// </summary>
    [HttpGet("staff")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> GetAssignments([FromQuery] int? lotId, [FromQuery] long? userId, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (!actorId.HasValue) return Unauthorized();

        var candidates = await _assignmentService.GetCandidatesAsync(new StaffCandidateFilterDto { Page = 1, PageSize = 100 }, actorId.Value, ct);
        var items = candidates.Items
            .Where(x => !userId.HasValue || x.Id == userId.Value)
            .Select(x => new
            {
                userId = x.Id,
                userFullName = string.IsNullOrWhiteSpace(x.FullName) || x.FullName == "string" ? x.Username : x.FullName,
                userRole = Roles.Staff
            }).ToList();

        return Ok(new
        {
            message = "Lấy danh sách nhân viên thành công.",
            data = new
            {
                items,
                totalCount = items.Count
            }
        });
    }

    /// <summary>
    /// Lấy danh sách các bãi đỗ xe mà tài khoản đang đăng nhập (MANAGER hoặc STAFF) được phân công làm việc.
    /// </summary>
    [HttpGet("my-lots")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Staff}")]
    [ProducesResponseType(typeof(List<AssignedLotSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyAssignedLots(CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Unauthorized(new { message = "Không xác định được danh tính người dùng." });
        }

        var lots = await _assignmentService.GetMyAssignedLotsAsync(currentUserId.Value, ct);
        return Ok(new
        {
            message = "Lấy danh sách bãi xe được phân công thành công.",
            data = lots
        });
    }

    private long? GetCurrentUserId()
    {
        var claimValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (long.TryParse(claimValue, out var userId))
        {
            return userId;
        }

        return null;
    }

    private async Task<bool> IsAssignedToLot(long userId, int lotId, CancellationToken ct)
    {
        var lots = await _assignmentService.GetMyAssignedLotsAsync(userId, ct);
        return lots.Any(x => x.LotId == lotId);
    }
}
