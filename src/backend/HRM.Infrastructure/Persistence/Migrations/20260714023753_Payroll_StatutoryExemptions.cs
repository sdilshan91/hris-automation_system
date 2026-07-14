using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll_StatutoryExemptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "statutory_exemption",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    statutory_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    calculation_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    component_id = table.Column<Guid>(type: "uuid", nullable: true),
                    max_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    is_annual = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    order_index = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statutory_exemption", x => x.id);
                    table.ForeignKey(
                        name: "fk_statutory_exemption_statutory_rules_statutory_rule_id",
                        column: x => x.statutory_rule_id,
                        principalTable: "statutory_rule",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_statutory_exemption_statutory_rule_id",
                table: "statutory_exemption",
                column: "statutory_rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_statutory_exemption_tenant_id_statutory_rule_id_order_index",
                table: "statutory_exemption",
                columns: new[] { "tenant_id", "statutory_rule_id", "order_index" });

            // DORMANT tenant-isolation RLS policy for the new tenant_id table (NEW-TENANT-TABLE rule):
            // the RlsIsolation coverage-guard test fails for any tenant_id table without a policy. tenant_id
            // is NOT NULL → strict USING + WITH CHECK (NULLIF → unset/reset GUC = NULL = fail-closed). DORMANT:
            // no ENABLE — the Rls:Enabled-gated reconciler enforces it. Idempotent. Mirrors payroll_report_exports.
            migrationBuilder.Sql("""
                DO $do$
                DECLARE
                    v_expr text := $q$tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid$q$;
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_policies
                        WHERE schemaname = 'public' AND tablename = 'statutory_exemption' AND policyname = 'tenant_isolation'
                    ) THEN
                        EXECUTE format(
                            'CREATE POLICY tenant_isolation ON public.%I USING (%s) WITH CHECK (%s)',
                            'statutory_exemption', v_expr, v_expr);
                    END IF;
                END
                $do$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "statutory_exemption");
        }
    }
}
