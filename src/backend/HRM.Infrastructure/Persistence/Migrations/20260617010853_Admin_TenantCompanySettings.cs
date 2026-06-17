using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Admin_TenantCompanySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "company_size",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "tenants",
                type: "text",
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.AddColumn<string>(
                name: "date_format",
                table: "tenants",
                type: "text",
                nullable: false,
                defaultValue: "dd MMM yyyy");

            migrationBuilder.AddColumn<string>(
                name: "default_language",
                table: "tenants",
                type: "text",
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<string>(
                name: "email_logo_url",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "favicon_url",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "fiscal_year_start_month",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "industry",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "legal_name",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "number_format",
                table: "tenants",
                type: "text",
                nullable: false,
                defaultValue: "1,234.56");

            migrationBuilder.AddColumn<int>(
                name: "password_max_age_days",
                table: "tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "registration_number",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "time_zone",
                table: "tenants",
                type: "text",
                nullable: false,
                defaultValue: "UTC");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "address",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "company_size",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "date_format",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "default_language",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "email_logo_url",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "favicon_url",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "fiscal_year_start_month",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "industry",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "legal_name",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "number_format",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "password_max_age_days",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "registration_number",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "time_zone",
                table: "tenants");
        }
    }
}
