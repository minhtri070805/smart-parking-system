using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingOperations.Domain.Entities;

namespace ParkingOperations.Persistence.EntityTypeConfigurations;

public sealed class ParkingShiftConfiguration : IEntityTypeConfiguration<ParkingShift>
{
    public void Configure(EntityTypeBuilder<ParkingShift> entity)
    {
        entity.ToTable("shifts");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(x => x.LotId).HasColumnName("lot_id").IsRequired();
        entity.Property(x => x.BusinessDate).HasColumnName("business_date").IsRequired();
        entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
        entity.Property(x => x.StartsAtUtc).HasColumnName("starts_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        entity.Property(x => x.EndsAtUtc).HasColumnName("ends_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        entity.Property(x => x.CreatedByManagerId).HasColumnName("created_by_manager_id").IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        entity.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc").HasColumnType("timestamp with time zone");
        entity.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").HasColumnType("timestamp with time zone");
        entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        entity.HasIndex(x => new { x.LotId, x.BusinessDate, x.Code }).IsUnique().HasDatabaseName("ux_shifts_lot_business_date_code");
        entity.HasMany(x => x.Assignments).WithOne(x => x.Shift).HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Cascade);
    }
}
