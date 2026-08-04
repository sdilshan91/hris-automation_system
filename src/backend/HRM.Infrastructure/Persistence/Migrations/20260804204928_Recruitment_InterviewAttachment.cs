using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Recruitment_InterviewAttachment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "interview_attachment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    interview_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    mime_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interview_attachment", x => x.id);
                    table.ForeignKey(
                        name: "fk_interview_attachment_interviews_interview_id",
                        column: x => x.interview_id,
                        principalTable: "interview",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_interview_attachment_interview_id",
                table: "interview_attachment",
                column: "interview_id");

            migrationBuilder.CreateIndex(
                name: "ix_interview_attachment_tenant_interview",
                table: "interview_attachment",
                columns: new[] { "tenant_id", "interview_id" });

            // Critical Rule #1 (three-layer tenant isolation): ship the DORMANT tenant_isolation policy with
            // the table. Inert until Rls:Enabled flips it on; a new tenant-scoped table WITHOUT a policy is a
            // silent hole the day RLS is enabled — and this one holds recruiter-uploaded documents.
            migrationBuilder.Sql("""
                DO $do$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_policies
                        WHERE schemaname = 'public'
                          AND tablename  = 'interview_attachment'
                          AND policyname = 'tenant_isolation'
                    ) THEN
                        EXECUTE $q$
                            CREATE POLICY tenant_isolation ON public.interview_attachment
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
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON public.interview_attachment;");

            migrationBuilder.DropTable(
                name: "interview_attachment");
        }
    }
}
