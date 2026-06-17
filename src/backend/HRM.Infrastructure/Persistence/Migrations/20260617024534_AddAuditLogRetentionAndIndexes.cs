using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogRetentionAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "audit_log_retention_days",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tenant_id_action",
                table: "audit_logs",
                columns: new[] { "tenant_id", "action" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tenant_id_resource_type_resource_id",
                table: "audit_logs",
                columns: new[] { "tenant_id", "resource_type", "resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tenant_id_user_id",
                table: "audit_logs",
                columns: new[] { "tenant_id", "user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_logs_tenant_id_action",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_tenant_id_resource_type_resource_id",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_tenant_id_user_id",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "audit_log_retention_days",
                table: "tenants");
        }
    }
}
