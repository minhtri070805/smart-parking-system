namespace SmartParking.Application.DTOs.AuditLogs;

public sealed class AuditLogDto
{
    public long Id { get; init; }
    public long? ActorId { get; init; }
    public string? ActorUsername { get; init; }
    public string? ActorName { get; init; }
    public string Action { get; init; } = string.Empty;
    public string TargetEntity { get; init; } = string.Empty;
    public string? TargetId { get; init; }
    public string? OldValues { get; init; }
    public string? NewValues { get; init; }
    public string? IpAddress { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class AuditLogFilterDto
{
    public string? Search { get; set; }
    public string? Action { get; set; }
    public string? TargetEntity { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 30;
}
