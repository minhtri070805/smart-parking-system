using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartParking.Persistence.Data;

#nullable disable

namespace SmartParking.Persistence.Migrations;

[DbContext(typeof(SmartParkingDbContext))]
[Migration("20261123130000_AddParkingSlotToBookings")]
public sealed class AddParkingSlotToBookings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "parking_slot_id", table: "bookings", type: "bigint", nullable: true);
        migrationBuilder.CreateIndex(name: "uq_bookings_pending_parking_slot", table: "bookings", column: "parking_slot_id", unique: true, filter: "status = 'PENDING' AND parking_slot_id IS NOT NULL");
        migrationBuilder.AddForeignKey(name: "fk_bookings_parking_slot", table: "bookings", column: "parking_slot_id", principalTable: "parking_slots", principalColumn: "id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "fk_bookings_parking_slot", table: "bookings");
        migrationBuilder.DropIndex(name: "uq_bookings_pending_parking_slot", table: "bookings");
        migrationBuilder.DropColumn(name: "parking_slot_id", table: "bookings");
    }
}
