using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSsoSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "allowed_email_domains",
                table: "tenants",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "allowed_entra_tenant_ids",
                table: "tenants",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "jit_default_role",
                table: "tenants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "jit_enabled",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "sso_enabled",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "sso_enforcement_mode",
                table: "tenants",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "optional");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allowed_email_domains",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "allowed_entra_tenant_ids",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "jit_default_role",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "jit_enabled",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "sso_enabled",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "sso_enforcement_mode",
                table: "tenants");
        }
    }
}
