using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Attendance_RegularizationApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attendance_regularization_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    regularization_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_attendance_regularization_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_attendance_regularization_history_attendance_regularization",
                        column: x => x.regularization_id,
                        principalTable: "attendance_regularization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attendance_regularization_history_employees_approver_employ",
                        column: x => x.approver_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_regularization_history_approver_employee_id",
                table: "attendance_regularization_history",
                column: "approver_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_regularization_history_regularization_id",
                table: "attendance_regularization_history",
                column: "regularization_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_regularization_history_tenant_reg",
                table: "attendance_regularization_history",
                columns: new[] { "tenant_id", "regularization_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_regularization_history");
        }
    }
}
