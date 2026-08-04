using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Recruitment_ScorecardRevisionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "interview_scorecard",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "interview_scorecard_revision",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scorecard_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    overall_recommendation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    average_score = table.Column<decimal>(type: "numeric(4,2)", nullable: false),
                    general_notes = table.Column<string>(type: "text", nullable: true),
                    ratings_json = table.Column<string>(type: "jsonb", nullable: false),
                    revised_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revised_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interview_scorecard_revision", x => x.id);
                    table.ForeignKey(
                        name: "fk_interview_scorecard_revision_interview_scorecard_scorecard_",
                        column: x => x.scorecard_id,
                        principalTable: "interview_scorecard",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_interview_scorecard_revision_scorecard_id",
                table: "interview_scorecard_revision",
                column: "scorecard_id");

            migrationBuilder.CreateIndex(
                name: "ix_interview_scorecard_revision_tenant_scorecard_version",
                table: "interview_scorecard_revision",
                columns: new[] { "tenant_id", "scorecard_id", "version" });

            // Critical Rule #1 (three-layer tenant isolation): ship the DORMANT tenant_isolation policy with
            // the table, exactly as tenant_latency_bucket and tenant_api_usage do. Inert until Rls:Enabled
            // flips it on; a new tenant-scoped table WITHOUT a policy is a silent hole the day RLS is enabled
            // — and this one holds historical hiring judgements, so it is not a table to leave uncovered.
            migrationBuilder.Sql("""
                DO $do$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_policies
                        WHERE schemaname = 'public'
                          AND tablename  = 'interview_scorecard_revision'
                          AND policyname = 'tenant_isolation'
                    ) THEN
                        EXECUTE $q$
                            CREATE POLICY tenant_isolation ON public.interview_scorecard_revision
                            USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                            WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                        $q$;
                    END IF;
                END
                $do$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON public.interview_scorecard_revision;");

            migrationBuilder.DropTable(
                name: "interview_scorecard_revision");

            migrationBuilder.DropColumn(
                name: "version",
                table: "interview_scorecard");
        }
    }
}
