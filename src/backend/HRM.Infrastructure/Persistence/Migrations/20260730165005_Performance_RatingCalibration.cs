using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Performance_RatingCalibration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rating_calibration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_score = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    previous_calibrated_score = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    calibrated_score = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    calibrated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rating_calibration", x => x.id);
                    table.ForeignKey(
                        name: "fk_rating_calibration_appraisal_cycle_cycle_id",
                        column: x => x.cycle_id,
                        principalTable: "appraisal_cycle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rating_calibration_manager_review_manager_review_id",
                        column: x => x.manager_review_id,
                        principalTable: "manager_review",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rating_calibration_cycle_id",
                table: "rating_calibration",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_rating_calibration_manager_review_id",
                table: "rating_calibration",
                column: "manager_review_id");

            migrationBuilder.CreateIndex(
                name: "ix_rating_calibration_tenant_id_cycle_id_employee_id",
                table: "rating_calibration",
                columns: new[] { "tenant_id", "cycle_id", "employee_id" });

            // US-PRF-011 (tenant isolation): create the DORMANT `tenant_isolation` RLS policy for the new
            // rating_calibration table, matching the strict (tenant_id NOT NULL) form shipped by
            // 20260710120000_Platform_RlsPolicies_Dormant. It is INERT until the increment-3 reconciler ENABLEs
            // + FORCEs RLS on the table (gated by Rls:Enabled), so this is enforcement-neutral. Without it,
            // RlsIsolationPostgresTests — which force-enables RLS on every tenant_id table — would leave this
            // table policy-less (default-deny). Idempotent via IF NOT EXISTS on pg_policies.
            migrationBuilder.Sql("""
                DO $do$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_policies
                        WHERE schemaname = 'public'
                          AND tablename  = 'rating_calibration'
                          AND policyname = 'tenant_isolation'
                    ) THEN
                        EXECUTE $q$
                            CREATE POLICY tenant_isolation ON public.rating_calibration
                            USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                            WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                        $q$;
                    END IF;
                END
                $do$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON public.rating_calibration;");
            migrationBuilder.DropTable(
                name: "rating_calibration");
        }
    }
}
