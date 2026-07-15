using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Employee_FteAndWorkArrangement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "fte",
                table: "employees",
                type: "numeric(3,2)",
                nullable: false,
                defaultValue: 1.00m);

            migrationBuilder.AddColumn<int>(
                name: "work_arrangement",
                table: "employees",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "fte_scaled_overtime_base",
                table: "attendance_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fte",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "work_arrangement",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "fte_scaled_overtime_base",
                table: "attendance_settings");
        }
    }
}
