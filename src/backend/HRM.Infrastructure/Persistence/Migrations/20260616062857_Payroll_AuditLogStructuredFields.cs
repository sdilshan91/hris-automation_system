using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll_AuditLogStructuredFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "action",
                table: "audit_logs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_employee_no",
                table: "audit_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "after",
                table: "audit_logs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "before",
                table: "audit_logs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resource_id",
                table: "audit_logs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resource_type",
                table: "audit_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trace_id",
                table: "audit_logs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tenant_id_resource_type_created_at",
                table: "audit_logs",
                columns: new[] { "tenant_id", "resource_type", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_logs_tenant_id_resource_type_created_at",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "action",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "actor_employee_no",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "after",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "before",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "resource_id",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "resource_type",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "trace_id",
                table: "audit_logs");
        }
    }
}
