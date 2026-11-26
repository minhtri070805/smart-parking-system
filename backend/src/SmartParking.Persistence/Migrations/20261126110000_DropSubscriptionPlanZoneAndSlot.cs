using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartParking.Persistence.Data;

#nullable disable

namespace SmartParking.Persistence.Migrations
{
    [DbContext(typeof(SmartParkingDbContext))]
    [Migration("20261126110000_DropSubscriptionPlanZoneAndSlot")]
    public partial class DropSubscriptionPlanZoneAndSlot : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_subscription_plans_parking_slots_slot_id",
                table: "subscription_plans");

            migrationBuilder.DropForeignKey(
                name: "FK_subscription_plans_parking_zones_zone_id",
                table: "subscription_plans");

            migrationBuilder.DropIndex(
                name: "IX_subscription_plans_slot_id",
                table: "subscription_plans");

            migrationBuilder.DropIndex(
                name: "IX_subscription_plans_zone_id",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "slot_id",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "zone_id",
                table: "subscription_plans");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "slot_id",
                table: "subscription_plans",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "zone_id",
                table: "subscription_plans",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscription_plans_slot_id",
                table: "subscription_plans",
                column: "slot_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_plans_zone_id",
                table: "subscription_plans",
                column: "zone_id");

            migrationBuilder.AddForeignKey(
                name: "FK_subscription_plans_parking_slots_slot_id",
                table: "subscription_plans",
                column: "slot_id",
                principalTable: "parking_slots",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_subscription_plans_parking_zones_zone_id",
                table: "subscription_plans",
                column: "zone_id",
                principalTable: "parking_zones",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
