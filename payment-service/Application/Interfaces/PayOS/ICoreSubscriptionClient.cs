using Application.Models.PayOS;

namespace Application.Interfaces.PayOS;

public interface ICoreSubscriptionClient
{
    Task<CorePrepareResponse> PrepareAsync(long orderCode, long userId, long planId, long vehicleId, CancellationToken ct);
    Task<CoreConfirmResponse> ConfirmAsync(long orderCode, decimal amount, string currency, string providerReference, DateTime paidAtUtc, CancellationToken ct);
    Task CancelAsync(long orderCode, CancellationToken ct);
}
