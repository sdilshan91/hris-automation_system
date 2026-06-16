using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll_PayslipEmailLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "leave_encashment_amount",
                table: "payroll_slip",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "leave_encashment_days",
                table: "payroll_slip",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "overtime_amount",
                table: "payroll_slip",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "overtime_hours",
                table: "payroll_slip",
                type: "numeric(7,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "payslip_email_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_slip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payslip_email_log", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payslip_email_log_tenant_run",
                table: "payslip_email_log",
                columns: new[] { "tenant_id", "payroll_run_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payslip_email_log_tenant_run_employee",
                table: "payslip_email_log",
                columns: new[] { "tenant_id", "payroll_run_id", "employee_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payslip_email_log");

            migrationBuilder.DropColumn(
                name: "leave_encashment_amount",
                table: "payroll_slip");

            migrationBuilder.DropColumn(
                name: "leave_encashment_days",
                table: "payroll_slip");

            migrationBuilder.DropColumn(
                name: "overtime_amount",
                table: "payroll_slip");

            migrationBuilder.DropColumn(
                name: "overtime_hours",
                table: "payroll_slip");
        }
    }
}
