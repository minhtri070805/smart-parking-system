using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartParking.Domain.Entities;

namespace SmartParking.Persistence.EntityTypeConfigurations;

public sealed class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> b)
    {
        b.ToTable("subscription_plans", t =>
        {
            t.HasCheckConstraint("ck_subscription_plans_status", "status IN ('DRAFT','APPROVED','RETIRED')");
            t.HasCheckConstraint("ck_subscription_plans_price", "price_amount >= 0");
            t.HasCheckConstraint("ck_subscription_plans_term", "term_months > 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        b.Property(x => x.Version).HasColumnName("version");
        b.Property(x => x.LotId).HasColumnName("lot_id");
        b.Property(x => x.TermMonths).HasColumnName("term_months");
        b.Property(x => x.PriceAmount).HasColumnName("price_amount").HasPrecision(18, 2);
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(x => x.EffectiveFromUtc).HasColumnName("effective_from_utc");
        b.Property(x => x.EffectiveToUtc).HasColumnName("effective_to_utc");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.ApprovedBy).HasColumnName("approved_by");
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");
        b.Property(x => x.ApprovedAtUtc).HasColumnName("approved_at_utc");
        b.HasIndex(x => new { x.Code, x.Version }).IsUnique();
        b.HasIndex(x => new { x.Status, x.LotId, x.EffectiveFromUtc });
        b.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
    }
}
