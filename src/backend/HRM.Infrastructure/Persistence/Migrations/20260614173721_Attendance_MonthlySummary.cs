using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Attendance_MonthlySummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "half_day_enabled",
                table: "attendance_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "attendance_monthly_summary",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year_month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    total_present_days = table.Column<decimal>(type: "numeric(4,1)", nullable: false),
                    total_absent_days = table.Column<decimal>(type: "numeric(4,1)", nullable: false),
                    total_late_count = table.Column<int>(type: "integer", nullable: false),
                    total_early_departure_count = table.Column<int>(type: "integer", nullable: false),
                    total_work_minutes = table.Column<int>(type: "integer", nullable: false),
                    total_overtime_minutes = table.Column<int>(type: "integer", nullable: false),
                    total_leave_days = table.Column<decimal>(type: "numeric(4,1)", nullable: false),
                    total_holidays = table.Column<int>(type: "integer", nullable: false),
                    total_weekly_offs = table.Column<int>(type: "integer", nullable: false),
                    lop_days = table.Column<decimal>(type: "numeric(4,1)", nullable: false),
                    generated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_monthly_summary", x => x.id);
                    table.ForeignKey(
                        name: "fk_attendance_monthly_summary_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_monthly_summary_employee_id",
                table: "attendance_monthly_summary",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_monthly_summary_tenant_month",
                table: "attendance_monthly_summary",
                columns: new[] { "tenant_id", "year_month" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_monthly_summary_unique",
                table: "attendance_monthly_summary",
                columns: new[] { "tenant_id", "employee_id", "year_month" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_monthly_summary");

            migrationBuilder.DropColumn(
                name: "half_day_enabled",
                table: "attendance_settings");
        }
    }
}
