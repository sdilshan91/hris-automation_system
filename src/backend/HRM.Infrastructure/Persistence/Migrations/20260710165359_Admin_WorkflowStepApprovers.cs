using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Admin_WorkflowStepApprovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_step_approvers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approver_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    approver_identifier = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_step_approvers", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_step_approvers_workflow_steps_workflow_step_id",
                        column: x => x.workflow_step_id,
                        principalTable: "workflow_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_step_approvers_workflow_step_id",
                table: "workflow_step_approvers",
                column: "workflow_step_id");

            // RLS (NEW-TENANT-TABLE RULE): this new tenant-scoped table needs its own DORMANT tenant_isolation
            // policy — the Platform_RlsPolicies_Dormant DO-block only covered tables existing at ITS apply-time,
            // and the RlsIsolation coverage-guard test fails for any tenant_id table without a policy. tenant_id
            // is NOT NULL → strict USING + WITH CHECK (NULLIF → unset/reset GUC = NULL = fail-closed). DORMANT:
            // no ENABLE — the Rls:Enabled-gated reconciler enforces it. Idempotent.
            migrationBuilder.Sql("""
                DO $do$
                DECLARE
                    v_expr text := $q$tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid$q$;
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_policies
                        WHERE schemaname = 'public' AND tablename = 'workflow_step_approvers' AND policyname = 'tenant_isolation'
                    ) THEN
                        EXECUTE format(
                            'CREATE POLICY tenant_isolation ON public.%I USING (%s) WITH CHECK (%s)',
                            'workflow_step_approvers', v_expr, v_expr);
                    END IF;
                END
                $do$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_step_approvers");
        }
    }
}
