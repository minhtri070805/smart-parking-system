using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkingOperations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveOperationsTablesToSharedSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "operations");

            migrationBuilder.RenameTable(
                name: "shifts",
                newName: "shifts",
                newSchema: "operations");

            migrationBuilder.RenameTable(
                name: "shift_assignments",
                newName: "shift_assignments",
                newSchema: "operations");

            migrationBuilder.RenameTable(
                name: "cash_reconciliations",
                newName: "cash_reconciliations",
                newSchema: "operations");

            migrationBuilder.RenameTable(
                name: "cash_handovers",
                newName: "cash_handovers",
                newSchema: "operations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "shifts",
                schema: "operations",
                newName: "shifts");

            migrationBuilder.RenameTable(
                name: "shift_assignments",
                schema: "operations",
                newName: "shift_assignments");

            migrationBuilder.RenameTable(
                name: "cash_reconciliations",
                schema: "operations",
                newName: "cash_reconciliations");

            migrationBuilder.RenameTable(
                name: "cash_handovers",
                schema: "operations",
                newName: "cash_handovers");
        }
    }
}
