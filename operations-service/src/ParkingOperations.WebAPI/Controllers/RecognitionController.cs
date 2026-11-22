using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ParkingOperations.Application.Common.Exceptions;
using ParkingOperations.Application.Common.Interfaces;
using ParkingOperations.Application.DTOs.Common;
using ParkingOperations.Application.DTOs.Recognition;

namespace ParkingOperations.WebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/operations/recognition")]
public class RecognitionController(
    IRecognitionFacade recognitionFacade,
    ICoreHttpClient coreHttpClient,
    ILogger<RecognitionController> logger) : ControllerBase
{
    private const string CorrelationHeader = "X-Correlation-Id";

    [HttpPost("detect")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Detect(
        [FromQuery] int lotId,
        [FromQuery] string purpose,
        [FromQuery] string? captureId,
        [FromQuery] string? imageId,
        [FromQuery] string? eventId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        IFormFile? file,
        CancellationToken ct)
    {
        var correlationId = GetCorrelationId();
        var staffUserId = GetCurrentUserId();

        if (staffUserId <= 0)
        {
            return Unauthorized(ErrorResponseDto.Create("UNAUTHORIZED", "Invalid or missing authenticated staff user claim.", correlationId));
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(ErrorResponseDto.Create("INVALID_IMAGE", "An image file is required.", correlationId));
        }

        // Validate assignment with Core (Phương án 2: Forward bearer token)
        var token = await GetBearerTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Unauthorized(ErrorResponseDto.Create("UNAUTHORIZED", "Bearer token is missing.", correlationId));
        }

        var assignmentValidation = await coreHttpClient.ValidateStaffAssignmentAsync(token, lotId, requiredDuty: "GATE", correlationId, ct);
        if (!assignmentValidation.IsValid)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ErrorResponseDto.Create("FORBIDDEN_LOT_ASSIGNMENT", assignmentValidation.ErrorMessage ?? "Staff not assigned to this lot.", correlationId));
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await recognitionFacade.DetectAsync(
                stream,
                file.ContentType,
                file.Length,
                staffUserId,
                lotId,
                purpose,
                captureId,
                imageId,
                eventId,
                idempotencyKey,
                correlationId,
                ct);

            return Ok(result);
        }
        catch (ImageValidationException ex)
        {
            return BadRequest(ErrorResponseDto.Create("INVALID_INPUT", ex.Message, correlationId));
        }
        catch (RecognitionServiceException ex)
        {
            logger.LogWarning(ex, "Downstream recognition service returned {StatusCode}: {Message}", ex.StatusCode, ex.Message);
            var code = ex.StatusCode switch
            {
                System.Net.HttpStatusCode.BadRequest => "DOWNSTREAM_BAD_REQUEST",
                System.Net.HttpStatusCode.Unauthorized => "SERVICE_UNAUTHORIZED",
                System.Net.HttpStatusCode.Forbidden => "SERVICE_FORBIDDEN",
                System.Net.HttpStatusCode.NotFound => "NOT_FOUND",
                System.Net.HttpStatusCode.Conflict => "CONFLICT",
                System.Net.HttpStatusCode.GatewayTimeout => "GATEWAY_TIMEOUT",
                _ => "RECOGNITION_SERVICE_UNAVAILABLE"
            };
            return StatusCode((int)ex.StatusCode, ErrorResponseDto.Create(code, ex.Message, correlationId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error handling detect request. CorrelationId: {CorrelationId}", correlationId);
            return StatusCode(500, ErrorResponseDto.Create("INTERNAL_ERROR", "An unexpected error occurred during image detection.", correlationId));
        }
    }

    [HttpPost("{id:long}/confirm")]
    public async Task<IActionResult> Confirm(
        long id,
        [FromBody] ConfirmRecognitionRequestDto request,
        CancellationToken ct)
    {
        var correlationId = GetCorrelationId();
        var staffUserId = GetCurrentUserId();

        if (staffUserId <= 0)
        {
            return Unauthorized(ErrorResponseDto.Create("UNAUTHORIZED", "Invalid or missing authenticated staff user claim.", correlationId));
        }

        var token = await GetBearerTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Unauthorized(ErrorResponseDto.Create("UNAUTHORIZED", "Bearer token is missing.", correlationId));
        }

        var assignmentValidation = await coreHttpClient.ValidateStaffAssignmentAsync(token, request.LotId, requiredDuty: "GATE", correlationId, ct);
        if (!assignmentValidation.IsValid)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ErrorResponseDto.Create("FORBIDDEN_LOT_ASSIGNMENT", assignmentValidation.ErrorMessage ?? "Staff not assigned to this lot.", correlationId));
        }

        try
        {
            var result = await recognitionFacade.ConfirmAsync(id, request, staffUserId, correlationId, ct);
            if (result is null)
            {
                return NotFound(ErrorResponseDto.Create("NOT_FOUND", "Recognition record not found.", correlationId));
            }

            return Ok(result);
        }
        catch (ImageValidationException ex)
        {
            return BadRequest(ErrorResponseDto.Create("INVALID_INPUT", ex.Message, correlationId));
        }
        catch (RecognitionServiceException ex)
        {
            return StatusCode((int)ex.StatusCode, ErrorResponseDto.Create("RECOGNITION_ERROR", ex.Message, correlationId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error handling confirm request. CorrelationId: {CorrelationId}", correlationId);
            return StatusCode(500, ErrorResponseDto.Create("INTERNAL_ERROR", "An unexpected error occurred during confirmation.", correlationId));
        }
    }

    [HttpPost("{id:long}/confirm-exit")]
    public async Task<IActionResult> ConfirmExit(
        long id,
        [FromBody] ConfirmExitRecognitionRequestDto request,
        CancellationToken ct)
    {
        var correlationId = GetCorrelationId();
        var staffUserId = GetCurrentUserId();

        if (staffUserId <= 0)
        {
            return Unauthorized(ErrorResponseDto.Create("UNAUTHORIZED", "Invalid or missing authenticated staff user claim.", correlationId));
        }

        var token = await GetBearerTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Unauthorized(ErrorResponseDto.Create("UNAUTHORIZED", "Bearer token is missing.", correlationId));
        }

        var assignmentValidation = await coreHttpClient.ValidateStaffAssignmentAsync(token, request.LotId, requiredDuty: "GATE", correlationId, ct);
        if (!assignmentValidation.IsValid)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ErrorResponseDto.Create("FORBIDDEN_LOT_ASSIGNMENT", assignmentValidation.ErrorMessage ?? "Staff not assigned to this lot.", correlationId));
        }

        try
        {
            var result = await recognitionFacade.ConfirmExitAsync(id, request, staffUserId, correlationId, ct);
            if (result is null)
            {
                return NotFound(ErrorResponseDto.Create("NOT_FOUND", "Exit recognition record or session not found.", correlationId));
            }

            return Ok(result);
        }
        catch (ImageValidationException ex)
        {
            return BadRequest(ErrorResponseDto.Create("INVALID_INPUT", ex.Message, correlationId));
        }
        catch (RecognitionServiceException ex)
        {
            return StatusCode((int)ex.StatusCode, ErrorResponseDto.Create("RECOGNITION_ERROR", ex.Message, correlationId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error handling confirm-exit request. CorrelationId: {CorrelationId}", correlationId);
            return StatusCode(500, ErrorResponseDto.Create("INTERNAL_ERROR", "An unexpected error occurred during exit confirmation.", correlationId));
        }
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
    {
        var correlationId = GetCorrelationId();
        var staffUserId = GetCurrentUserId();

        if (staffUserId <= 0)
        {
            return Unauthorized(ErrorResponseDto.Create("UNAUTHORIZED", "Invalid or missing authenticated staff user claim.", correlationId));
        }

        try
        {
            var result = await recognitionFacade.GetByIdAsync(id, correlationId, ct);
            if (result is null)
            {
                return NotFound(ErrorResponseDto.Create("NOT_FOUND", "Recognition record not found.", correlationId));
            }

            return Ok(result);
        }
        catch (ImageValidationException ex)
        {
            return BadRequest(ErrorResponseDto.Create("INVALID_INPUT", ex.Message, correlationId));
        }
        catch (RecognitionServiceException ex)
        {
            return StatusCode((int)ex.StatusCode, ErrorResponseDto.Create("RECOGNITION_ERROR", ex.Message, correlationId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error querying recognition record. Id: {Id}, CorrelationId: {CorrelationId}", id, correlationId);
            return StatusCode(500, ErrorResponseDto.Create("INTERNAL_ERROR", "An unexpected error occurred during recognition lookup.", correlationId));
        }
    }

    private string GetCorrelationId()
    {
        if (Request.Headers.TryGetValue(CorrelationHeader, out var val) && !string.IsNullOrWhiteSpace(val))
        {
            return val.ToString();
        }

        return HttpContext.TraceIdentifier;
    }

    private long GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value
                    ?? User.FindFirst("userId")?.Value;

        if (long.TryParse(claim, out var userId) && userId > 0)
        {
            return userId;
        }

        return 0;
    }

    private async Task<string?> GetBearerTokenAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader["Bearer ".Length..].Trim();
        }

        return await HttpContext.GetTokenAsync("access_token");
    }
}
