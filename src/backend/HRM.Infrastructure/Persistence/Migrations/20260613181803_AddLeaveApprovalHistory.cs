using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveApprovalHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: xmin is a PostgreSQL system column that already exists on every table.
            // It is mapped as the LeaveRequest optimistic-concurrency token in the model
            // (see the Designer snapshot), but must NOT be emitted as an AddColumn here —
            // ALTER TABLE ... ADD COLUMN xmin conflicts with the system column. (US-LV-005)
            migrationBuilder.AddColumn<Guid>(
                name: "leave_request_id",
                table: "leave_ledger",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "leave_approval_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approver_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approval_level = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    actioned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_approval_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_approval_history_employees_approver_employee_id",
                        column: x => x.approver_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leave_approval_history_leave_requests_leave_request_id",
                        column: x => x.leave_request_id,
                        principalTable: "leave_request",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leave_ledger_leave_request_id",
                table: "leave_ledger",
                column: "leave_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_approval_history_approver_employee_id",
                table: "leave_approval_history",
                column: "approver_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_approval_history_leave_request_id",
                table: "leave_approval_history",
                column: "leave_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_approval_history_tenant_request",
                table: "leave_approval_history",
                columns: new[] { "tenant_id", "leave_request_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_leave_ledger_leave_requests_leave_request_id",
                table: "leave_ledger",
                column: "leave_request_id",
                principalTable: "leave_request",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_leave_ledger_leave_requests_leave_request_id",
                table: "leave_ledger");

            migrationBuilder.DropTable(
                name: "leave_approval_history");

            migrationBuilder.DropIndex(
                name: "ix_leave_ledger_leave_request_id",
                table: "leave_ledger");

            migrationBuilder.DropColumn(
                name: "leave_request_id",
                table: "leave_ledger");
        }
    }
}
