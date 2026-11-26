namespace Infrastructure.Features.PayOS.Persistence;

public sealed class PayOSPaymentAttempt
{
    public long Id { get; set; }
    public long OrderCode { get; set; }
    public long UserId { get; set; }
    public long PlanId { get; set; }
    public long VehicleId { get; set; }
    public long CorePaymentIntentId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public DateTime BillingPeriodStartUtc { get; set; }
    public DateTime BillingPeriodEndUtc { get; set; }
    public string Status { get; set; } = "PENDING";
    public string? PaymentLinkId { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? ProviderReference { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? CoreConfirmedAtUtc { get; set; }
    public int CoreConfirmationAttempts { get; set; }
    public DateTime? NextCoreRetryAtUtc { get; set; }
    public string? LastCoreErrorCode { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
