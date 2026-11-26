using System.Globalization;
using Application.Exceptions.PayOS;
using Application.Interfaces.PayOS;
using Application.Models.PayOS;
using Microsoft.Extensions.Logging;

namespace Application.Services.PayOS;

public sealed class PayOSSubscriptionPaymentService(
    IPayOSPaymentAttemptRepository attempts,
    ICoreSubscriptionClient core,
    IPayOSCheckoutProvider payOS,
    ILogger<PayOSSubscriptionPaymentService> logger) : IPayOSSubscriptionPaymentService
{
    public async Task<PayOSSubscriptionPaymentResponse> CreateAsync(long userId, long planId, long vehicleId,
        string? idempotencyKey, CancellationToken ct)
    {
        if (userId <= 0) throw new PayOSRequestException(401, "INVALID_USER_CLAIM");
        if (planId <= 0 || vehicleId <= 0) throw new PayOSRequestException(400, "INVALID_BODY");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new PayOSRequestException(400, "IDEMPOTENCY_KEY_REQUIRED");

        var key = idempotencyKey.Trim();
        var existing = await attempts.FindByUserAndIdempotencyKeyAsync(userId, key, ct);
        if (existing is not null) return ToResponse(existing, replay: true);
        EnsurePayOSConfigured();

        var orderCode = GenerateOrderCode();
        CorePrepareResponse prepared;
        try { prepared = await core.PrepareAsync(orderCode, userId, planId, vehicleId, ct); }
        catch (CoreSubscriptionException ex) { throw new PayOSRequestException(ex.Code == "CORE_CONFLICT" ? 409 : 400, ex.Code); }

        var attempt = new PayOSPaymentAttempt
        {
            OrderCode = orderCode,
            UserId = userId,
            PlanId = prepared.PlanId,
            VehicleId = prepared.VehicleId,
            CorePaymentIntentId = prepared.PaymentIntentId,
            Amount = prepared.Amount,
            Currency = prepared.Currency,
            BillingPeriodStartUtc = DateTime.SpecifyKind(prepared.BillingPeriodStartUtc, DateTimeKind.Utc),
            BillingPeriodEndUtc = DateTime.SpecifyKind(prepared.BillingPeriodEndUtc, DateTimeKind.Utc),
            Status = "CREATING",
            IdempotencyKey = key,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        if (!await attempts.TryAddAsync(attempt, ct))
        {
            var replay = await attempts.FindByUserAndIdempotencyKeyAsync(userId, key, ct);
            if (replay is not null) return ToResponse(replay, replay: true);
            try { await core.CancelAsync(orderCode, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not cancel Core intent {OrderCode} after attempt persistence conflict.", orderCode); }
            throw new PayOSRequestException(409, "PAYMENT_ATTEMPT_CONFLICT");
        }

        CreatedPayOSLink link;
        try
        {
            var expiresAt = new DateTimeOffset(DateTime.SpecifyKind(prepared.ExpiresAtUtc, DateTimeKind.Utc))
                .AddMinutes(-5).ToUnixTimeSeconds();
            link = await payOS.CreatePaymentLinkAsync(new CreatePayOSLinkRequest(
                orderCode,
                checked((int)attempt.Amount),
                $"SUB{orderCode}"[..Math.Min(25, $"SUB{orderCode}".Length)],
                checked((int)expiresAt)), ct);
        }
        catch (Exception ex)
        {
            var markedFailed = await attempts.TryMarkLinkCreationFailedAsync(attempt.Id, DateTime.UtcNow, ct);
            if (markedFailed)
            {
                try { await core.CancelAsync(orderCode, ct); }
                catch (Exception cancelError) { logger.LogWarning(cancelError, "Could not cancel Core intent {OrderCode} after PayOS link creation failed.", orderCode); }
            }
            logger.LogError(ex, "PayOS link creation failed for order {OrderCode}.", orderCode);
            throw new PayOSRequestException(502, "PAYMENT_LINK_CREATION_FAILED");
        }

        var saved = await attempts.StorePaymentLinkAsync(attempt.Id, link.PaymentLinkId, link.CheckoutUrl, DateTime.UtcNow, ct)
            ?? throw new InvalidOperationException($"Payment attempt {attempt.Id} disappeared while storing its PayOS link.");
        return ToResponse(saved, replay: false);
    }

    public async Task<PayOSSubscriptionPaymentResponse?> GetAsync(long userId, long orderCode, CancellationToken ct)
    {
        var attempt = await attempts.FindByOrderCodeAsync(orderCode, ct);
        return attempt is null || attempt.UserId != userId ? null : ToResponse(attempt, replay: false);
    }

    public async Task<bool> HandleWebhookAsync(string rawBody, CancellationToken ct)
    {
        var data = await payOS.VerifyWebhookAsync(rawBody, ct);
        // PayOS sends this signed sample event when confirming a webhook URL.
        // It intentionally has no local payment attempt and must not change payment state.
        if (IsPayOSWebhookUrlVerification(data))
        {
            logger.LogInformation("Accepted PayOS webhook URL verification event.");
            return true;
        }

        var attempt = await attempts.FindByOrderCodeAsync(data.OrderCode, ct);
        if (attempt is null) return false;
        if (attempt.Amount != data.Amount || attempt.Currency != "VND")
        {
            logger.LogWarning("PayOS webhook amount mismatch for order {OrderCode}.", data.OrderCode);
            return false;
        }
        if (!string.IsNullOrWhiteSpace(attempt.PaymentLinkId) &&
            !string.IsNullOrWhiteSpace(data.PaymentLinkId) && attempt.PaymentLinkId != data.PaymentLinkId)
            return false;

        if (!string.Equals(data.Code, "00", StringComparison.Ordinal))
        {
            if (attempt.Status == "PENDING")
            {
                attempt.Status = "FAILED";
                attempt.UpdatedAtUtc = DateTime.UtcNow;
                await attempts.SaveAsync(attempt, ct);
                try { await core.CancelAsync(attempt.OrderCode, ct); }
                catch (Exception ex) { logger.LogWarning(ex, "Core cancellation failed for order {OrderCode}.", data.OrderCode); }
            }
            return true;
        }

        var reference = string.IsNullOrWhiteSpace(data.Reference) ? data.PaymentLinkId : data.Reference;
        if (string.IsNullOrWhiteSpace(reference)) return false;
        if (attempt.Status == "CONFIRMED") return attempt.ProviderReference == reference;
        if (attempt.ProviderReference is not null && attempt.ProviderReference != reference) return false;
        if (attempt.Status is "FAILED" or "CANCELLED" or "LINK_CREATION_FAILED") return false;

        attempt.ProviderReference = reference;
        attempt.PaidAtUtc = DateTimeOffset.TryParse(data.TransactionDateTime, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedPaidAt)
            ? parsedPaidAt.UtcDateTime
            : DateTime.UtcNow;
        attempt.Status = "PAID_PENDING_CORE_CONFIRMATION";
        attempt.NextCoreRetryAtUtc ??= DateTime.UtcNow;
        attempt.UpdatedAtUtc = DateTime.UtcNow;
        await attempts.SaveAsync(attempt, ct);
        await TryConfirmCoreAsync(attempt, ct);
        return true;
    }

    public async Task<int> RetryCoreConfirmationsAsync(CancellationToken ct)
    {
        var due = await attempts.FindDueCoreConfirmationsAsync(DateTime.UtcNow, 25, ct);
        foreach (var attempt in due) await TryConfirmCoreAsync(attempt, ct);
        return due.Count;
    }

    private static bool IsPayOSWebhookUrlVerification(VerifiedPayOSWebhook data) =>
        data.OrderCode == 123 && data.Amount == 3000m &&
        string.Equals(data.Code, "00", StringComparison.Ordinal) &&
        string.Equals(data.Reference, "TF230204212323", StringComparison.Ordinal) &&
        string.Equals(data.PaymentLinkId, "124c33293c43417ab7879e14c8d9eb18", StringComparison.Ordinal);

    private async Task TryConfirmCoreAsync(PayOSPaymentAttempt attempt, CancellationToken ct)
    {
        try
        {
            await core.ConfirmAsync(attempt.OrderCode, attempt.Amount, attempt.Currency, attempt.ProviderReference!,
                attempt.PaidAtUtc ?? DateTime.UtcNow, ct);
            attempt.Status = "CONFIRMED";
            attempt.CoreConfirmedAtUtc = DateTime.UtcNow;
            attempt.NextCoreRetryAtUtc = null;
            attempt.LastCoreErrorCode = null;
        }
        catch (Exception ex)
        {
            attempt.CoreConfirmationAttempts++;
            attempt.LastCoreErrorCode = ex is CoreSubscriptionException coreError ? coreError.Code : "CORE_UNAVAILABLE";
            attempt.NextCoreRetryAtUtc = DateTime.UtcNow.AddSeconds(Math.Min(3600, Math.Pow(2, Math.Min(attempt.CoreConfirmationAttempts, 11))));
            logger.LogWarning(ex, "Core confirmation attempt {Attempt} failed for PayOS order {OrderCode}.",
                attempt.CoreConfirmationAttempts, attempt.OrderCode);
        }
        attempt.UpdatedAtUtc = DateTime.UtcNow;
        await attempts.SaveAsync(attempt, ct);
    }

    private void EnsurePayOSConfigured()
    {
        if (!payOS.IsConfigured)
            throw new PayOSRequestException(503, "PAYOS_NOT_CONFIGURED");
    }

    private static long GenerateOrderCode() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 100 + Random.Shared.Next(0, 100);

    private static PayOSSubscriptionPaymentResponse ToResponse(PayOSPaymentAttempt attempt, bool replay) => new(
        attempt.OrderCode, attempt.PlanId, attempt.VehicleId, attempt.Amount, attempt.Currency,
        attempt.BillingPeriodStartUtc, attempt.BillingPeriodEndUtc, attempt.Status, attempt.CheckoutUrl, replay);
}
