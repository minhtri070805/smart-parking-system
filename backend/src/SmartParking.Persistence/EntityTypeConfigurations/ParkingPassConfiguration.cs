using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartParking.Domain.Entities;

namespace SmartParking.Persistence.EntityTypeConfigurations;

public sealed class ParkingPassConfiguration : IEntityTypeConfiguration<ParkingPass>
{
    public void Configure(EntityTypeBuilder<ParkingPass> b)
    {
        b.ToTable("parking_passes_subcription", t =>
        {
            t.HasCheckConstraint("ck_parking_passes_status", "status IN ('PENDING','SCHEDULED','ACTIVE','EXPIRED','SUSPENDED','CANCELLED')");
            t.HasCheckConstraint("ck_parking_passes_dates", "ends_at_utc > starts_at_utc");
            t.HasCheckConstraint("ck_parking_passes_price", "price_amount >= 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
        b.Property(x => x.VehicleId).HasColumnName("vehicle_id");
        b.Property(x => x.PlanId).HasColumnName("plan_id");
        b.Property(x => x.PlanVersion).HasColumnName("plan_version");
        b.Property(x => x.LotId).HasColumnName("lot_id");
        b.Property(x => x.StartsAtUtc).HasColumnName("starts_at_utc");
        b.Property(x => x.EndsAtUtc).HasColumnName("ends_at_utc");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        b.Property(x => x.PriceAmount).HasColumnName("price_amount").HasPrecision(18, 2);
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(x => x.PaymentInvoiceId).HasColumnName("payment_invoice_id");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasDefaultValueSql("now()");
        b.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken();
        b.HasIndex(x => new { x.OwnerUserId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.VehicleId, x.LotId });
        b.HasIndex(x => x.EndsAtUtc).HasFilter("status = 'ACTIVE'");
        b.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Vehicle).WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
    }
}
