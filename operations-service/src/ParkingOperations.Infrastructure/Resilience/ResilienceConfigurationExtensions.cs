using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace ParkingOperations.Infrastructure.Resilience;

public static class ResilienceConfigurationExtensions
{
    public static IHttpClientBuilder AddDownstreamResilience(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler("DownstreamPipeline", pipeline =>
        {
            // 1. Total request timeout (bao gồm cả các lần retry)
            pipeline.AddTimeout(TimeSpan.FromSeconds(30));

            // 2. Retry Policy: chỉ retry với Transient error (502, 503, 504, HttpRequestException), 
            // KHÔNG retry với 400, 403, 404, 409
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = static args =>
                {
                    // Lỗi exception mạng hoặc timeout
                    if (args.Outcome.Exception is HttpRequestException or TimeoutException)
                    {
                        return ValueTask.FromResult(true);
                    }

                    // Lỗi HTTP status code từ downstream
                    var response = args.Outcome.Result;
                    if (response != null)
                    {
                        var status = response.StatusCode;
                        if (status is HttpStatusCode.BadGateway 
                                  or HttpStatusCode.ServiceUnavailable 
                                  or HttpStatusCode.GatewayTimeout 
                                  or HttpStatusCode.RequestTimeout)
                        {
                            return ValueTask.FromResult(true);
                        }
                    }

                    return ValueTask.FromResult(false);
                }
            });

            // 3. Circuit Breaker: Ngắt mạch nếu tỷ lệ lỗi quá cao để bảo vệ downstream & tránh nghẽn thread
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(15),
                ShouldHandle = static args =>
                {
                    if (args.Outcome.Exception is HttpRequestException or TimeoutException)
                    {
                        return ValueTask.FromResult(true);
                    }

                    var response = args.Outcome.Result;
                    if (response != null)
                    {
                        var status = response.StatusCode;
                        if (status is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
                        {
                            return ValueTask.FromResult(true);
                        }
                    }

                    return ValueTask.FromResult(false);
                }
            });

            // 4. Timeout cho từng lần gửi riêng biệt (Attempt timeout)
            pipeline.AddTimeout(TimeSpan.FromSeconds(15));
        });

        return builder;
    }
}
