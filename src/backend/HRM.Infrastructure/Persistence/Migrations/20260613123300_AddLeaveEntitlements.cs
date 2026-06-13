using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leave_entitlement_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_year = table.Column<int>(type: "integer", nullable: false),
                    entitlement_days = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_entitlement_overrides", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_entitlement_overrides_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_leave_entitlement_overrides_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_entitlement_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_title_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_level_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employment_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    tenure_min_months = table.Column<int>(type: "integer", nullable: true),
                    tenure_max_months = table.Column<int>(type: "integer", nullable: true),
                    entitlement_days = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_entitlement_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_entitlement_rules_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_leave_entitlement_rules_job_titles_job_title_id",
                        column: x => x.job_title_id,
                        principalTable: "job_titles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_leave_entitlement_rules_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_year = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_ledger", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_ledger_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_leave_ledger_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leave_entitlement_overrides_employee_id",
                table: "leave_entitlement_overrides",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_entitlement_overrides_leave_type_id",
                table: "leave_entitlement_overrides",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_entitlement_overrides_unique",
                table: "leave_entitlement_overrides",
                columns: new[] { "tenant_id", "employee_id", "leave_type_id", "leave_year" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_leave_entitlement_rules_department_id",
                table: "leave_entitlement_rules",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_entitlement_rules_job_title_id",
                table: "leave_entitlement_rules",
                column: "job_title_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_entitlement_rules_leave_type_id",
                table: "leave_entitlement_rules",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_entitlement_rules_tenant_active_priority",
                table: "leave_entitlement_rules",
                columns: new[] { "tenant_id", "is_active", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_entitlement_rules_tenant_id_leave_type_id",
                table: "leave_entitlement_rules",
                columns: new[] { "tenant_id", "leave_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_ledger_employee_id",
                table: "leave_ledger",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_ledger_leave_type_id",
                table: "leave_ledger",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_ledger_tenant_emp_type_year",
                table: "leave_ledger",
                columns: new[] { "tenant_id", "employee_id", "leave_type_id", "leave_year" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_ledger_tenant_occurred_at",
                table: "leave_ledger",
                columns: new[] { "tenant_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leave_entitlement_overrides");

            migrationBuilder.DropTable(
                name: "leave_entitlement_rules");

            migrationBuilder.DropTable(
                name: "leave_ledger");
        }
    }
}
