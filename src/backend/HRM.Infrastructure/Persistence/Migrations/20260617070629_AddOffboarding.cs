using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOffboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "onboarding_checklist_template",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Onboarding");

            migrationBuilder.CreateTable(
                name: "offboarding_instance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    template_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    last_working_day = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    initiated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offboarding_instance", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "offboarding_task_instance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    offboarding_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_template_task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    clearance_category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    responsible_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    responsible_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_mandatory = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    clearance_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offboarding_task_instance", x => x.id);
                    table.ForeignKey(
                        name: "fk_offboarding_task_instance_offboarding_instance_offboarding_",
                        column: x => x.offboarding_instance_id,
                        principalTable: "offboarding_instance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_instance_tenant_id_employee_id",
                table: "offboarding_instance",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_task_instance_offboarding_instance_id",
                table: "offboarding_task_instance",
                column: "offboarding_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_task_instance_tenant_id_offboarding_instance_id",
                table: "offboarding_task_instance",
                columns: new[] { "tenant_id", "offboarding_instance_id", "sort_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offboarding_task_instance");

            migrationBuilder.DropTable(
                name: "offboarding_instance");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "onboarding_checklist_template");
        }
    }
}
