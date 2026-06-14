using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Attendance_ClockOut : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "auto_break_minutes",
                table: "attendance_settings",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "auto_break_threshold_minutes",
                table: "attendance_settings",
                type: "integer",
                nullable: false,
                defaultValue: 360);

            migrationBuilder.AddColumn<int>(
                name: "minimum_work_minutes",
                table: "attendance_settings",
                type: "integer",
                nullable: false,
                defaultValue: 240);

            migrationBuilder.AddColumn<int>(
                name: "overtime_threshold_minutes",
                table: "attendance_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "standard_work_minutes",
                table: "attendance_settings",
                type: "integer",
                nullable: false,
                defaultValue: 480);

            migrationBuilder.AddColumn<string>(
                name: "clock_out_ip",
                table: "attendance_log",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "clock_out_latitude",
                table: "attendance_log",
                type: "numeric(10,7)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "clock_out_longitude",
                table: "attendance_log",
                type: "numeric(10,7)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "overtime_minutes",
                table: "attendance_log",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "attendance_log",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "total_work_minutes",
                table: "attendance_log",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_break_minutes",
                table: "attendance_settings");

            migrationBuilder.DropColumn(
                name: "auto_break_threshold_minutes",
                table: "attendance_settings");

            migrationBuilder.DropColumn(
                name: "minimum_work_minutes",
                table: "attendance_settings");

            migrationBuilder.DropColumn(
                name: "overtime_threshold_minutes",
                table: "attendance_settings");

            migrationBuilder.DropColumn(
                name: "standard_work_minutes",
                table: "attendance_settings");

            migrationBuilder.DropColumn(
                name: "clock_out_ip",
                table: "attendance_log");

            migrationBuilder.DropColumn(
                name: "clock_out_latitude",
                table: "attendance_log");

            migrationBuilder.DropColumn(
                name: "clock_out_longitude",
                table: "attendance_log");

            migrationBuilder.DropColumn(
                name: "overtime_minutes",
                table: "attendance_log");

            migrationBuilder.DropColumn(
                name: "status",
                table: "attendance_log");

            migrationBuilder.DropColumn(
                name: "total_work_minutes",
                table: "attendance_log");
        }
    }
}
