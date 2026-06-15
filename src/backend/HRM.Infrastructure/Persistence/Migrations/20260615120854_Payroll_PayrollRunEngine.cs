using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll_PayrollRunEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_run",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pay_month = table.Column<int>(type: "integer", nullable: false),
                    pay_year = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    total_employees = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    processed_employees = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    skipped_employees = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_gross = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    total_deductions = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    total_net = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    total_statutory = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    initiated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    initiated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    finalized_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    run_log = table.Column<string>(type: "text", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_run", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_slip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gross_earnings = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    total_deductions = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    net_salary = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    lop_days = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    working_days = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    paid_days = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    pay_month = table.Column<int>(type: "integer", nullable: false),
                    pay_year = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_slip", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_slip_detail",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_slip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salary_component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    component_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    calculation_basis = table.Column<string>(type: "text", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_slip_detail", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_idempotency_key_per_tenant",
                table: "payroll_run",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_one_active_per_period",
                table: "payroll_run",
                columns: new[] { "tenant_id", "pay_year", "pay_month" },
                unique: true,
                filter: "status <> 'Cancelled' AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_slip_tenant_run_employee",
                table: "payroll_slip",
                columns: new[] { "tenant_id", "payroll_run_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_slip_detail_tenant_id_payroll_slip_id",
                table: "payroll_slip_detail",
                columns: new[] { "tenant_id", "payroll_slip_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_run");

            migrationBuilder.DropTable(
                name: "payroll_slip");

            migrationBuilder.DropTable(
                name: "payroll_slip_detail");
        }
    }
}
