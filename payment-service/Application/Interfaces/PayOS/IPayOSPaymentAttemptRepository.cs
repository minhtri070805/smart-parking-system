using Application.Models.PayOS;

namespace Application.Interfaces.PayOS;

public interface IPayOSPaymentAttemptRepository
{
    Task<PayOSPaymentAttempt?> FindByUserAndIdempotencyKeyAsync(long userId, string idempotencyKey, CancellationToken ct);
    Task<PayOSPaymentAttempt?> FindByOrderCodeAsync(long orderCode, CancellationToken ct);
    Task<PayOSPaymentAttempt?> FindByIdAsync(long id, CancellationToken ct);
    Task<bool> TryAddAsync(PayOSPaymentAttempt attempt, CancellationToken ct);
    Task<PayOSPaymentAttempt?> StorePaymentLinkAsync(long id, string paymentLinkId, string checkoutUrl, DateTime updatedAtUtc, CancellationToken ct);
    Task<bool> TryMarkLinkCreationFailedAsync(long id, DateTime updatedAtUtc, CancellationToken ct);
    Task<IReadOnlyList<PayOSPaymentAttempt>> FindDueCoreConfirmationsAsync(DateTime nowUtc, int limit, CancellationToken ct);
    Task SaveAsync(PayOSPaymentAttempt attempt, CancellationToken ct);
}
