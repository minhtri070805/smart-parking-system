using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartParking.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingPaymentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payment_invoice_id",
                table: "bookings",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_mode",
                table: "bookings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "PAY_AT_COUNTER");

            migrationBuilder.AddColumn<DateTime>(
                name: "payment_paid_at",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_status",
                table: "bookings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "PAY_AT_COUNTER");

            migrationBuilder.AddColumn<string>(
                name: "payment_transaction_ref",
                table: "bookings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "prepaid_amount",
                table: "bookings",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payment_invoice_id",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "payment_mode",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "payment_paid_at",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "payment_status",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "payment_transaction_ref",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "prepaid_amount",
                table: "bookings");
        }
    }
}
