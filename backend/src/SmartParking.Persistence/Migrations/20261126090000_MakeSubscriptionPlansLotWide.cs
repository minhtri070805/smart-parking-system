using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartParking.Persistence.Data;

#nullable disable

namespace SmartParking.Persistence.Migrations
{
    [DbContext(typeof(SmartParkingDbContext))]
    [Migration("20261126090000_MakeSubscriptionPlansLotWide")]
    public partial class MakeSubscriptionPlansLotWide : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "zone_id",
                table: "subscription_plans",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "zone_id",
                table: "parking_passes",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.DropIndex(
                name: "IX_subscription_plans_status_lot_id_zone_id_effective_from_utc",
                table: "subscription_plans");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_plans_status_lot_id_effective_from_utc",
                table: "subscription_plans",
                columns: new[] { "status", "lot_id", "effective_from_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_parking_passes_vehicle_id_lot_id",
                table: "parking_passes",
                columns: new[] { "vehicle_id", "lot_id" },
                unique: true,
                filter: "zone_id IS NULL AND status IN ('PENDING','SCHEDULED','ACTIVE')");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_parking_passes_vehicle_id_lot_id",
                table: "parking_passes");

            migrationBuilder.DropIndex(
                name: "IX_subscription_plans_status_lot_id_effective_from_utc",
                table: "subscription_plans");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_plans_status_lot_id_zone_id_effective_from_utc",
                table: "subscription_plans",
                columns: new[] { "status", "lot_id", "zone_id", "effective_from_utc" });

            migrationBuilder.AlterColumn<int>(
                name: "zone_id",
                table: "subscription_plans",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "zone_id",
                table: "parking_passes",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
