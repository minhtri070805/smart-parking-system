using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ParkingOperations.Persistence.Data;

#nullable disable

namespace ParkingOperations.Persistence.Migrations;

[DbContext(typeof(OperationsDbContext))]
[Migration("20261123120000_RemoveCashHandover")]
public sealed class RemoveCashHandover : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "cash_handovers", schema: "operations");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "cash_handovers",
            schema: "operations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                lot_id = table.Column<int>(type: "integer", nullable: false),
                business_date = table.Column<DateOnly>(type: "date", nullable: false),
                from_shift_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                to_shift_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                outgoing_shift_id = table.Column<Guid>(type: "uuid", nullable: true),
                incoming_shift_id = table.Column<Guid>(type: "uuid", nullable: true),
                outgoing_staff_id = table.Column<long>(type: "bigint", nullable: false),
                incoming_staff_id = table.Column<long>(type: "bigint", nullable: false),
                created_by_manager_id = table.Column<long>(type: "bigint", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                outgoing_counted_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                outgoing_confirmed_by = table.Column<long>(type: "bigint", nullable: true),
                outgoing_confirmed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                incoming_counted_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                incoming_confirmed_by = table.Column<long>(type: "bigint", nullable: true),
                incoming_confirmed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                dispute_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                disputed_by = table.Column<long>(type: "bigint", nullable: true),
                disputed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                version = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cash_handovers", x => x.id);
                table.ForeignKey(name: "FK_cash_handovers_shifts_incoming_shift_id", column: x => x.incoming_shift_id, principalTable: "shifts", principalColumn: "id", principalSchema: "operations", onDelete: ReferentialAction.Restrict);
                table.ForeignKey(name: "FK_cash_handovers_shifts_outgoing_shift_id", column: x => x.outgoing_shift_id, principalTable: "shifts", principalColumn: "id", principalSchema: "operations", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "IX_cash_handovers_incoming_shift_id", schema: "operations", table: "cash_handovers", column: "incoming_shift_id");
        migrationBuilder.CreateIndex(name: "IX_cash_handovers_outgoing_shift_id", schema: "operations", table: "cash_handovers", column: "outgoing_shift_id");
        migrationBuilder.CreateIndex(name: "ux_cash_handovers_lot_date_outgoing_shift", schema: "operations", table: "cash_handovers", columns: new[] { "lot_id", "business_date", "outgoing_shift_id" }, unique: true);
        migrationBuilder.CreateIndex(name: "ix_cash_handovers_lot_date_status", schema: "operations", table: "cash_handovers", columns: new[] { "lot_id", "business_date", "status" });
    }
}
