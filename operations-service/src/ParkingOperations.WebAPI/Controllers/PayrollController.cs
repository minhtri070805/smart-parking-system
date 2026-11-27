using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingOperations.Application.UseCases.Payroll;

namespace ParkingOperations.WebAPI.Controllers;

[ApiController]
[Route("api/v1/operations/payroll")]
[Produces("application/json")]
public sealed class PayrollController(IPayrollService payrollService, ILogger<PayrollController> logger) : ControllerBase
{
    [HttpGet("batches")]
    [Authorize(Roles = "MANAGER,ADMIN")]
    public async Task<IActionResult> ListBatches([FromQuery] int lotId, [FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        try
        {
            var data = await payrollService.ListBatchesAsync(lotId, year, month, AccessToken(), ct);
            return Ok(new { data });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Core authorization check failed while listing payroll batches for lot {LotId}", lotId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Không xác minh được quyền với Core. Hãy thử lại sau." });
        }
    }

    [HttpPost("batches/generate")]
    [Authorize(Roles = "MANAGER")]
    public async Task<IActionResult> Generate([FromBody] GeneratePayrollRequest request, CancellationToken ct)
    {
        if (!TryActor(out var managerId))
            return Unauthorized(new { message = "Không xác định được người dùng từ access token." });
        try
        {
            return ToActionResult(await payrollService.GenerateDraftAsync(request, managerId, AccessToken(), ct));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Core authorization check failed while generating payroll for lot {LotId}", request.LotId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Không xác minh được quyền với Core. Hãy thử lại sau." });
        }
    }

    [HttpPost("batches/{id:guid}/submit")]
    [Authorize(Roles = "MANAGER")]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        if (!TryActor(out var managerId))
            return Unauthorized(new { message = "Không xác định được người dùng từ access token." });
        try
        {
            return ToActionResult(await payrollService.SubmitAsync(id, managerId, AccessToken(), ct));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Core authorization check failed while submitting payroll batch {BatchId}", id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Không xác minh được quyền với Core. Hãy thử lại sau." });
        }
    }

    [HttpPost("batches/{id:guid}/approve")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        if (!TryActor(out var adminId))
            return Unauthorized(new { message = "Không xác định được người dùng từ access token." });

        return ToActionResult(await payrollService.ApproveAsync(id, adminId, ct));
    }

    [HttpPost("batches/{id:guid}/lock")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Lock(Guid id, CancellationToken ct)
    {
        if (!TryActor(out var adminId))
            return Unauthorized(new { message = "Không xác định được người dùng từ access token." });

        return ToActionResult(await payrollService.LockAsync(id, adminId, ct));
    }

    [HttpGet("my-payslips")]
    [Authorize(Roles = "STAFF,MANAGER")]
    public async Task<IActionResult> MyPayslips([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        if (!TryActor(out var staffId))
            return Unauthorized(new { message = "Không xác định được người dùng từ access token." });

        var data = await payrollService.GetMyPayslipsAsync(staffId, year, month, ct);
        return Ok(new { data });
    }

    private IActionResult ToActionResult(PayrollOperationResult result) =>
        !result.Success
            ? StatusCode(result.StatusCode, new { message = result.Message })
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
