using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Attendance_LateEarly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "early_departure_minutes",
                table: "attendance_log",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_early_departure",
                table: "attendance_log",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_late",
                table: "attendance_log",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "late_by_minutes",
                table: "attendance_log",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "late_minutes",
                table: "attendance_log",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "late_policy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    threshold_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    deduction_days = table.Column<decimal>(type: "numeric(3,1)", nullable: false, defaultValue: 0.5m),
                    period = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "MONTHLY"),
                    notification_on_late = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    chronic_threshold = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_late_policy", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_late_policy_tenant_unique",
                table: "late_policy",
                column: "tenant_id",
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "late_policy");

            migrationBuilder.DropColumn(
                name: "early_departure_minutes",
                table: "attendance_log");

            migrationBuilder.DropColumn(
                name: "is_early_departure",
                table: "attendance_log");

            migrationBuilder.DropColumn(
                name: "is_late",
                table: "attendance_log");

            migrationBuilder.DropColumn(
                name: "late_by_minutes",
                table: "attendance_log");

            migrationBuilder.DropColumn(
                name: "late_minutes",
                table: "attendance_log");
        }
    }
}
