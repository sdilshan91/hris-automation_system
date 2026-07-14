using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll_MultiCountryTaxFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_statutory_rule_tenant_id_rule_type_fiscal_year",
                table: "statutory_rule");

            migrationBuilder.AddColumn<string>(
                name: "default_country_code",
                table: "tenants",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "country_code",
                table: "locations",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_statutory_rule_tenant_id_rule_type_country_code_fiscal_year",
                table: "statutory_rule",
                columns: new[] { "tenant_id", "rule_type", "country_code", "fiscal_year", "effective_from" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_statutory_rule_tenant_id_rule_type_country_code_fiscal_year",
                table: "statutory_rule");

            migrationBuilder.DropColumn(
                name: "default_country_code",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "country_code",
                table: "locations");

            migrationBuilder.CreateIndex(
                name: "ix_statutory_rule_tenant_id_rule_type_fiscal_year",
                table: "statutory_rule",
                columns: new[] { "tenant_id", "rule_type", "fiscal_year" });
        }
    }
}
