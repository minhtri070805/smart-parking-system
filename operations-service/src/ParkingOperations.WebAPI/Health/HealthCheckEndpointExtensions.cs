using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ParkingOperations.WebAPI.Health;

public static class HealthCheckEndpointExtensions
{
    public static void MapOperationsHealthChecks(this WebApplication app)
    {
        // 1. /health: Process liveness probe (chỉ kiểm tra service còn chạy)
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "healthy",
            service = "parking-operations-service",
            timestamp = DateTime.UtcNow
        })).AllowAnonymous();

        // 2. /readiness: Readiness probe (kiểm tra cấu hình bắt buộc & downstream Recognition service)
        app.MapHealthChecks("/readiness", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var response = new
                {
                    status = report.Status.ToString().ToLowerInvariant(),
                    totalDurationMs = report.TotalDuration.TotalMilliseconds,
                    checks = report.Entries.Select(e => new
                    {
                        name = e.Key,
                        status = e.Value.Status.ToString().ToLowerInvariant(),
                        description = e.Value.Description,
                        durationMs = e.Value.Duration.TotalMilliseconds,
                        error = e.Value.Exception?.Message
                    })
                };

                await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            }
        }).AllowAnonymous();
    }
}
