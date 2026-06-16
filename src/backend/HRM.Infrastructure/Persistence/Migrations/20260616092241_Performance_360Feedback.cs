using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Performance_360Feedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "min360peer_reviewers",
                table: "appraisal_cycle",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "three_sixty_manager_weight_percent",
                table: "appraisal_cycle",
                type: "integer",
                nullable: false,
                defaultValue: 40);

            migrationBuilder.AddColumn<int>(
                name: "three_sixty_peer_weight_percent",
                table: "appraisal_cycle",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "three_sixty_report_weight_percent",
                table: "appraisal_cycle",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<int>(
                name: "three_sixty_self_weight_percent",
                table: "appraisal_cycle",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.CreateTable(
                name: "feedback_360",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewee_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_anonymous = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    overall_comment = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    reviewee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback_360", x => x.id);
                    table.ForeignKey(
                        name: "fk_feedback_360_appraisal_cycle_cycle_id",
                        column: x => x.cycle_id,
                        principalTable: "appraisal_cycle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_feedback_360_employees_reviewee_id",
                        column: x => x.reviewee_id,
                        principalTable: "employees",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_feedback_360_employees_reviewer_id",
                        column: x => x.reviewer_id,
                        principalTable: "employees",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "reviewer_assignment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewee_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    reviewee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reviewer_assignment", x => x.id);
                    table.ForeignKey(
                        name: "fk_reviewer_assignment_appraisal_cycle_cycle_id",
                        column: x => x.cycle_id,
                        principalTable: "appraisal_cycle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reviewer_assignment_employees_reviewee_id",
                        column: x => x.reviewee_id,
                        principalTable: "employees",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_reviewer_assignment_employees_reviewer_id",
                        column: x => x.reviewer_id,
                        principalTable: "employees",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "feedback_360_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feedback360id = table.Column<Guid>(type: "uuid", nullable: false),
                    goal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    competency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback_360_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_feedback_360_item_feedback_360_feedback360id",
                        column: x => x.feedback360id,
                        principalTable: "feedback_360",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_feedback_360_item_goals_goal_id",
                        column: x => x.goal_id,
                        principalTable: "goal",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_feedback_360_cycle_id",
                table: "feedback_360",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_360_reviewee_id",
                table: "feedback_360",
                column: "reviewee_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_360_reviewer_id",
                table: "feedback_360",
                column: "reviewer_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_360_tenant_id_cycle_id_reviewee_employee_id",
                table: "feedback_360",
                columns: new[] { "tenant_id", "cycle_id", "reviewee_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_feedback_360_tenant_id_cycle_id_reviewee_employee_id_review",
                table: "feedback_360",
                columns: new[] { "tenant_id", "cycle_id", "reviewee_employee_id", "reviewer_employee_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_360_item_feedback360id",
                table: "feedback_360_item",
                column: "feedback360id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_360_item_goal_id",
                table: "feedback_360_item",
                column: "goal_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_360_item_tenant_id_feedback360id",
                table: "feedback_360_item",
                columns: new[] { "tenant_id", "feedback360id" });

            migrationBuilder.CreateIndex(
                name: "ix_reviewer_assignment_cycle_id",
                table: "reviewer_assignment",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviewer_assignment_reviewee_id",
                table: "reviewer_assignment",
                column: "reviewee_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviewer_assignment_reviewer_id",
                table: "reviewer_assignment",
                column: "reviewer_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviewer_assignment_tenant_id_cycle_id_reviewee_employee_id",
                table: "reviewer_assignment",
                columns: new[] { "tenant_id", "cycle_id", "reviewee_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_reviewer_assignment_tenant_id_cycle_id_reviewee_employee_id1",
                table: "reviewer_assignment",
                columns: new[] { "tenant_id", "cycle_id", "reviewee_employee_id", "reviewer_employee_id", "category" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feedback_360_item");

            migrationBuilder.DropTable(
                name: "reviewer_assignment");

            migrationBuilder.DropTable(
                name: "feedback_360");

            migrationBuilder.DropColumn(
                name: "min360peer_reviewers",
                table: "appraisal_cycle");

            migrationBuilder.DropColumn(
                name: "three_sixty_manager_weight_percent",
                table: "appraisal_cycle");

            migrationBuilder.DropColumn(
                name: "three_sixty_peer_weight_percent",
                table: "appraisal_cycle");

            migrationBuilder.DropColumn(
                name: "three_sixty_report_weight_percent",
                table: "appraisal_cycle");

            migrationBuilder.DropColumn(
                name: "three_sixty_self_weight_percent",
                table: "appraisal_cycle");
        }
    }
}
