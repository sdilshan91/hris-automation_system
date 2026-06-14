using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Attendance_ClockIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attendance_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clock_in = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    clock_out = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    clock_in_latitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    clock_in_longitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    clock_in_ip = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    clock_in_user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    clock_in_photo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_attendance_log_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attendance_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    require_geolocation = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    geo_fence_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    geo_fence_latitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    geo_fence_longitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    geo_fence_radius_meters = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    ip_allowlist_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ip_allowlist = table.Column<List<string>>(type: "text[]", nullable: false),
                    require_photo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    grace_period_minutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_log_employee_id",
                table: "attendance_log",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_log_open_unique",
                table: "attendance_log",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true,
                filter: "clock_out IS NULL AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_log_tenant_emp_clockin",
                table: "attendance_log",
                columns: new[] { "tenant_id", "employee_id", "clock_in" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_settings_tenant_unique",
                table: "attendance_settings",
                column: "tenant_id",
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_log");

            migrationBuilder.DropTable(
                name: "attendance_settings");
        }
    }
}
