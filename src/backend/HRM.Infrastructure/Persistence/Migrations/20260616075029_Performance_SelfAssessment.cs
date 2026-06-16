using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Performance_SelfAssessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "rating_scale_max",
                table: "appraisal_cycle",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<DateTime>(
                name: "self_assessment_end",
                table: "appraisal_cycle",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "self_assessment_start",
                table: "appraisal_cycle",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "self_weight_percent",
                table: "appraisal_cycle",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.CreateTable(
                name: "self_assessment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    weighted_self_score = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
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
                    table.PrimaryKey("pk_self_assessment", x => x.id);
                    table.ForeignKey(
                        name: "fk_self_assessment_appraisal_cycle_cycle_id",
                        column: x => x.cycle_id,
                        principalTable: "appraisal_cycle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_self_assessment_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "self_assessment_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    self_assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    goal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    self_rating = table.Column<int>(type: "integer", nullable: true),
                    achievement_percentage = table.Column<int>(type: "integer", nullable: true),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_self_assessment_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_self_assessment_item_goal_goal_id",
                        column: x => x.goal_id,
                        principalTable: "goal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_self_assessment_item_self_assessment_self_assessment_id",
                        column: x => x.self_assessment_id,
                        principalTable: "self_assessment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "self_assessment_attachment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    self_assessment_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    content_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_scanned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_self_assessment_attachment", x => x.id);
                    table.ForeignKey(
                        name: "fk_self_assessment_attachment_self_assessment_items_self_asses",
                        column: x => x.self_assessment_item_id,
                        principalTable: "self_assessment_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_self_assessment_cycle_id",
                table: "self_assessment",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_self_assessment_employee_id",
                table: "self_assessment",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_self_assessment_tenant_id_cycle_id_employee_id",
                table: "self_assessment",
                columns: new[] { "tenant_id", "cycle_id", "employee_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_self_assessment_attachment_self_assessment_item_id",
                table: "self_assessment_attachment",
                column: "self_assessment_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_self_assessment_attachment_tenant_id_self_assessment_item_id",
                table: "self_assessment_attachment",
                columns: new[] { "tenant_id", "self_assessment_item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_self_assessment_item_goal_id",
                table: "self_assessment_item",
                column: "goal_id");

            migrationBuilder.CreateIndex(
                name: "ix_self_assessment_item_self_assessment_id",
                table: "self_assessment_item",
                column: "self_assessment_id");

            migrationBuilder.CreateIndex(
                name: "ix_self_assessment_item_tenant_id_self_assessment_id_goal_id",
                table: "self_assessment_item",
                columns: new[] { "tenant_id", "self_assessment_id", "goal_id" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "self_assessment_attachment");

            migrationBuilder.DropTable(
                name: "self_assessment_item");

            migrationBuilder.DropTable(
                name: "self_assessment");

            migrationBuilder.DropColumn(
                name: "rating_scale_max",
                table: "appraisal_cycle");

            migrationBuilder.DropColumn(
                name: "self_assessment_end",
                table: "appraisal_cycle");

            migrationBuilder.DropColumn(
                name: "self_assessment_start",
                table: "appraisal_cycle");

            migrationBuilder.DropColumn(
                name: "self_weight_percent",
                table: "appraisal_cycle");
        }
    }
}
