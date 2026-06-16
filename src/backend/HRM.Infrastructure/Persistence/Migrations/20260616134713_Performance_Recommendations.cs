using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Performance_Recommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recommendation_budget",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    allocated_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    consumed_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_recommendation_budget", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recommendation_rule",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    min_final_score = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    recommended_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    default_bonus_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    default_increment_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
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
                    table.PrimaryKey("pk_recommendation_rule", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recommendation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_review_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_auto_generated = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    current_grade = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    target_grade = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    current_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    target_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    current_compensation = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    bonus_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    bonus_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    increment_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    increment_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    training_course = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    custom_type_label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    justification = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    auto_generation_rationale = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    budget_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_recommendation", x => x.id);
                    table.ForeignKey(
                        name: "fk_recommendation_appraisal_cycle_cycle_id",
                        column: x => x.cycle_id,
                        principalTable: "appraisal_cycle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_recommendation_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_recommendation_recommendation_budget_budget_id",
                        column: x => x.budget_id,
                        principalTable: "recommendation_budget",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "recommendation_approver",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recommendation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approver_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_order = table.Column<int>(type: "integer", nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_recommendation_approver", x => x.id);
                    table.ForeignKey(
                        name: "fk_recommendation_approver_recommendations_recommendation_id",
                        column: x => x.recommendation_id,
                        principalTable: "recommendation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recommendation_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recommendation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    client_ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    detail = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recommendation_event", x => x.id);
                    table.ForeignKey(
                        name: "fk_recommendation_event_recommendation_recommendation_id",
                        column: x => x.recommendation_id,
                        principalTable: "recommendation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_budget_id",
                table: "recommendation",
                column: "budget_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_cycle_id",
                table: "recommendation",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_employee_id",
                table: "recommendation",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_tenant_id_cycle_id_employee_id",
                table: "recommendation",
                columns: new[] { "tenant_id", "cycle_id", "employee_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_approver_recommendation_id_step_order",
                table: "recommendation_approver",
                columns: new[] { "recommendation_id", "step_order" });

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_budget_tenant_id_cycle_id",
                table: "recommendation_budget",
                columns: new[] { "tenant_id", "cycle_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_event_recommendation_id_occurred_at",
                table: "recommendation_event",
                columns: new[] { "recommendation_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_rule_tenant_id_is_active",
                table: "recommendation_rule",
                columns: new[] { "tenant_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recommendation_approver");

            migrationBuilder.DropTable(
                name: "recommendation_event");

            migrationBuilder.DropTable(
                name: "recommendation_rule");

            migrationBuilder.DropTable(
                name: "recommendation");

            migrationBuilder.DropTable(
                name: "recommendation_budget");
        }
    }
}
