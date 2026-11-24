using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartParking.Domain.Entities;

namespace SmartParking.Persistence.EntityTypeConfigurations;

public class VehicleRegistrationBlockConfiguration : IEntityTypeConfiguration<VehicleRegistrationBlock>
{
    public void Configure(EntityTypeBuilder<VehicleRegistrationBlock> builder)
    {
        builder.ToTable("vehicle_registration_blocks", table =>
            table.HasCheckConstraint("chk_vehicle_registration_blocks_plate_normalized", "plate_number = UPPER(TRIM(plate_number))"));
        builder.HasKey(block => block.Id).HasName("pk_vehicle_registration_blocks");
        builder.Property(block => block.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(block => block.PlateNumber).HasColumnName("plate_number").HasMaxLength(20).IsRequired();
        builder.HasIndex(block => block.PlateNumber).IsUnique().HasDatabaseName("uq_vehicle_registration_blocks_plate_number");
        builder.Property(block => block.BlockedUntilUtc).HasColumnName("blocked_until_utc").IsRequired();
        builder.Property(block => block.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()").IsRequired();
    }
}
