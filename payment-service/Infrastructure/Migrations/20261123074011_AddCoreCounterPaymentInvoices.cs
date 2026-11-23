using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCoreCounterPaymentInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CoreSessionId",
                table: "Invoices",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CoreSessionId",
                table: "Invoices",
                column: "CoreSessionId",
                unique: true,
                filter: "\"Purpose\" = 'COUNTER_CHECKOUT' AND \"CoreSessionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_CoreSessionId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CoreSessionId",
                table: "Invoices");
        }
    }
}
