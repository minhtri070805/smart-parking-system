using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartParking.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameParkingPassesSubcriptionAndDropLotZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_parking_passes_subscription_plans_plan_id",
                table: "parking_passes");

            migrationBuilder.DropForeignKey(
                name: "FK_parking_passes_users_owner_user_id",
                table: "parking_passes");

            migrationBuilder.DropForeignKey(
                name: "FK_parking_passes_vehicles_vehicle_id",
                table: "parking_passes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_parking_passes",
                table: "parking_passes");

            migrationBuilder.DropIndex(
                name: "IX_parking_passes_vehicle_id_lot_id",
                table: "parking_passes");

            migrationBuilder.DropIndex(
                name: "IX_parking_passes_vehicle_id_zone_id",
                table: "parking_passes");

            migrationBuilder.DropColumn(
                name: "lot_id",
                table: "parking_passes");

            migrationBuilder.DropColumn(
                name: "zone_id",
                table: "parking_passes");

            migrationBuilder.RenameTable(
                name: "parking_passes",
                newName: "parking_passes_subcription");

            migrationBuilder.RenameIndex(
                name: "IX_parking_passes_slot_id",
                table: "parking_passes_subcription",
                newName: "IX_parking_passes_subcription_slot_id");

            migrationBuilder.RenameIndex(
                name: "IX_parking_passes_plan_id",
                table: "parking_passes_subcription",
                newName: "IX_parking_passes_subcription_plan_id");

            migrationBuilder.RenameIndex(
                name: "IX_parking_passes_owner_user_id_idempotency_key",
                table: "parking_passes_subcription",
                newName: "IX_parking_passes_subcription_owner_user_id_idempotency_key");

            migrationBuilder.AddPrimaryKey(
                name: "PK_parking_passes_subcription",
                table: "parking_passes_subcription",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "IX_parking_passes_subcription_vehicle_id",
                table: "parking_passes_subcription",
                column: "vehicle_id");

            migrationBuilder.AddForeignKey(
                name: "FK_parking_passes_subcription_subscription_plans_plan_id",
                table: "parking_passes_subcription",
                column: "plan_id",
                principalTable: "subscription_plans",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_parking_passes_subcription_users_owner_user_id",
                table: "parking_passes_subcription",
                column: "owner_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_parking_passes_subcription_vehicles_vehicle_id",
                table: "parking_passes_subcription",
                column: "vehicle_id",
                principalTable: "vehicles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_parking_passes_subcription_subscription_plans_plan_id",
                table: "parking_passes_subcription");

            migrationBuilder.DropForeignKey(
                name: "FK_parking_passes_subcription_users_owner_user_id",
                table: "parking_passes_subcription");

            migrationBuilder.DropForeignKey(
                name: "FK_parking_passes_subcription_vehicles_vehicle_id",
                table: "parking_passes_subcription");

            migrationBuilder.DropPrimaryKey(
                name: "PK_parking_passes_subcription",
                table: "parking_passes_subcription");

            migrationBuilder.DropIndex(
                name: "IX_parking_passes_subcription_vehicle_id",
                table: "parking_passes_subcription");

            migrationBuilder.RenameTable(
                name: "parking_passes_subcription",
                newName: "parking_passes");

            migrationBuilder.RenameIndex(
                name: "IX_parking_passes_subcription_slot_id",
                table: "parking_passes",
                newName: "IX_parking_passes_slot_id");

            migrationBuilder.RenameIndex(
                name: "IX_parking_passes_subcription_plan_id",
                table: "parking_passes",
                newName: "IX_parking_passes_plan_id");

            migrationBuilder.RenameIndex(
                name: "IX_parking_passes_subcription_owner_user_id_idempotency_key",
                table: "parking_passes",
                newName: "IX_parking_passes_owner_user_id_idempotency_key");

            migrationBuilder.AddColumn<int>(
                name: "lot_id",
                table: "parking_passes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "zone_id",
                table: "parking_passes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_parking_passes",
                table: "parking_passes",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "IX_parking_passes_vehicle_id_lot_id",
                table: "parking_passes",
                columns: new[] { "vehicle_id", "lot_id" },
                unique: true,
                filter: "zone_id IS NULL AND status IN ('PENDING','SCHEDULED','ACTIVE')");

            migrationBuilder.CreateIndex(
                name: "IX_parking_passes_vehicle_id_zone_id",
                table: "parking_passes",
                columns: new[] { "vehicle_id", "zone_id" },
                unique: true,
                filter: "status IN ('PENDING','SCHEDULED','ACTIVE')");

            migrationBuilder.AddForeignKey(
                name: "FK_parking_passes_subscription_plans_plan_id",
                table: "parking_passes",
                column: "plan_id",
                principalTable: "subscription_plans",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_parking_passes_users_owner_user_id",
                table: "parking_passes",
                column: "owner_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_parking_passes_vehicles_vehicle_id",
                table: "parking_passes",
                column: "vehicle_id",
                principalTable: "vehicles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
