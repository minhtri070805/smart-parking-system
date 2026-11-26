using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartParking.Application.Common.Interfaces;
using SmartParking.Application.DTOs.AuditLogs;
using SmartParking.Domain.Constants;

namespace SmartParking.Api.Controllers;

[ApiController]
[Route("api/v1/admin/audit-logs")]
[Produces("application/json")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminAuditLogsController : ControllerBase
{
    private readonly IAuditLogService _auditLogService;

    public AdminAuditLogsController(IAuditLogService auditLogService) => _auditLogService = auditLogService;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] AuditLogFilterDto filter, CancellationToken ct)
    {
        var result = await _auditLogService.GetAsync(filter, ct);
        return Ok(new { message = "Lấy nhật ký quản trị thành công.", data = result });
    }
}
