using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Performance_360Release : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feedback_360_release",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewee_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    released_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    released_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback_360_release", x => x.id);
                    table.ForeignKey(
                        name: "fk_feedback_360_release_appraisal_cycle_cycle_id",
                        column: x => x.cycle_id,
                        principalTable: "appraisal_cycle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_feedback_360_release_employees_reviewee_employee_id",
                        column: x => x.reviewee_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_feedback_360_release_cycle_id",
                table: "feedback_360_release",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_360_release_reviewee_employee_id",
                table: "feedback_360_release",
                column: "reviewee_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_360_release_tenant_id_cycle_id_reviewee_employee_id",
                table: "feedback_360_release",
                columns: new[] { "tenant_id", "cycle_id", "reviewee_employee_id" },
                unique: true,
                filter: "is_deleted = false");

            // Critical Rule #1 (three-layer tenant isolation): ship the DORMANT tenant_isolation policy with the
            // table. Inert until Rls:Enabled flips it on; a new tenant-scoped table WITHOUT a policy is a silent
            // hole the day RLS is enabled — and this one gates whether an employee may see their own 360 results.
            migrationBuilder.Sql("""
                DO $do$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_policies
                        WHERE schemaname = 'public'
                          AND tablename  = 'feedback_360_release'
                          AND policyname = 'tenant_isolation'
                    ) THEN
                        EXECUTE $q$
                            CREATE POLICY tenant_isolation ON public.feedback_360_release
                            USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                            WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                        $q$;
                    END IF;
                END
                $do$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON public.feedback_360_release;");

            migrationBuilder.DropTable(
                name: "feedback_360_release");
        }
    }
}
