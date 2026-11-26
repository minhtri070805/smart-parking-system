using Microsoft.EntityFrameworkCore;
using SmartParking.Application.Common.Interfaces;
using SmartParking.Application.Common.Models;
using SmartParking.Application.DTOs.AuditLogs;

namespace SmartParking.Infrastructure.Services;

public sealed class AuditLogService : IAuditLogService
{
    private readonly IApplicationDbContext _context;

    public AuditLogService(IApplicationDbContext context) => _context = context;

    public async Task<PagedResult<AuditLogDto>> GetAsync(AuditLogFilterDto filter, CancellationToken ct = default)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var query = _context.AuditLogs.AsNoTracking().Include(x => x.Actor).AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Action))
            query = query.Where(x => x.Action == filter.Action.Trim());
        if (!string.IsNullOrWhiteSpace(filter.TargetEntity))
            query = query.Where(x => x.TargetEntity == filter.TargetEntity.Trim());
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(x => x.Action.Contains(search)
                || x.TargetEntity.Contains(search)
                || (x.TargetId != null && x.TargetId.Contains(search))
                || (x.Actor != null && (x.Actor.Username.Contains(search) || (x.Actor.FullName != null && x.Actor.FullName.Contains(search)))));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AuditLogDto
            {
                Id = x.Id, ActorId = x.ActorId,
                ActorUsername = x.Actor == null ? null : x.Actor.Username,
                ActorName = x.Actor == null ? null : x.Actor.FullName,
                Action = x.Action, TargetEntity = x.TargetEntity, TargetId = x.TargetId,
                OldValues = x.OldValues, NewValues = x.NewValues, IpAddress = x.IpAddress,
                CreatedAt = x.CreatedAt
            }).ToListAsync(ct);

        return PagedResult<AuditLogDto>.Create(items, total, page, pageSize);
    }
}
