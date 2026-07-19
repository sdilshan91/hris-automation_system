using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeProfileExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "city",
                table: "employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "country",
                table: "employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "postal_code",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "state",
                table: "employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "employee_dependents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    relationship = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_dependents", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_dependents_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    institution = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    degree = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    field_of_study = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    start_year = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    end_year = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_education", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_education_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_work_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    position = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: true),
                    to_date = table.Column<DateOnly>(type: "date", nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_work_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_work_history_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_dependents_employee_id",
                table: "employee_dependents",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_dependents_tenant_employee",
                table: "employee_dependents",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_education_employee_id",
                table: "employee_education",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_education_tenant_employee",
                table: "employee_education",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_work_history_employee_id",
                table: "employee_work_history",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_work_history_tenant_employee",
                table: "employee_work_history",
                columns: new[] { "tenant_id", "employee_id" });

            // DORMANT tenant-isolation RLS policies for the new tenant_id tables (NEW-TENANT-TABLE rule): the
            // RlsIsolation coverage-guard test fails for any tenant_id table without a policy. tenant_id is
            // NOT NULL → strict USING + WITH CHECK (NULLIF → unset/reset GUC = NULL = fail-closed).
            // DORMANT: no ENABLE — the Rls:Enabled-gated reconciler enforces it. Idempotent. Mirrors
            // payroll_approval_step_config / tenant_payroll_calendar_policy / statutory_exemption.
            foreach (var tableName in new[] { "employee_education", "employee_work_history", "employee_dependents" })
            {
                migrationBuilder.Sql($$"""
                    DO $do$
                    DECLARE
                        v_expr text := $q$tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid$q$;
                    BEGIN
                        IF NOT EXISTS (
                            SELECT 1 FROM pg_policies
                            WHERE schemaname = 'public' AND tablename = '{{tableName}}' AND policyname = 'tenant_isolation'
                        ) THEN
                            EXECUTE format(
                                'CREATE POLICY tenant_isolation ON public.%I USING (%s) WITH CHECK (%s)',
                                '{{tableName}}', v_expr, v_expr);
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
                name: "employee_dependents");

            migrationBuilder.DropTable(
                name: "employee_education");

            migrationBuilder.DropTable(
                name: "employee_work_history");

            migrationBuilder.DropColumn(
                name: "city",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "country",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "postal_code",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "state",
                table: "employees");
        }
    }
}
