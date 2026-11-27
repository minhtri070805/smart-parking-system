using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using recognition.Persistence.Data;

#nullable disable

namespace recognition.Persistence.Migrations;

[DbContext(typeof(AiRecognitionDbContext))]
[Migration("20261126160000_AlignRecognitionRecordSchema")]
public sealed class AlignRecognitionRecordSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                pair text[];
                old_name text;
                new_name text;
            BEGIN
                FOREACH pair SLICE 1 IN ARRAY ARRAY[
                    ARRAY['Id', 'id'],
                    ARRAY['StaffUserId', 'staff_user_id'],
                    ARRAY['PlateNumber', 'plate_number'],
                    ARRAY['RawPlateNumber', 'raw_plate_number'],
                    ARRAY['Confidence', 'confidence'],
                    ARRAY['IsLegit', 'is_legit'],
                    ARRAY['VehicleConfidence', 'vehicle_confidence'],
                    ARRAY['VehicleBboxJson', 'vehicle_bbox_json'],
                    ARRAY['PlateBboxJson', 'plate_bbox_json'],
                    ARRAY['InferenceTimeMs', 'inference_time_ms'],
                    ARRAY['CreatedAt', 'created_at'],
                    ARRAY['ConfirmationNote', 'confirmation_note'],
                    ARRAY['ConfirmedAt', 'confirmed_at'],
                    ARRAY['ConfirmedByStaffUserId', 'confirmed_by_staff_user_id'],
                    ARRAY['ConfirmedPlateNumber', 'confirmed_plate_number'],
                    ARRAY['FailureReason', 'failure_reason'],
                    ARRAY['ContentSha256', 'content_sha256'],
                    ARRAY['ParkingLotId', 'parking_lot_id'],
                    ARRAY['Purpose', 'purpose'],
                    ARRAY['MatchedParkingSessionId', 'matched_parking_session_id'],
                    ARRAY['CaptureId', 'capture_id'],
                    ARRAY['ImageId', 'image_id'],
                    ARRAY['EventId', 'event_id'],
                    ARRAY['TicketId', 'ticket_id'],
                    ARRAY['IdempotencyKey', 'idempotency_key'],
                    ARRAY['ConfirmationIdempotencyKey', 'confirmation_idempotency_key'],
                    ARRAY['CorrelationId', 'correlation_id'],
                    ARRAY['ModelVersion', 'model_version'],
                    ARRAY['vehicle_bbox_json', 'vehicle_bbox_json'],
                    ARRAY['plate_bbox_json', 'plate_bbox_json']
                ] LOOP
                    old_name := pair[1];
                    new_name := pair[2];
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public' AND table_name = 'ai_recognition_records'
                          AND column_name = old_name
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public' AND table_name = 'ai_recognition_records'
                          AND column_name = new_name
                    ) THEN
                        EXECUTE format('ALTER TABLE public.ai_recognition_records RENAME COLUMN %I TO %I', old_name, new_name);
                    END IF;
                END LOOP;

                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='ai_recognition_records' AND column_name='ContentSha256')
                   AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='ai_recognition_records' AND column_name='content_sha256') THEN
                    EXECUTE 'ALTER TABLE public.ai_recognition_records RENAME COLUMN "ContentSha256" TO content_sha256';
                END IF;

                IF NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_name='ai_recognition_records') THEN
                    RAISE EXCEPTION 'Expected table public.ai_recognition_records was not found';
                END IF;
            END $$;
            """);

        migrationBuilder.Sql("""
            ALTER TABLE public.ai_recognition_records
                ADD COLUMN IF NOT EXISTS capture_id varchar(128),
                ADD COLUMN IF NOT EXISTS image_id varchar(128),
                ADD COLUMN IF NOT EXISTS event_id varchar(128),
                ADD COLUMN IF NOT EXISTS ticket_id varchar(128),
                ADD COLUMN IF NOT EXISTS idempotency_key varchar(256),
                ADD COLUMN IF NOT EXISTS confirmation_idempotency_key varchar(256),
                ADD COLUMN IF NOT EXISTS correlation_id varchar(128),
                ADD COLUMN IF NOT EXISTS model_version varchar(64),
                ADD COLUMN IF NOT EXISTS content_sha256 text,
                ADD COLUMN IF NOT EXISTS confirmed_by_staff_user_id bigint,
                ADD COLUMN IF NOT EXISTS confirmed_at timestamptz,
                ADD COLUMN IF NOT EXISTS confirmation_note varchar(256),
                ADD COLUMN IF NOT EXISTS parking_lot_id integer,
                ADD COLUMN IF NOT EXISTS purpose varchar(16) DEFAULT 'ENTRY',
                ADD COLUMN IF NOT EXISTS matched_parking_session_id bigint;

            ALTER TABLE public.ai_recognition_records
                ALTER COLUMN idempotency_key TYPE varchar(256),
                ALTER COLUMN confirmation_idempotency_key TYPE varchar(256),
                ALTER COLUMN model_version TYPE varchar(64),
                ALTER COLUMN failure_reason TYPE varchar(256),
                ALTER COLUMN vehicle_bbox_json TYPE jsonb USING
                    CASE WHEN vehicle_bbox_json IS NULL THEN NULL WHEN jsonb_typeof(vehicle_bbox_json::jsonb) IS NOT NULL THEN vehicle_bbox_json::jsonb END,
                ALTER COLUMN plate_bbox_json TYPE jsonb USING
                    CASE WHEN plate_bbox_json IS NULL THEN NULL WHEN jsonb_typeof(plate_bbox_json::jsonb) IS NOT NULL THEN plate_bbox_json::jsonb END;

            CREATE INDEX IF NOT EXISTS ix_ai_recognition_records_capture_id
                ON public.ai_recognition_records (capture_id);
            CREATE INDEX IF NOT EXISTS ix_ai_recognition_records_idempotency_key
                ON public.ai_recognition_records (idempotency_key);
            CREATE INDEX IF NOT EXISTS ix_ai_recognition_records_confirmation_idempotency_key
                ON public.ai_recognition_records (confirmation_idempotency_key);
            CREATE INDEX IF NOT EXISTS ix_ai_recognition_records_parking_lot_id
                ON public.ai_recognition_records (parking_lot_id);
            CREATE INDEX IF NOT EXISTS ix_ai_recognition_records_created_at
                ON public.ai_recognition_records (created_at);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("This schema alignment migration is intentionally irreversible to preserve recognition data.");
    }
}
