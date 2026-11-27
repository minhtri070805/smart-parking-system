using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartParking.Application.Common.Interfaces;

namespace SmartParking.Api.HostedServices;

public sealed class ParkingPassExpirationWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromDays(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ParkingPassExpirationWorker> _logger;

    public ParkingPassExpirationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ParkingPassExpirationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        do
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var expiration = scope.ServiceProvider.GetRequiredService<IParkingPassExpirationService>();
                var totalExpired = 0;
                int expiredCount;
                do
                {
                    expiredCount = await expiration.ExpireDuePassesAsync(stoppingToken);
                    totalExpired += expiredCount;
                }
                while (expiredCount > 0 && !stoppingToken.IsCancellationRequested);

                if (totalExpired > 0)
                    _logger.LogInformation("Automatically expired {ExpiredCount} parking pass(es).", totalExpired);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to expire parking passes; will retry in {PollInterval}.", PollInterval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
