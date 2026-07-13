using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll_AddPayrollReportExport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_report_exports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    filters_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    file_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_report_exports", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_report_exports_tenant_requested_at",
                table: "payroll_report_exports",
                columns: new[] { "tenant_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_report_exports_tenant_user_status",
                table: "payroll_report_exports",
                columns: new[] { "tenant_id", "requested_by_user_id", "status" });

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
                        WHERE schemaname = 'public' AND tablename = 'payroll_report_exports' AND policyname = 'tenant_isolation'
                    ) THEN
                        EXECUTE format(
                            'CREATE POLICY tenant_isolation ON public.%I USING (%s) WITH CHECK (%s)',
                            'payroll_report_exports', v_expr, v_expr);
                    END IF;
                END
                $do$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_report_exports");
        }
    }
}
