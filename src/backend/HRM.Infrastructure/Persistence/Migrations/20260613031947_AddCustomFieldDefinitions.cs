using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomFieldDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_custom_fields",
                table: "tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "custom_field_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    field_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    field_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    field_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    options = table.Column<string>(type: "jsonb", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_custom_field_definitions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employees_custom_fields_gin",
                table: "employees",
                column: "custom_fields")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_custom_field_definitions_tenant_entity_key",
                table: "custom_field_definitions",
                columns: new[] { "tenant_id", "entity_type", "field_key" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_custom_field_definitions_tenant_entity_name",
                table: "custom_field_definitions",
                columns: new[] { "tenant_id", "entity_type", "field_name" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_field_definitions");

            migrationBuilder.DropIndex(
                name: "ix_employees_custom_fields_gin",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "max_custom_fields",
                table: "tenants");
        }
    }
}
