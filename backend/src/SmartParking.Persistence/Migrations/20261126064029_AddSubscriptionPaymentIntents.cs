using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartParking.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPaymentIntents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscription_payment_intents",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_code = table.Column<long>(type: "bigint", nullable: false),
                    owner_user_id = table.Column<long>(type: "bigint", nullable: false),
                    vehicle_id = table.Column<long>(type: "bigint", nullable: false),
                    plan_id = table.Column<long>(type: "bigint", nullable: false),
                    lot_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    term_months = table.Column<int>(type: "integer", nullable: false),
                    billing_period_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    billing_period_end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    parking_pass_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    confirmed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_payment_intents", x => x.id);
                    table.CheckConstraint("ck_subscription_payment_intents_status", "status IN ('PENDING_PAYMENT','CONFIRMED','CANCELLED')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payment_intents_order_code",
                table: "subscription_payment_intents",
                column: "order_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payment_intents_provider_reference",
                table: "subscription_payment_intents",
                column: "provider_reference",
                unique: true,
                filter: "provider_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payment_intents_vehicle_id_lot_id",
                table: "subscription_payment_intents",
                columns: new[] { "vehicle_id", "lot_id" },
                unique: true,
                filter: "status = 'PENDING_PAYMENT'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_payment_intents");
        }
    }
}
