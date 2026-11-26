using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartParking.Persistence.Migrations.SmartParkingDb
{
    /// <inheritdoc />
    public partial class AddParkingLotCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "latitude",
                table: "parking_lots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "longitude",
                table: "parking_lots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_parking_lots_coordinates",
                table: "parking_lots",
                sql: "(latitude IS NULL AND longitude IS NULL) OR (latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_parking_lots_coordinates",
                table: "parking_lots");

            migrationBuilder.DropColumn(
                name: "latitude",
                table: "parking_lots");

            migrationBuilder.DropColumn(
                name: "longitude",
                table: "parking_lots");
        }
    }
}
