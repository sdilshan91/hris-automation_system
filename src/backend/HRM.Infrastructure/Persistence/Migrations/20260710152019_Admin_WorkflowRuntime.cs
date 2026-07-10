using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Admin_WorkflowRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "workflow_instance_id",
                table: "leave_request",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "workflow_instances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lineage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "InProgress"),
                    current_step_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    route_step_orders = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false, defaultValue: ""),
                    requester_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requester_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_instances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_step_instances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_order = table.Column<int>(type: "integer", nullable: false),
                    is_parallel = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    approver_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    approver_identifier = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_approver_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    comments = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sla_due_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    escalated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_step_instances", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_step_instances_workflow_instances_workflow_instanc",
                        column: x => x.workflow_instance_id,
                        principalTable: "workflow_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_tenant_entity",
                table: "workflow_instances",
                columns: new[] { "tenant_id", "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_tenant_lineage_status",
                table: "workflow_instances",
                columns: new[] { "tenant_id", "lineage_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_step_instances_instance_decision",
                table: "workflow_step_instances",
                columns: new[] { "workflow_instance_id", "decision" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_step_instances_instance_id",
                table: "workflow_step_instances",
                column: "workflow_instance_id");

            // RLS: the two new tenant-scoped tables need their DORMANT tenant_isolation policy (the
            // Platform_RlsPolicies_Dormant DO-block only covered tables existing at ITS apply-time; a new
            // tenant table added by a later migration must carry its own policy or the RLS coverage-guard
            // test fails). Both have a NOT-NULL tenant_id → strict USING + WITH CHECK, matching the 2a form
            // (NULLIF → unset/reset GUC = NULL = fail-closed). DORMANT: no ENABLE — the Rls:Enabled-gated
            // reconciler enforces it. Idempotent.
            migrationBuilder.Sql("""
                DO $do$
                DECLARE
                    v_expr text := $q$tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid$q$;
                    t text;
                BEGIN
                    FOREACH t IN ARRAY ARRAY['workflow_instances', 'workflow_step_instances'] LOOP
                        IF NOT EXISTS (
                            SELECT 1 FROM pg_policies
                            WHERE schemaname = 'public' AND tablename = t AND policyname = 'tenant_isolation'
                        ) THEN
                            EXECUTE format(
                                'CREATE POLICY tenant_isolation ON public.%I USING (%s) WITH CHECK (%s)',
                                t, v_expr, v_expr);
                        END IF;
                    END LOOP;
                END
                $do$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_step_instances");

            migrationBuilder.DropTable(
                name: "workflow_instances");

            migrationBuilder.DropColumn(
                name: "workflow_instance_id",
                table: "leave_request");
        }
    }
}
