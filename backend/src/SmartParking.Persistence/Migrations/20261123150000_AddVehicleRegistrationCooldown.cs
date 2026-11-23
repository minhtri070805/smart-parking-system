using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartParking.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleRegistrationCooldown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vehicle_registration_blocks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    plate_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    blocked_until_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicle_registration_blocks", x => x.id);
                    table.CheckConstraint("chk_vehicle_registration_blocks_plate_normalized", "plate_number = UPPER(TRIM(plate_number))");
                });

            migrationBuilder.CreateIndex(
                name: "uq_vehicle_registration_blocks_plate_number",
                table: "vehicle_registration_blocks",
                column: "plate_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vehicle_registration_blocks");
        }
    }
}
