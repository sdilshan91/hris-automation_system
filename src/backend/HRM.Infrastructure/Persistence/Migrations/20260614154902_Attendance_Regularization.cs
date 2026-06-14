using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Attendance_Regularization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "regularization_lookback_days",
                table: "attendance_settings",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.CreateTable(
                name: "attendance_regularization",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attendance_log_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    regularization_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_clock_in = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    requested_clock_out = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
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
                    table.PrimaryKey("pk_attendance_regularization", x => x.id);
                    table.ForeignKey(
                        name: "fk_attendance_regularization_attendance_log_attendance_log_id",
                        column: x => x.attendance_log_id,
                        principalTable: "attendance_log",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attendance_regularization_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payroll_lock_period",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_lock_period", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_regularization_attendance_log_id",
                table: "attendance_regularization",
                column: "attendance_log_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_regularization_employee_id",
                table: "attendance_regularization",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_regularization_pending_unique",
                table: "attendance_regularization",
                columns: new[] { "tenant_id", "employee_id", "date" },
                unique: true,
                filter: "status = 'PENDING' AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_lock_period_tenant_range",
                table: "payroll_lock_period",
                columns: new[] { "tenant_id", "start_date", "end_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_regularization");

            migrationBuilder.DropTable(
                name: "payroll_lock_period");

            migrationBuilder.DropColumn(
                name: "regularization_lookback_days",
                table: "attendance_settings");
        }
    }
}
