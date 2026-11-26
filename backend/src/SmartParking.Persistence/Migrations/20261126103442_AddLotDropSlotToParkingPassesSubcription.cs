using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartParking.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLotDropSlotToParkingPassesSubcription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_parking_passes_subcription_slot_id",
                table: "parking_passes_subcription");

            migrationBuilder.DropIndex(
                name: "IX_parking_passes_subcription_vehicle_id",
                table: "parking_passes_subcription");

            migrationBuilder.DropColumn(
                name: "slot_id",
                table: "parking_passes_subcription");

            migrationBuilder.AddColumn<int>(
                name: "lot_id",
                table: "parking_passes_subcription",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE parking_passes_subcription AS pass
                SET lot_id = plan.lot_id
                FROM subscription_plans AS plan
                WHERE plan.id = pass.plan_id;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "lot_id",
                table: "parking_passes_subcription",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_parking_passes_subcription_vehicle_id_lot_id",
                table: "parking_passes_subcription",
                columns: new[] { "vehicle_id", "lot_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_parking_passes_subcription_vehicle_id_lot_id",
                table: "parking_passes_subcription");

            migrationBuilder.DropColumn(
                name: "lot_id",
                table: "parking_passes_subcription");

            migrationBuilder.AddColumn<long>(
                name: "slot_id",
                table: "parking_passes_subcription",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_parking_passes_subcription_slot_id",
                table: "parking_passes_subcription",
                column: "slot_id",
                unique: true,
                filter: "slot_id IS NOT NULL AND status IN ('PENDING','SCHEDULED','ACTIVE')");

            migrationBuilder.CreateIndex(
                name: "IX_parking_passes_subcription_vehicle_id",
                table: "parking_passes_subcription",
                column: "vehicle_id");
        }
    }
}
