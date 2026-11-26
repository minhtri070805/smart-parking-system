using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Features.PayOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayOSSubscriptionPaymentAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payos_subscription_payment_attempts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_code = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    plan_id = table.Column<long>(type: "bigint", nullable: false),
                    vehicle_id = table.Column<long>(type: "bigint", nullable: false),
                    core_payment_intent_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    billing_period_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    billing_period_end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    payment_link_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    checkout_url = table.Column<string>(type: "text", nullable: true),
                    provider_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    paid_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    core_confirmed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    core_confirmation_attempts = table.Column<int>(type: "integer", nullable: false),
                    next_core_retry_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_core_error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payos_subscription_payment_attempts", x => x.id);
                    table.CheckConstraint("ck_payos_subscription_payment_attempts_status", "status IN ('CREATING','PENDING','PAID_PENDING_CORE_CONFIRMATION','CONFIRMED','FAILED','CANCELLED','LINK_CREATION_FAILED')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_payos_subscription_payment_attempts_order_code",
                table: "payos_subscription_payment_attempts",
                column: "order_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payos_subscription_payment_attempts_provider_reference",
                table: "payos_subscription_payment_attempts",
                column: "provider_reference",
                unique: true,
                filter: "provider_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payos_subscription_payment_attempts_status_next_core_retry_~",
                table: "payos_subscription_payment_attempts",
                columns: new[] { "status", "next_core_retry_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_payos_subscription_payment_attempts_user_id_idempotency_key",
                table: "payos_subscription_payment_attempts",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payos_subscription_payment_attempts");
        }
    }
}
