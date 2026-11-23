using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartParking.Application.Common.Interfaces;

namespace SmartParking.Api.HostedServices;

/// <summary>
/// Marks reservations as expired after their configured late-arrival window so
/// their capacity hold and the vehicle's one-active-booking restriction are released.
/// </summary>
public sealed class BookingNoShowExpirationService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookingNoShowExpirationService> _logger;

    public BookingNoShowExpirationService(
        IServiceScopeFactory scopeFactory,
        ILogger<BookingNoShowExpirationService> logger)
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
                var bookings = scope.ServiceProvider.GetRequiredService<IBookingService>();
                var expiredCount = await bookings.ExpireNoShowBookingsAsync(ct: stoppingToken);
                if (expiredCount > 0)
                    _logger.LogInformation("Automatically expired {ExpiredCount} no-show booking(s).", expiredCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to expire no-show bookings; will retry in {PollInterval}.", PollInterval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
