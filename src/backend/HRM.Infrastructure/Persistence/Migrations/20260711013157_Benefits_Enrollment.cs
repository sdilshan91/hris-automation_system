using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Benefits_Enrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "benefit_eligibility_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    benefit_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    @operator = table.Column<string>(name: "operator", type: "character varying(4)", maxLength: 4, nullable: false),
                    value = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_benefit_eligibility_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_benefit_eligibility_rules_benefit_plans_benefit_plan_id",
                        column: x => x.benefit_plan_id,
                        principalTable: "benefit_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "benefit_enrollments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    benefit_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    coverage_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    elected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    elected_by = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_benefit_enrollments", x => x.id);
                    table.ForeignKey(
                        name: "fk_benefit_enrollments_benefit_plans_benefit_plan_id",
                        column: x => x.benefit_plan_id,
                        principalTable: "benefit_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_benefit_eligibility_rules_benefit_plan_id",
                table: "benefit_eligibility_rules",
                column: "benefit_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_benefit_eligibility_rules_tenant_id_benefit_plan_id",
                table: "benefit_eligibility_rules",
                columns: new[] { "tenant_id", "benefit_plan_id" });

            migrationBuilder.CreateIndex(
                name: "ix_benefit_enrollments_benefit_plan_id",
                table: "benefit_enrollments",
                column: "benefit_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_benefit_enrollments_tenant_id_employee_id_status",
                table: "benefit_enrollments",
                columns: new[] { "tenant_id", "employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_benefit_enrollments_active",
                table: "benefit_enrollments",
                columns: new[] { "tenant_id", "benefit_plan_id", "employee_id" },
                unique: true,
                filter: "is_deleted = false AND status = 'Active'");

            // RLS (NEW-TENANT-TABLE RULE): these two new tenant-scoped tables each need their own DORMANT
            // tenant_isolation policy — the Platform_RlsPolicies_Dormant DO-block only covered tables existing at
            // ITS apply-time, and the RlsIsolation coverage-guard test fails for any tenant_id table without a
            // policy. tenant_id is NOT NULL → strict USING + WITH CHECK (NULLIF → unset/reset GUC = NULL =
            // fail-closed). DORMANT: no ENABLE — the Rls:Enabled-gated reconciler enforces it. Idempotent.
            migrationBuilder.Sql("""
                DO $do$
                DECLARE
                    v_expr text := $q$tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid$q$;
                    v_table text;
                BEGIN
                    FOREACH v_table IN ARRAY ARRAY['benefit_eligibility_rules', 'benefit_enrollments'] LOOP
                        IF NOT EXISTS (
                            SELECT 1 FROM pg_policies
                            WHERE schemaname = 'public' AND tablename = v_table AND policyname = 'tenant_isolation'
                        ) THEN
                            EXECUTE format(
                                'CREATE POLICY tenant_isolation ON public.%I USING (%s) WITH CHECK (%s)',
                                v_table, v_expr, v_expr);
                        END IF;
                    END LOOP;
                END
                $do$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "benefit_eligibility_rules");

            migrationBuilder.DropTable(
                name: "benefit_enrollments");
        }
    }
}
