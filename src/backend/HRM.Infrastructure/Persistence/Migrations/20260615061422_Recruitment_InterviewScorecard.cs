using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Recruitment_InterviewScorecard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "interview_scorecard",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    interview_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interviewer_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    overall_recommendation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    average_score = table.Column<decimal>(type: "numeric(4,2)", nullable: false),
                    general_notes = table.Column<string>(type: "text", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    locked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interview_scorecard", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scorecard_criterion_rating",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scorecard_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criterion_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scorecard_criterion_rating", x => x.id);
                    table.ForeignKey(
                        name: "fk_scorecard_criterion_rating_interview_scorecard_scorecard_id",
                        column: x => x.scorecard_id,
                        principalTable: "interview_scorecard",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_interview_scorecard_tenant_interview",
                table: "interview_scorecard",
                columns: new[] { "tenant_id", "interview_id" });

            migrationBuilder.CreateIndex(
                name: "ux_interview_scorecard_tenant_interview_interviewer",
                table: "interview_scorecard",
                columns: new[] { "tenant_id", "interview_id", "interviewer_employee_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_scorecard_criterion_rating_scorecard_id",
                table: "scorecard_criterion_rating",
                column: "scorecard_id");

            migrationBuilder.CreateIndex(
                name: "ix_scorecard_criterion_rating_tenant_scorecard",
                table: "scorecard_criterion_rating",
                columns: new[] { "tenant_id", "scorecard_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scorecard_criterion_rating");

            migrationBuilder.DropTable(
                name: "interview_scorecard");
        }
    }
}
