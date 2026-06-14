using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Attendance_Overtime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "holiday_overtime_multiplier",
                table: "attendance_settings",
                type: "numeric(3,2)",
                nullable: false,
                defaultValue: 2.5m);

            migrationBuilder.AddColumn<int>(
                name: "max_daily_overtime_minutes",
                table: "attendance_settings",
                type: "integer",
                nullable: false,
                defaultValue: 240);

            migrationBuilder.AddColumn<int>(
                name: "max_weekly_overtime_minutes",
                table: "attendance_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1200);

            migrationBuilder.AddColumn<int>(
                name: "overtime_minimum_threshold_minutes",
                table: "attendance_settings",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<bool>(
                name: "require_overtime_pre_approval",
                table: "attendance_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "weekday_overtime_multiplier",
                table: "attendance_settings",
                type: "numeric(3,2)",
                nullable: false,
                defaultValue: 1.5m);

            migrationBuilder.AddColumn<decimal>(
                name: "weekend_overtime_multiplier",
                table: "attendance_settings",
                type: "numeric(3,2)",
                nullable: false,
                defaultValue: 2.0m);

            migrationBuilder.CreateTable(
                name: "overtime_record",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attendance_log_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    overtime_minutes = table.Column<int>(type: "integer", nullable: false),
                    approved_minutes = table.Column<int>(type: "integer", nullable: true),
                    multiplier = table.Column<decimal>(type: "numeric(3,2)", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    manager_comment = table.Column<string>(type: "text", nullable: true),
                    is_payroll_ready = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    daily_cap_applied = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    weekly_cap_exceeded = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    calculation_basis = table.Column<string>(type: "text", nullable: true),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_overtime_record", x => x.id);
                    table.ForeignKey(
                        name: "fk_overtime_record_attendance_log_attendance_log_id",
                        column: x => x.attendance_log_id,
                        principalTable: "attendance_log",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_overtime_record_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "overtime_approval_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    overtime_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approver_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approval_level = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    approved_minutes = table.Column<int>(type: "integer", nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true),
                    calculation_basis = table.Column<string>(type: "text", nullable: true),
                    actioned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_overtime_approval_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_overtime_approval_history_employees_approver_employee_id",
                        column: x => x.approver_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_overtime_approval_history_overtime_records_overtime_record_",
                        column: x => x.overtime_record_id,
                        principalTable: "overtime_record",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_overtime_approval_history_approver_employee_id",
                table: "overtime_approval_history",
                column: "approver_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_overtime_approval_history_overtime_record_id",
                table: "overtime_approval_history",
                column: "overtime_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_overtime_approval_history_tenant_record",
                table: "overtime_approval_history",
                columns: new[] { "tenant_id", "overtime_record_id" });

            migrationBuilder.CreateIndex(
                name: "ix_overtime_record_attendance_log_id",
                table: "overtime_record",
                column: "attendance_log_id");

            migrationBuilder.CreateIndex(
                name: "ix_overtime_record_employee_id",
                table: "overtime_record",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_overtime_record_tenant_emp_date",
                table: "overtime_record",
                columns: new[] { "tenant_id", "employee_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_overtime_record_tenant_status",
                table: "overtime_record",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "overtime_approval_history");

            migrationBuilder.DropTable(
                name: "overtime_record");

            migrationBuilder.DropColumn(
                name: "holiday_overtime_multiplier",
                table: "attendance_settings");

            migrationBuilder.DropColumn(
                name: "max_daily_overtime_minutes",
                table: "attendance_settings");

            migrationBuilder.DropColumn(
                name: "max_weekly_overtime_minutes",
                table: "attendance_settings");

            migrationBuilder.DropColumn(
                name: "overtime_minimum_threshold_minutes",
                table: "attendance_settings");

            migrationBuilder.DropColumn(
                name: "require_overtime_pre_approval",
                table: "attendance_settings");

            migrationBuilder.DropColumn(
                name: "weekday_overtime_multiplier",
                table: "attendance_settings");

            migrationBuilder.DropColumn(
                name: "weekend_overtime_multiplier",
                table: "attendance_settings");
        }
    }
}
