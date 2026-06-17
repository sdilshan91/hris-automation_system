using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingAssetRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asset",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    asset_tag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    purchase_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    assigned_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issue_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    acknowledgment_doc_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    acknowledgment_doc_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_employees_assigned_employee_id",
                        column: x => x.assigned_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_assigned_employee_id",
                table: "asset",
                column: "assigned_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_tenant_id_asset_tag",
                table: "asset",
                columns: new[] { "tenant_id", "asset_tag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_tenant_id_assigned_employee_id",
                table: "asset",
                columns: new[] { "tenant_id", "assigned_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_asset_tenant_id_status_asset_type",
                table: "asset",
                columns: new[] { "tenant_id", "status", "asset_type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset");
        }
    }
}
