using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartParking.Domain.Entities;

namespace SmartParking.Persistence.EntityTypeConfigurations;

public sealed class SubscriptionPaymentIntentConfiguration : IEntityTypeConfiguration<SubscriptionPaymentIntent>
{
    public void Configure(EntityTypeBuilder<SubscriptionPaymentIntent> b)
    {
        b.ToTable("subscription_payment_intents", t =>
            t.HasCheckConstraint("ck_subscription_payment_intents_status", "status IN ('PENDING_PAYMENT','CONFIRMED','CANCELLED')"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.OrderCode).HasColumnName("order_code");
        b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
        b.Property(x => x.VehicleId).HasColumnName("vehicle_id");
        b.Property(x => x.PlanId).HasColumnName("plan_id");
        b.Property(x => x.LotId).HasColumnName("lot_id");
        b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 2);
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(x => x.TermMonths).HasColumnName("term_months");
        b.Property(x => x.BillingPeriodStartUtc).HasColumnName("billing_period_start_utc");
        b.Property(x => x.BillingPeriodEndUtc).HasColumnName("billing_period_end_utc");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(24).IsRequired();
        b.Property(x => x.ProviderReference).HasColumnName("provider_reference").HasMaxLength(100);
        b.Property(x => x.ParkingPassId).HasColumnName("parking_pass_id");
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");
        b.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
        b.Property(x => x.ConfirmedAtUtc).HasColumnName("confirmed_at_utc");
        b.HasIndex(x => x.OrderCode).IsUnique();
        b.HasIndex(x => x.ProviderReference).IsUnique().HasFilter("provider_reference IS NOT NULL");
        b.HasIndex(x => new { x.VehicleId, x.LotId }).IsUnique().HasFilter("status = 'PENDING_PAYMENT'");
    }
}
