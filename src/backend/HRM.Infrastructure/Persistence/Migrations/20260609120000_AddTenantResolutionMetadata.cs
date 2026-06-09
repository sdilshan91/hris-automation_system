using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260609120000_AddTenantResolutionMetadata")]
    public partial class AddTenantResolutionMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "enabled_modules",
                table: "tenants",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "mfa_required_roles",
                table: "tenants",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "plan_id",
                table: "tenants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_subdomain_format",
                table: "tenants",
                sql: "subdomain = lower(subdomain) AND length(subdomain) BETWEEN 3 AND 63 AND subdomain ~ '^[a-z0-9]([a-z0-9-]*[a-z0-9])$'");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_subdomain_status",
                table: "tenants",
                columns: new[] { "subdomain", "status" },
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tenants_subdomain_status",
                table: "tenants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_subdomain_format",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "enabled_modules",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "mfa_required_roles",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "plan_id",
                table: "tenants");
        }
    }
}
