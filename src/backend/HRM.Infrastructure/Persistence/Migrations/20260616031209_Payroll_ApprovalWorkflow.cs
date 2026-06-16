using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll_ApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "current_approval_step",
                table: "payroll_run",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "current_workflow_instance_id",
                table: "payroll_run",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                table: "payroll_run",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "submitted_at",
                table: "payroll_run",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "submitted_by",
                table: "payroll_run",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "total_approval_steps",
                table: "payroll_run",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payroll_approval_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comments = table.Column<string>(type: "text", nullable: true),
                    acted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_approval_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_payroll_approval_history_payroll_runs_payroll_run_id",
                        column: x => x.payroll_run_id,
                        principalTable: "payroll_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_approval_history_payroll_run_id",
                table: "payroll_approval_history",
                column: "payroll_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_approval_history_tenant_run",
                table: "payroll_approval_history",
                columns: new[] { "tenant_id", "payroll_run_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_approval_history");

            migrationBuilder.DropColumn(
                name: "current_approval_step",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "current_workflow_instance_id",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "submitted_by",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "total_approval_steps",
                table: "payroll_run");
        }
    }
}
