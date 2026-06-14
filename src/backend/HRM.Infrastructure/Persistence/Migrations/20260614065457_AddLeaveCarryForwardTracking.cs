using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveCarryForwardTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leave_carry_forward_tracking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_year = table.Column<int>(type: "integer", nullable: false),
                    to_year = table.Column<int>(type: "integer", nullable: false),
                    carried_days = table.Column<decimal>(type: "numeric(7,2)", nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expired_days = table.Column<decimal>(type: "numeric(7,2)", nullable: false, defaultValue: 0m),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_carry_forward_tracking", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_carry_forward_tracking_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_leave_carry_forward_tracking_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leave_carry_forward_tracking_employee_id",
                table: "leave_carry_forward_tracking",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_carry_forward_tracking_leave_type_id",
                table: "leave_carry_forward_tracking",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_carry_forward_tracking_status_expiry",
                table: "leave_carry_forward_tracking",
                columns: new[] { "tenant_id", "status", "expiry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_carry_forward_tracking_unique",
                table: "leave_carry_forward_tracking",
                columns: new[] { "tenant_id", "employee_id", "leave_type_id", "from_year", "to_year" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leave_carry_forward_tracking");
        }
    }
}
