using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartParking.Persistence.Data;

#nullable disable

namespace SmartParking.Persistence.Migrations;

[DbContext(typeof(SmartParkingDbContext))]
[Migration("20261119122000_AddParkedBookingStatus")]
public partial class AddParkedBookingStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "chk_bookings_status",
            table: "bookings");

        migrationBuilder.Sql(
            "UPDATE bookings SET status = 'PARKED' " +
            "WHERE status = 'CHECKED_IN' AND EXISTS (" +
            "SELECT 1 FROM parking_sessions WHERE parking_sessions.booking_id = bookings.id " +
            "AND parking_sessions.current_slot_id IS NOT NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "chk_bookings_status",
            table: "bookings",
            sql: "status IN ('PENDING', 'CHECKED_IN', 'PARKED', 'CANCELLED', 'EXPIRED')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "chk_bookings_status",
            table: "bookings");

        migrationBuilder.Sql("UPDATE bookings SET status = 'CHECKED_IN' WHERE status = 'PARKED'");

        migrationBuilder.AddCheckConstraint(
            name: "chk_bookings_status",
            table: "bookings",
            sql: "status IN ('PENDING', 'CHECKED_IN', 'CANCELLED', 'EXPIRED')");
    }
}
