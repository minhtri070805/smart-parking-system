using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartParking.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPlansAndPasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscription_plans",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    lot_id = table.Column<int>(type: "integer", nullable: false),
                    zone_id = table.Column<int>(type: "integer", nullable: false),
                    slot_id = table.Column<long>(type: "bigint", nullable: true),
                    term_months = table.Column<int>(type: "integer", nullable: false),
                    price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    effective_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    approved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_plans", x => x.id);
                    table.CheckConstraint("ck_subscription_plans_price", "price_amount >= 0");
                    table.CheckConstraint("ck_subscription_plans_status", "status IN ('DRAFT','APPROVED','RETIRED')");
                    table.CheckConstraint("ck_subscription_plans_term", "term_months > 0");
                    table.ForeignKey(
                        name: "FK_subscription_plans_parking_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "parking_lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subscription_plans_parking_slots_slot_id",
                        column: x => x.slot_id,
                        principalTable: "parking_slots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subscription_plans_parking_zones_zone_id",
                        column: x => x.zone_id,
                        principalTable: "parking_zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parking_passes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_user_id = table.Column<long>(type: "bigint", nullable: false),
                    vehicle_id = table.Column<long>(type: "bigint", nullable: false),
                    plan_id = table.Column<long>(type: "bigint", nullable: false),
                    plan_version = table.Column<int>(type: "integer", nullable: false),
                    lot_id = table.Column<int>(type: "integer", nullable: false),
                    zone_id = table.Column<int>(type: "integer", nullable: false),
                    slot_id = table.Column<long>(type: "bigint", nullable: true),
                    starts_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ends_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    payment_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parking_passes", x => x.id);
                    table.CheckConstraint("ck_parking_passes_dates", "ends_at_utc > starts_at_utc");
                    table.CheckConstraint("ck_parking_passes_price", "price_amount >= 0");
                    table.CheckConstraint("ck_parking_passes_status", "status IN ('PENDING','SCHEDULED','ACTIVE','EXPIRED','SUSPENDED','CANCELLED')");
                    table.ForeignKey(
                        name: "FK_parking_passes_subscription_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_parking_passes_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_parking_passes_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_parking_passes_owner_user_id_idempotency_key",
                table: "parking_passes",
                columns: new[] { "owner_user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_parking_passes_plan_id",
                table: "parking_passes",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_parking_passes_slot_id",
                table: "parking_passes",
                column: "slot_id",
                unique: true,
                filter: "slot_id IS NOT NULL AND status IN ('PENDING','SCHEDULED','ACTIVE')");

            migrationBuilder.CreateIndex(
                name: "IX_parking_passes_vehicle_id_zone_id",
                table: "parking_passes",
                columns: new[] { "vehicle_id", "zone_id" },
                unique: true,
                filter: "status IN ('PENDING','SCHEDULED','ACTIVE')");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_plans_code_version",
                table: "subscription_plans",
                columns: new[] { "code", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscription_plans_lot_id",
                table: "subscription_plans",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_plans_slot_id",
                table: "subscription_plans",
                column: "slot_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_plans_status_lot_id_zone_id_effective_from_utc",
                table: "subscription_plans",
                columns: new[] { "status", "lot_id", "zone_id", "effective_from_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_subscription_plans_zone_id",
                table: "subscription_plans",
                column: "zone_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parking_passes");

            migrationBuilder.DropTable(
                name: "subscription_plans");
        }
    }
}
