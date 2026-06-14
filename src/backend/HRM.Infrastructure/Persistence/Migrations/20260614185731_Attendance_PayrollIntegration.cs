using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Attendance_PayrollIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_lock_period");

            migrationBuilder.CreateTable(
                name: "attendance_period_lock",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    locked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    locked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    unlocked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    unlocked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_period_lock", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_period_lock_active_unique",
                table: "attendance_period_lock",
                columns: new[] { "tenant_id", "period_start", "period_end" },
                unique: true,
                filter: "is_locked = true AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_period_lock_tenant_range",
                table: "attendance_period_lock",
                columns: new[] { "tenant_id", "is_locked", "period_start", "period_end" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_period_lock");

            migrationBuilder.CreateTable(
                name: "payroll_lock_period",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_lock_period", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_lock_period_tenant_range",
                table: "payroll_lock_period",
                columns: new[] { "tenant_id", "start_date", "end_date" });
        }
    }
}
