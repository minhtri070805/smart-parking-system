using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Features.PayOS.Persistence;

public sealed class PayOSPaymentAttemptConfiguration : IEntityTypeConfiguration<PayOSPaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PayOSPaymentAttempt> b)
    {
        b.ToTable("payos_subscription_payment_attempts", t => t.HasCheckConstraint(
            "ck_payos_subscription_payment_attempts_status",
            "status IN ('CREATING','PENDING','PAID_PENDING_CORE_CONFIRMATION','CONFIRMED','FAILED','CANCELLED','LINK_CREATION_FAILED')"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.OrderCode).HasColumnName("order_code");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.PlanId).HasColumnName("plan_id");
        b.Property(x => x.VehicleId).HasColumnName("vehicle_id");
        b.Property(x => x.CorePaymentIntentId).HasColumnName("core_payment_intent_id");
        b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 2);
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(x => x.BillingPeriodStartUtc).HasColumnName("billing_period_start_utc");
        b.Property(x => x.BillingPeriodEndUtc).HasColumnName("billing_period_end_utc");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(40).IsRequired();
        b.Property(x => x.PaymentLinkId).HasColumnName("payment_link_id").HasMaxLength(100);
        b.Property(x => x.CheckoutUrl).HasColumnName("checkout_url");
        b.Property(x => x.ProviderReference).HasColumnName("provider_reference").HasMaxLength(100);
        b.Property(x => x.PaidAtUtc).HasColumnName("paid_at_utc");
        b.Property(x => x.CoreConfirmedAtUtc).HasColumnName("core_confirmed_at_utc");
        b.Property(x => x.CoreConfirmationAttempts).HasColumnName("core_confirmation_attempts");
        b.Property(x => x.NextCoreRetryAtUtc).HasColumnName("next_core_retry_at_utc");
        b.Property(x => x.LastCoreErrorCode).HasColumnName("last_core_error_code").HasMaxLength(100);
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasDefaultValueSql("now()");
        b.HasIndex(x => x.OrderCode).IsUnique();
        b.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => x.ProviderReference).IsUnique().HasFilter("provider_reference IS NOT NULL");
        b.HasIndex(x => new { x.Status, x.NextCoreRetryAtUtc });
    }
}
