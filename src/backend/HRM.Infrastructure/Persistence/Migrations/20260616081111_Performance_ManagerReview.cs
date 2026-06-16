using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Performance_ManagerReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "manager_review_end",
                table: "appraisal_cycle",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "manager_review_start",
                table: "appraisal_cycle",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "manager_review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    weighted_manager_score = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    final_score = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    self_score_at_submit = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    summary_comment = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    flag = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_manager_review", x => x.id);
                    table.ForeignKey(
                        name: "fk_manager_review_appraisal_cycle_cycle_id",
                        column: x => x.cycle_id,
                        principalTable: "appraisal_cycle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_manager_review_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "manager_review_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    goal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_rating = table.Column<int>(type: "integer", nullable: true),
                    manager_comment = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manager_review_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_manager_review_item_goal_goal_id",
                        column: x => x.goal_id,
                        principalTable: "goal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_manager_review_item_manager_review_manager_review_id",
                        column: x => x.manager_review_id,
                        principalTable: "manager_review",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_manager_review_cycle_id",
                table: "manager_review",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_manager_review_employee_id",
                table: "manager_review",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_manager_review_tenant_id_cycle_id_employee_id",
                table: "manager_review",
                columns: new[] { "tenant_id", "cycle_id", "employee_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_manager_review_item_goal_id",
                table: "manager_review_item",
                column: "goal_id");

            migrationBuilder.CreateIndex(
                name: "ix_manager_review_item_manager_review_id",
                table: "manager_review_item",
                column: "manager_review_id");

            migrationBuilder.CreateIndex(
                name: "ix_manager_review_item_tenant_id_manager_review_id_goal_id",
                table: "manager_review_item",
                columns: new[] { "tenant_id", "manager_review_id", "goal_id" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "manager_review_item");

            migrationBuilder.DropTable(
                name: "manager_review");

            migrationBuilder.DropColumn(
                name: "manager_review_end",
                table: "appraisal_cycle");

            migrationBuilder.DropColumn(
                name: "manager_review_start",
                table: "appraisal_cycle");
        }
    }
}
