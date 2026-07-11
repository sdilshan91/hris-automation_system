using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Benefits_Plans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "benefit_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    coverage_details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    employer_cost = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    employee_cost = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    enrollment_opens_at = table.Column<DateOnly>(type: "date", nullable: true),
                    enrollment_closes_at = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_benefit_plans", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_benefit_plans_tenant_id_status",
                table: "benefit_plans",
                columns: new[] { "tenant_id", "status" });

            // RLS (NEW-TENANT-TABLE RULE): this new tenant-scoped table needs its own DORMANT tenant_isolation
            // policy — the Platform_RlsPolicies_Dormant DO-block only covered tables existing at ITS apply-time,
            // and the RlsIsolation coverage-guard test fails for any tenant_id table without a policy. tenant_id
            // is NOT NULL → strict USING + WITH CHECK (NULLIF → unset/reset GUC = NULL = fail-closed). DORMANT:
            // no ENABLE — the Rls:Enabled-gated reconciler enforces it. Idempotent.
            migrationBuilder.Sql("""
                DO $do$
                DECLARE
                    v_expr text := $q$tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid$q$;
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_policies
                        WHERE schemaname = 'public' AND tablename = 'benefit_plans' AND policyname = 'tenant_isolation'
                    ) THEN
                        EXECUTE format(
                            'CREATE POLICY tenant_isolation ON public.%I USING (%s) WITH CHECK (%s)',
                            'benefit_plans', v_expr, v_expr);
                    END IF;
                END
                $do$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "benefit_plans");
        }
    }
}
