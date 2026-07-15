using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProbationPeriodDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "probation_period_days",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.AddColumn<int>(
                name: "probation_period_days",
                table: "locations",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "probation_period_days",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "probation_period_days",
                table: "locations");
        }
    }
}
