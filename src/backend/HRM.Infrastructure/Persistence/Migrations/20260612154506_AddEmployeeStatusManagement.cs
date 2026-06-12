using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeStatusManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "future_dated_status_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    effective_date = table.Column<DateTime>(type: "date", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    is_applied = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    applied_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_cancelled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_future_dated_status_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_future_dated_status_changes_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    operation_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    response_json = table.Column<string>(type: "jsonb", nullable: true),
                    response_status_code = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_records", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_future_dated_status_changes_employee",
                table: "future_dated_status_changes",
                columns: new[] { "tenant_id", "employee_id", "is_applied", "is_cancelled" });

            migrationBuilder.CreateIndex(
                name: "ix_future_dated_status_changes_employee_id",
                table: "future_dated_status_changes",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_future_dated_status_changes_pending",
                table: "future_dated_status_changes",
                columns: new[] { "tenant_id", "effective_date", "is_applied", "is_cancelled" });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_expires_at",
                table: "idempotency_records",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_tenant_key_operation",
                table: "idempotency_records",
                columns: new[] { "tenant_id", "idempotency_key", "operation_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "future_dated_status_changes");

            migrationBuilder.DropTable(
                name: "idempotency_records");
        }
    }
}
