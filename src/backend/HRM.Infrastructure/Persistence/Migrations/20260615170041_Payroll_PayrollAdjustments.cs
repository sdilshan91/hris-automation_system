using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll_PayrollAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_adjustment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adjustment_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    applicable_pay_month = table.Column<int>(type: "integer", nullable: false),
                    applicable_pay_year = table.Column<int>(type: "integer", nullable: false),
                    is_taxable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_recurring = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    recurrence_end_month = table.Column<int>(type: "integer", nullable: true),
                    recurrence_end_year = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    applied_in_payroll_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference_payroll_slip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supporting_document_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recurring_series_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_adjustment", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_adjustment_tenant_id_applicable_pay_year_applicable",
                table: "payroll_adjustment",
                columns: new[] { "tenant_id", "applicable_pay_year", "applicable_pay_month", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_adjustment_tenant_id_employee_id",
                table: "payroll_adjustment",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_adjustment_tenant_id_recurring_series_id",
                table: "payroll_adjustment",
                columns: new[] { "tenant_id", "recurring_series_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_adjustment");
        }
    }
}
