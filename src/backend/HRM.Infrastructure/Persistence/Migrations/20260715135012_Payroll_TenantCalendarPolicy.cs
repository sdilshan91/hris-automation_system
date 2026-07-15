using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll_TenantCalendarPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_payroll_calendar_policy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    exclude_holidays_from_working_days = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_payroll_calendar_policy", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_payroll_calendar_policy_tenant_id_effective_from",
                table: "tenant_payroll_calendar_policy",
                columns: new[] { "tenant_id", "effective_from" });

            // DORMANT tenant-isolation RLS policy for the new tenant_id table (NEW-TENANT-TABLE rule): the
            // RlsIsolation coverage-guard test fails for any tenant_id table without a policy. tenant_id is
            // NOT NULL → strict USING + WITH CHECK (NULLIF → unset/reset GUC = NULL = fail-closed).
            // DORMANT: no ENABLE — the Rls:Enabled-gated reconciler enforces it. Idempotent. Mirrors
            // tenant_fnf_policy / statutory_exemption.
            migrationBuilder.Sql("""
                DO $do$
                DECLARE
                    v_expr text := $q$tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid$q$;
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_policies
                        WHERE schemaname = 'public' AND tablename = 'tenant_payroll_calendar_policy' AND policyname = 'tenant_isolation'
                    ) THEN
                        EXECUTE format(
                            'CREATE POLICY tenant_isolation ON public.%I USING (%s) WITH CHECK (%s)',
                            'tenant_payroll_calendar_policy', v_expr, v_expr);
                    END IF;
                END
                $do$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_payroll_calendar_policy");
        }
    }
}
