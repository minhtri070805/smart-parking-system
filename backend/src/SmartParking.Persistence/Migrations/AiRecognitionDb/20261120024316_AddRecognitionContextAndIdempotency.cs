using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartParking.Persistence.Migrations.AiRecognitionDb
{
    /// <inheritdoc />
    public partial class AddRecognitionContextAndIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CaptureId",
                table: "ai_recognition_records",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmationIdempotencyKey",
                table: "ai_recognition_records",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "ai_recognition_records",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventId",
                table: "ai_recognition_records",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "ai_recognition_records",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageId",
                table: "ai_recognition_records",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelVersion",
                table: "ai_recognition_records",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TicketId",
                table: "ai_recognition_records",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ai_recognition_records_CaptureId",
                table: "ai_recognition_records",
                column: "CaptureId",
                unique: true,
                filter: "\"CaptureId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ai_recognition_records_IdempotencyKey",
                table: "ai_recognition_records",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ai_recognition_records_CaptureId",
                table: "ai_recognition_records");

            migrationBuilder.DropIndex(
                name: "IX_ai_recognition_records_IdempotencyKey",
                table: "ai_recognition_records");

            migrationBuilder.DropColumn(
                name: "CaptureId",
                table: "ai_recognition_records");

            migrationBuilder.DropColumn(
                name: "ConfirmationIdempotencyKey",
                table: "ai_recognition_records");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "ai_recognition_records");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "ai_recognition_records");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "ai_recognition_records");

            migrationBuilder.DropColumn(
                name: "ImageId",
                table: "ai_recognition_records");

            migrationBuilder.DropColumn(
                name: "ModelVersion",
                table: "ai_recognition_records");

            migrationBuilder.DropColumn(
                name: "TicketId",
                table: "ai_recognition_records");
        }
    }
}
