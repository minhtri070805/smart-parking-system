using SmartParking.Application.Common.Models;
using SmartParking.Application.DTOs.AuditLogs;

namespace SmartParking.Application.Common.Interfaces;

public interface IAuditLogService
{
    Task<PagedResult<AuditLogDto>> GetAsync(AuditLogFilterDto filter, CancellationToken ct = default);
}
