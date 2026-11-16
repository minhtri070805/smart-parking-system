using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingOperations.Domain.Entities;

namespace ParkingOperations.Persistence.EntityTypeConfigurations;

public sealed class CashHandoverConfiguration : IEntityTypeConfiguration<CashHandover>
{
    public void Configure(EntityTypeBuilder<CashHandover> entity)
    {
        entity.ToTable("cash_handovers");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(x => x.LotId).HasColumnName("lot_id").IsRequired();
        entity.Property(x => x.BusinessDate).HasColumnName("business_date").IsRequired();
        entity.Property(x => x.FromShiftCode).HasColumnName("from_shift_code").HasMaxLength(20).IsRequired();
        entity.Property(x => x.ToShiftCode).HasColumnName("to_shift_code").HasMaxLength(20).IsRequired();
        entity.Property(x => x.OutgoingShiftId).HasColumnName("outgoing_shift_id");
        entity.Property(x => x.IncomingShiftId).HasColumnName("incoming_shift_id");
        entity.Property(x => x.OutgoingStaffId).HasColumnName("outgoing_staff_id").IsRequired();
        entity.Property(x => x.IncomingStaffId).HasColumnName("incoming_staff_id").IsRequired();
        entity.Property(x => x.CreatedByManagerId).HasColumnName("created_by_manager_id").IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        entity.Property(x => x.OutgoingCountedAmount).HasColumnName("outgoing_counted_amount").HasPrecision(14, 2);
        entity.Property(x => x.OutgoingConfirmedBy).HasColumnName("outgoing_confirmed_by");
        entity.Property(x => x.OutgoingConfirmedAtUtc).HasColumnName("outgoing_confirmed_at_utc").HasColumnType("timestamp with time zone");
        entity.Property(x => x.IncomingCountedAmount).HasColumnName("incoming_counted_amount").HasPrecision(14, 2);
        entity.Property(x => x.IncomingConfirmedBy).HasColumnName("incoming_confirmed_by");
        entity.Property(x => x.IncomingConfirmedAtUtc).HasColumnName("incoming_confirmed_at_utc").HasColumnType("timestamp with time zone");
        entity.Property(x => x.DisputeReason).HasColumnName("dispute_reason").HasMaxLength(500);
        entity.Property(x => x.DisputedBy).HasColumnName("disputed_by");
        entity.Property(x => x.DisputedAtUtc).HasColumnName("disputed_at_utc").HasColumnType("timestamp with time zone");
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        entity.HasOne<ParkingShift>().WithMany().HasForeignKey(x => x.OutgoingShiftId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ParkingShift>().WithMany().HasForeignKey(x => x.IncomingShiftId).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(x => new { x.LotId, x.BusinessDate, x.OutgoingShiftId }).IsUnique().HasDatabaseName("ux_cash_handovers_lot_date_outgoing_shift");
        entity.HasIndex(x => new { x.LotId, x.BusinessDate, x.Status }).HasDatabaseName("ix_cash_handovers_lot_date_status");
    }
}
