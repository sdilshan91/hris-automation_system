using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// US-CHR-001: Add Employee entity with all FKs, and wire deferred FKs:
    /// - Department.manager_id FK to employees.id (ON DELETE SET NULL)
    /// - Tenant.max_employees nullable column
    /// </summary>
    public partial class AddEmployeeEntity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add max_employees column to tenants table (FR-5)
            migrationBuilder.AddColumn<int>(
                name: "max_employees",
                table: "tenants",
                type: "integer",
                nullable: true);

            // Create employees table
            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    date_of_birth = table.Column<DateTime>(type: "date", nullable: true),
                    gender = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    date_of_joining = table.Column<DateTime>(type: "date", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_title_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employment_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Active"),
                    profile_photo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    custom_fields = table.Column<string>(type: "jsonb", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                    table.ForeignKey(
                        name: "fk_employees_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employees_job_titles_job_title_id",
                        column: x => x.job_title_id,
                        principalTable: "job_titles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employees_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Unique indexes
            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_employee_no",
                table: "employees",
                columns: new[] { "tenant_id", "employee_no" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_email",
                table: "employees",
                columns: new[] { "tenant_id", "email" },
                unique: true,
                filter: "is_deleted = false");

            // Query performance indexes
            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_department_id",
                table: "employees",
                columns: new[] { "tenant_id", "department_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_job_title_id",
                table: "employees",
                columns: new[] { "tenant_id", "job_title_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_status",
                table: "employees",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_user_id",
                table: "employees",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_department_id",
                table: "employees",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_job_title_id",
                table: "employees",
                column: "job_title_id");

            // Wire deferred FK: Department.manager_id -> employees.id (US-CHR-001)
            migrationBuilder.CreateIndex(
                name: "ix_departments_manager_id",
                table: "departments",
                column: "manager_id");

            migrationBuilder.AddForeignKey(
                name: "fk_departments_employees_manager_id",
                table: "departments",
                column: "manager_id",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove deferred FK from departments
            migrationBuilder.DropForeignKey(
                name: "fk_departments_employees_manager_id",
                table: "departments");

            migrationBuilder.DropIndex(
                name: "ix_departments_manager_id",
                table: "departments");

            // Drop employees table
            migrationBuilder.DropTable(name: "employees");

            // Remove max_employees from tenants
            migrationBuilder.DropColumn(
                name: "max_employees",
                table: "tenants");
        }
    }
}
