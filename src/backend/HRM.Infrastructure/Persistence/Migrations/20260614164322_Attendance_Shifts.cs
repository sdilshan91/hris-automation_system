using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Attendance_Shifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shift",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time", nullable: true),
                    end_time = table.Column<TimeOnly>(type: "time", nullable: true),
                    break_duration_minutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    grace_period_minutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    minimum_hours = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    working_days = table.Column<List<int>>(type: "integer[]", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    rotation_cycle_length_days = table.Column<int>(type: "integer", nullable: true),
                    rotation_reference_start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_shift",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_shift", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_shift_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_employee_shift_shifts_shift_id",
                        column: x => x.shift_id,
                        principalTable: "shift",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shift_rotation_step",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    step_shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    duration_days = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_rotation_step", x => x.id);
                    table.ForeignKey(
                        name: "fk_shift_rotation_step_shift_shift_id",
                        column: x => x.shift_id,
                        principalTable: "shift",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_shift_employee_effective",
                table: "employee_shift",
                columns: new[] { "employee_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_shift_shift_id",
                table: "employee_shift",
                column: "shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_tenant_default_unique",
                table: "shift",
                column: "tenant_id",
                unique: true,
                filter: "is_default = true AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_shift_tenant_name_unique",
                table: "shift",
                columns: new[] { "tenant_id", "name" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_shift_rotation_step_shift_order",
                table: "shift_rotation_step",
                columns: new[] { "shift_id", "order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_shift");

            migrationBuilder.DropTable(
                name: "shift_rotation_step");

            migrationBuilder.DropTable(
                name: "shift");
        }
    }
}
