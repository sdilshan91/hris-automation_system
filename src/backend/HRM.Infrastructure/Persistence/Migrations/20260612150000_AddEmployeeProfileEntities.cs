using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// US-CHR-002: View and Edit Employee Profile.
    /// Adds: personal_email, address columns to employees; emergency_contacts table;
    /// employment_histories table; employee_field_audit_logs table.
    /// Note: xmin concurrency token is a PostgreSQL system column and does not
    /// require a migration — EF Core maps it via UseXminAsConcurrencyToken().
    /// </summary>
    public partial class AddEmployeeProfileEntities : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add new columns to employees table
            migrationBuilder.AddColumn<string>(
                name: "personal_email",
                table: "employees",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "employees",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // Create emergency_contacts table
            migrationBuilder.CreateTable(
                name: "emergency_contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    relationship = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    alternate_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_emergency_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_emergency_contacts_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_emergency_contacts_tenant_employee",
                table: "emergency_contacts",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_emergency_contacts_employee_id",
                table: "emergency_contacts",
                column: "employee_id");

            // Create employment_histories table
            migrationBuilder.CreateTable(
                name: "employment_histories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    previous_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    new_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    previous_reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    new_reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    effective_date = table.Column<DateTime>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    changed_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employment_histories", x => x.id);
                    table.ForeignKey(
                        name: "fk_employment_histories_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employment_histories_tenant_employee_effective_date",
                table: "employment_histories",
                columns: new[] { "tenant_id", "employee_id", "effective_date" });

            migrationBuilder.CreateIndex(
                name: "ix_employment_histories_tenant_employee_change_type",
                table: "employment_histories",
                columns: new[] { "tenant_id", "employee_id", "change_type" });

            migrationBuilder.CreateIndex(
                name: "ix_employment_histories_employee_id",
                table: "employment_histories",
                column: "employee_id");

            // Create employee_field_audit_logs table
            migrationBuilder.CreateTable(
                name: "employee_field_audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    section = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    before_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    after_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_field_audit_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_field_audit_logs_tenant_employee_created",
                table: "employee_field_audit_logs",
                columns: new[] { "tenant_id", "employee_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_field_audit_logs_tenant_created",
                table: "employee_field_audit_logs",
                columns: new[] { "tenant_id", "created_at" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "employee_field_audit_logs");
            migrationBuilder.DropTable(name: "employment_histories");
            migrationBuilder.DropTable(name: "emergency_contacts");

            migrationBuilder.DropColumn(name: "personal_email", table: "employees");
            migrationBuilder.DropColumn(name: "address", table: "employees");
        }
    }
}
