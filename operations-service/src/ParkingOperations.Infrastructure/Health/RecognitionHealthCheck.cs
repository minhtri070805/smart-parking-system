using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ParkingOperations.Infrastructure.Health;

public sealed class RecognitionHealthCheck(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = configuration["Recognition:BaseUrl"] 
                      ?? configuration["RecognitionService:BaseUrl"] 
                      ?? "http://localhost:5002/";

        var internalKey = configuration["Recognition:InternalKey"] 
                          ?? configuration["Recognition__InternalKey"] 
                          ?? configuration["RecognitionService:InternalKey"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return HealthCheckResult.Unhealthy("Recognition service BaseUrl is missing from configuration.");
        }

        if (string.IsNullOrWhiteSpace(internalKey) || internalKey.Length < 32)
        {
            return HealthCheckResult.Unhealthy("Recognition internal key is missing or invalid (< 32 chars).");
        }

        try
        {
            var client = httpClientFactory.CreateClient("RecognitionHealthProbe");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3)); // Timeout ngắn 3s cho health check probe

            // Probe cả readiness (đảm bảo downstream AI model đã sẵn sàng) hoặc fallback về health
            var response = await client.GetAsync(new Uri(new Uri(baseUrl), "readiness"), cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                response = await client.GetAsync(new Uri(new Uri(baseUrl), "health"), cts.Token);
            }

            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy("Recognition service and models are reachable and healthy.");
            }

            return HealthCheckResult.Unhealthy($"Recognition service responded with status {response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Recognition service is unreachable.", ex);
        }
    }
}
