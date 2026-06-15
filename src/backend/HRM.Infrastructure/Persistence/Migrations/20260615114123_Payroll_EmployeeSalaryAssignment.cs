using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll_EmployeeSalaryAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_salary_component",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salary_structure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salary_component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    annual_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    monthly_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    is_override = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_salary_component", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "salary_revision_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_structure_id = table.Column<Guid>(type: "uuid", nullable: true),
                    new_structure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_annual_ctc = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    new_annual_ctc = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salary_revision_history", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_salary_component_employee_id_effective_to",
                table: "employee_salary_component",
                columns: new[] { "employee_id", "effective_to" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_salary_component_salary_structure_id",
                table: "employee_salary_component",
                column: "salary_structure_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_salary_component_tenant_id_employee_id",
                table: "employee_salary_component",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_salary_revision_history_tenant_id_employee_id_changed_at",
                table: "salary_revision_history",
                columns: new[] { "tenant_id", "employee_id", "changed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_salary_component");

            migrationBuilder.DropTable(
                name: "salary_revision_history");
        }
    }
}
