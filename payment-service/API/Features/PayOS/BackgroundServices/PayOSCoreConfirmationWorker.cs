using Application.Interfaces.PayOS;
using API.Configurations;
using Microsoft.Extensions.Options;

namespace API.Features.PayOS.BackgroundServices;

public sealed class PayOSCoreConfirmationWorker(
    IServiceScopeFactory scopes,
    IOptions<PayOSConfirmationWorkerOptions> configured,
    ILogger<PayOSCoreConfirmationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(configured.Value.ConfirmationPollIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IPayOSSubscriptionPaymentService>();
                await service.RetryCoreConfirmationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "PayOS Core confirmation retry cycle failed."); }
        }
    }
}
