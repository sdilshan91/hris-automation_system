using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Plan_MaxTemplateLanguageVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_template_language_variants",
                table: "tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_template_language_variants",
                table: "subscription_plans",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "max_template_language_variants",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "max_template_language_variants",
                table: "subscription_plans");
        }
    }
}
