using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Performance_GoalsAndAppraisalCycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "appraisal_cycle",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    goal_setting_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    goal_setting_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appraisal_cycle", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "goal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    weight = table.Column<int>(type: "integer", nullable: false),
                    target_value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    measurement_unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    parent_goal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goal", x => x.id);
                    table.ForeignKey(
                        name: "fk_goal_appraisal_cycle_cycle_id",
                        column: x => x.cycle_id,
                        principalTable: "appraisal_cycle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_goal_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_goal_goal_parent_goal_id",
                        column: x => x.parent_goal_id,
                        principalTable: "goal",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_appraisal_cycle_tenant_id_status",
                table: "appraisal_cycle",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_goal_cycle_id",
                table: "goal",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_goal_employee_id",
                table: "goal",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_goal_parent_goal_id",
                table: "goal",
                column: "parent_goal_id");

            migrationBuilder.CreateIndex(
                name: "ix_goal_tenant_id_cycle_id_employee_id",
                table: "goal",
                columns: new[] { "tenant_id", "cycle_id", "employee_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "goal");

            migrationBuilder.DropTable(
                name: "appraisal_cycle");
        }
    }
}
