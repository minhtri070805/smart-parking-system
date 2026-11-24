using Microsoft.EntityFrameworkCore;
using recognition.Domain.Entities;

namespace recognition.Persistence.Data;

public class AiRecognitionDbContext(DbContextOptions<AiRecognitionDbContext> options) : DbContext(options)
{
    public DbSet<AiRecognitionRecord> RecognitionRecords => Set<AiRecognitionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AiRecognitionRecord>(entity =>
        {
            entity.ToTable("ai_recognition_records");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.StaffUserId).HasColumnName("staff_user_id").IsRequired();
            entity.Property(e => e.CaptureId).HasColumnName("capture_id").HasMaxLength(128);
            entity.Property(e => e.ImageId).HasColumnName("image_id").HasMaxLength(128);
            entity.Property(e => e.EventId).HasColumnName("event_id").HasMaxLength(128);
            entity.Property(e => e.TicketId).HasColumnName("ticket_id").HasMaxLength(128);
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(256);
            entity.Property(e => e.ConfirmationIdempotencyKey).HasColumnName("confirmation_idempotency_key").HasMaxLength(256);
            entity.Property(e => e.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
            entity.Property(e => e.ModelVersion).HasColumnName("model_version").HasMaxLength(64);
            entity.Property(e => e.ContentSha256).HasColumnName("content_sha256").HasMaxLength(64);
            entity.Property(e => e.PlateNumber).HasColumnName("plate_number").HasMaxLength(32);
            entity.Property(e => e.RawPlateNumber).HasColumnName("raw_plate_number").HasMaxLength(64);
            entity.Property(e => e.FailureReason).HasColumnName("failure_reason").HasMaxLength(256);
            entity.Property(e => e.ConfirmedPlateNumber).HasColumnName("confirmed_plate_number").HasMaxLength(32);
            entity.Property(e => e.ConfirmedByStaffUserId).HasColumnName("confirmed_by_staff_user_id");
            entity.Property(e => e.ConfirmedAt).HasColumnName("confirmed_at");
            entity.Property(e => e.ConfirmationNote).HasColumnName("confirmation_note").HasMaxLength(256);
            entity.Property(e => e.ParkingLotId).HasColumnName("parking_lot_id");
            entity.Property(e => e.Purpose).HasColumnName("purpose").HasMaxLength(16);
            entity.Property(e => e.MatchedParkingSessionId).HasColumnName("matched_parking_session_id");
            entity.Property(e => e.Confidence).HasColumnName("confidence");
            entity.Property(e => e.IsLegit).HasColumnName("is_legit");
            entity.Property(e => e.VehicleConfidence).HasColumnName("vehicle_confidence");
            entity.Property(e => e.VehicleBboxJson).HasColumnName("vehicle_bbox_json").HasColumnType("jsonb");
            entity.Property(e => e.PlateBboxJson).HasColumnName("plate_bbox_json").HasColumnType("jsonb");
            entity.Property(e => e.InferenceTimeMs).HasColumnName("inference_time_ms");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasIndex(e => e.CaptureId);
            entity.HasIndex(e => e.IdempotencyKey);
            entity.HasIndex(e => e.ConfirmationIdempotencyKey);
            entity.HasIndex(e => e.ParkingLotId);
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}
