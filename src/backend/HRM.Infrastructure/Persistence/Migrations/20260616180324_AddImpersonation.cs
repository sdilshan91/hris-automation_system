using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImpersonation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "impersonation_session_id",
                table: "audit_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "impersonator_user_id",
                table: "audit_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_impersonation_action",
                table: "audit_logs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "impersonation_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    impersonator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_read_only = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    actions_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_impersonation_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_impersonation_session_id",
                table: "audit_logs",
                column: "impersonation_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_impersonation_sessions_impersonator_user_id",
                table: "impersonation_sessions",
                column: "impersonator_user_id",
                unique: true,
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_impersonation_sessions_target_tenant_id_started_at",
                table: "impersonation_sessions",
                columns: new[] { "target_tenant_id", "started_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "impersonation_sessions");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_impersonation_session_id",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "impersonation_session_id",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "impersonator_user_id",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "is_impersonation_action",
                table: "audit_logs");
        }
    }
}
