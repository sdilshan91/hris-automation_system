using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll_StatutoryRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "statutory_rule",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    rule_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    country_code = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    fiscal_year = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statutory_rule", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "social_security_rule",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    statutory_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_rate = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    employer_rate = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    wage_ceiling_annual = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    applicable_on = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    applicable_component_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_social_security_rule", x => x.id);
                    table.ForeignKey(
                        name: "fk_social_security_rule_statutory_rules_statutory_rule_id",
                        column: x => x.statutory_rule_id,
                        principalTable: "statutory_rule",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_slab",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    statutory_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slab_from = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    slab_to = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    rate_percentage = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    order_index = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_slab", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_slab_statutory_rule_statutory_rule_id",
                        column: x => x.statutory_rule_id,
                        principalTable: "statutory_rule",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_social_security_rule_statutory_rule_id",
                table: "social_security_rule",
                column: "statutory_rule_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_social_security_rule_tenant_id_statutory_rule_id",
                table: "social_security_rule",
                columns: new[] { "tenant_id", "statutory_rule_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_statutory_rule_tenant_id_rule_type_effective_from",
                table: "statutory_rule",
                columns: new[] { "tenant_id", "rule_type", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_statutory_rule_tenant_id_rule_type_fiscal_year",
                table: "statutory_rule",
                columns: new[] { "tenant_id", "rule_type", "fiscal_year" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_slab_statutory_rule_id",
                table: "tax_slab",
                column: "statutory_rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_slab_tenant_id_statutory_rule_id_order_index",
                table: "tax_slab",
                columns: new[] { "tenant_id", "statutory_rule_id", "order_index" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "social_security_rule");

            migrationBuilder.DropTable(
                name: "tax_slab");

            migrationBuilder.DropTable(
                name: "statutory_rule");
        }
    }
}
