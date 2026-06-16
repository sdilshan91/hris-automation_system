using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Performance_Pip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mentor_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin_manager_review_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    scheduled_checkpoint_dates = table.Column<List<DateOnly>>(type: "date[]", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    escalation_action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    initiated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    acknowledgement_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    acknowledged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    outcome_set_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    final_outcome_notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    escalation_confirmed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    escalation_confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    escalation_notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
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
                    table.PrimaryKey("pk_pip", x => x.id);
                    table.ForeignKey(
                        name: "fk_pip_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pip_checkpoint",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checkpoint_date = table.Column<DateOnly>(type: "date", nullable: false),
                    progress_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    evidence_notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    reviewer_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewer_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    attachment_storage_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    attachment_file_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    attachment_content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    attachment_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pip_checkpoint", x => x.id);
                    table.ForeignKey(
                        name: "fk_pip_checkpoint_pips_pip_id",
                        column: x => x.pip_id,
                        principalTable: "pip",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pip_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    client_ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
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
                    table.PrimaryKey("pk_pip_event", x => x.id);
                    table.ForeignKey(
                        name: "fk_pip_event_pip_pip_id",
                        column: x => x.pip_id,
                        principalTable: "pip",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pip_objective",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    success_criteria = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    added_at_extension = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pip_objective", x => x.id);
                    table.ForeignKey(
                        name: "fk_pip_objective_pip_pip_id",
                        column: x => x.pip_id,
                        principalTable: "pip",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pip_employee_id",
                table: "pip",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_pip_tenant_id_employee_id",
                table: "pip",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true,
                filter: "is_deleted = false AND status IN ('Draft', 'Active', 'Extended')");

            migrationBuilder.CreateIndex(
                name: "ix_pip_checkpoint_pip_id",
                table: "pip_checkpoint",
                column: "pip_id");

            migrationBuilder.CreateIndex(
                name: "ix_pip_checkpoint_tenant_id_pip_id_recorded_at",
                table: "pip_checkpoint",
                columns: new[] { "tenant_id", "pip_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pip_event_pip_id",
                table: "pip_event",
                column: "pip_id");

            migrationBuilder.CreateIndex(
                name: "ix_pip_event_tenant_id_pip_id_occurred_at",
                table: "pip_event",
                columns: new[] { "tenant_id", "pip_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pip_objective_pip_id",
                table: "pip_objective",
                column: "pip_id");

            migrationBuilder.CreateIndex(
                name: "ix_pip_objective_tenant_id_pip_id",
                table: "pip_objective",
                columns: new[] { "tenant_id", "pip_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pip_checkpoint");

            migrationBuilder.DropTable(
                name: "pip_event");

            migrationBuilder.DropTable(
                name: "pip_objective");

            migrationBuilder.DropTable(
                name: "pip");
        }
    }
}
