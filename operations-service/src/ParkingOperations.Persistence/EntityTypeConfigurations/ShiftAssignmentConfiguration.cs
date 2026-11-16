using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingOperations.Domain.Entities;

namespace ParkingOperations.Persistence.EntityTypeConfigurations;

public sealed class ShiftAssignmentConfiguration : IEntityTypeConfiguration<ShiftAssignment>
{
    public void Configure(EntityTypeBuilder<ShiftAssignment> entity)
    {
        entity.ToTable("shift_assignments");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(x => x.ShiftId).HasColumnName("shift_id").IsRequired();
        entity.Property(x => x.StaffId).HasColumnName("staff_id").IsRequired();
        entity.Property(x => x.DutyCode).HasColumnName("duty_code").HasMaxLength(32).IsRequired();
        entity.Property(x => x.AssignedByManagerId).HasColumnName("assigned_by_manager_id").IsRequired();
        entity.Property(x => x.AssignedAtUtc).HasColumnName("assigned_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        entity.HasIndex(x => new { x.ShiftId, x.StaffId }).IsUnique().HasDatabaseName("ux_shift_assignments_shift_staff");
    }
}
