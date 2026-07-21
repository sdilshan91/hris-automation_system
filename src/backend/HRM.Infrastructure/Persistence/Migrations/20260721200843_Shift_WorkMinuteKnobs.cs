using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Shift_WorkMinuteKnobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "auto_break_minutes",
                table: "shift",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "auto_break_threshold_minutes",
                table: "shift",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "minimum_work_minutes",
                table: "shift",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "overtime_threshold_minutes",
                table: "shift",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "standard_work_minutes",
                table: "shift",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_break_minutes",
                table: "shift");

            migrationBuilder.DropColumn(
                name: "auto_break_threshold_minutes",
                table: "shift");

            migrationBuilder.DropColumn(
                name: "minimum_work_minutes",
                table: "shift");

            migrationBuilder.DropColumn(
                name: "overtime_threshold_minutes",
                table: "shift");

            migrationBuilder.DropColumn(
                name: "standard_work_minutes",
                table: "shift");
        }
    }
}
