using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll_FnFSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "final_settlement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offboarding_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_working_day = table.Column<DateOnly>(type: "date", nullable: false),
                    country_code = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    fiscal_year = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    pro_rated_gross = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    statutory_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    leave_encashment_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    net_payable = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    policy_effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    final_period_owned_by_settlement = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    statutory_skipped = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    computed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_final_settlement", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_fnf_policy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    include_pro_rated_final_pay = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    include_statutory = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    include_leave_encashment = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    final_period_owned_by_settlement = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
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
                    table.PrimaryKey("pk_tenant_fnf_policy", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "final_settlement_line",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    final_settlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_final_settlement_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_final_settlement_line_final_settlement_final_settlement_id",
                        column: x => x.final_settlement_id,
                        principalTable: "final_settlement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_final_settlement_offboarding_instance_id",
                table: "final_settlement",
                column: "offboarding_instance_id",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_final_settlement_tenant_id_last_working_day",
                table: "final_settlement",
                columns: new[] { "tenant_id", "last_working_day" });

            migrationBuilder.CreateIndex(
                name: "ix_final_settlement_line_final_settlement_id",
                table: "final_settlement_line",
                column: "final_settlement_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_fnf_policy_tenant_id_effective_from",
                table: "tenant_fnf_policy",
                columns: new[] { "tenant_id", "effective_from" });

            // DORMANT tenant-isolation RLS policies for the three new tenant_id tables (NEW-TENANT-TABLE rule):
            // the RlsIsolation coverage-guard test fails for any tenant_id table without a policy. tenant_id is
            // NOT NULL on all three → strict USING + WITH CHECK (NULLIF → unset/reset GUC = NULL = fail-closed).
            // DORMANT: no ENABLE — the Rls:Enabled-gated reconciler enforces it. Idempotent. Mirrors
            // statutory_exemption / payroll_report_exports.
            foreach (var table in new[] { "tenant_fnf_policy", "final_settlement", "final_settlement_line" })
            {
                migrationBuilder.Sql($"""
                    DO $do$
                    DECLARE
                        v_expr text := $q$tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid$q$;
                    BEGIN
                        IF NOT EXISTS (
                            SELECT 1 FROM pg_policies
                            WHERE schemaname = 'public' AND tablename = '{table}' AND policyname = 'tenant_isolation'
                        ) THEN
                            EXECUTE format(
                                'CREATE POLICY tenant_isolation ON public.%I USING (%s) WITH CHECK (%s)',
                                '{table}', v_expr, v_expr);
                        END IF;
                    END
                    $do$;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "final_settlement_line");

            migrationBuilder.DropTable(
                name: "tenant_fnf_policy");

            migrationBuilder.DropTable(
                name: "final_settlement");
        }
    }
}
