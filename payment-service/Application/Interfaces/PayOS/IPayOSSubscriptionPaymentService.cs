using Application.Models.PayOS;

namespace Application.Interfaces.PayOS;

public interface IPayOSSubscriptionPaymentService
{
    Task<PayOSSubscriptionPaymentResponse> CreateAsync(long userId, long planId, long vehicleId, string? idempotencyKey, CancellationToken ct);
    Task<PayOSSubscriptionPaymentResponse?> GetAsync(long userId, long orderCode, CancellationToken ct);
    Task<bool> HandleWebhookAsync(string rawBody, CancellationToken ct);
    Task<int> RetryCoreConfirmationsAsync(CancellationToken ct);
}
