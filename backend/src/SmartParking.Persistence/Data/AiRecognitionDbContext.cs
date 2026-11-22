using Microsoft.EntityFrameworkCore;
using SmartParking.Persistence.Entities;

namespace SmartParking.Persistence.Data;

public sealed class AiRecognitionDbContext : DbContext
{
    public AiRecognitionDbContext(DbContextOptions<AiRecognitionDbContext> options) : base(options) { }

    public DbSet<AiRecognitionRecord> RecognitionRecords => Set<AiRecognitionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiRecognitionRecord>(entity =>
        {
            entity.ToTable("ai_recognition_records");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PlateNumber).HasMaxLength(32);
            entity.Property(x => x.RawPlateNumber).HasMaxLength(64);
            entity.Property(x => x.FailureReason).HasMaxLength(512);
            entity.Property(x => x.ConfirmedPlateNumber).HasMaxLength(32);
            entity.Property(x => x.ConfirmationNote).HasMaxLength(256);
            entity.Property(x => x.Purpose).HasMaxLength(16).HasDefaultValue("ENTRY");
            entity.Property(x => x.CaptureId).HasMaxLength(128);
            entity.Property(x => x.ImageId).HasMaxLength(128);
            entity.Property(x => x.EventId).HasMaxLength(128);
            entity.Property(x => x.TicketId).HasMaxLength(128);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(128);
            entity.Property(x => x.ConfirmationIdempotencyKey).HasMaxLength(128);
            entity.Property(x => x.CorrelationId).HasMaxLength(128);
            entity.Property(x => x.ModelVersion).HasMaxLength(128);
            entity.HasIndex(x => x.CaptureId).IsUnique().HasFilter("\"CaptureId\" IS NOT NULL");
            entity.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
            entity.Property(x => x.ParkingLotId);
            entity.Property(x => x.MatchedParkingSessionId);
            entity.HasIndex(x => new { x.ParkingLotId, x.Purpose, x.CreatedAt });
            entity.Property(x => x.VehicleBboxJson).HasMaxLength(128);
            entity.Property(x => x.PlateBboxJson).HasMaxLength(128);
            entity.HasIndex(x => x.StaffUserId);
            entity.HasIndex(x => x.CreatedAt);
        });
    }
}
