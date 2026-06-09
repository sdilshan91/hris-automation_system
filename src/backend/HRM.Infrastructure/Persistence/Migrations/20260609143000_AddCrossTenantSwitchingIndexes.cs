using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260609143000_AddCrossTenantSwitchingIndexes")]
    public partial class AddCrossTenantSwitchingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ix_user_tenant_roles_role_id",
                table: "user_tenant_roles");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id_tenant_id_expires_at",
                table: "refresh_tokens",
                columns: new[] { "user_id", "tenant_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_tenant_roles_role_id_user_tenant_id",
                table: "user_tenant_roles",
                columns: new[] { "role_id", "user_tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_tenants_tenant_id_status",
                table: "user_tenants",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_user_tenants_user_id_status",
                table: "user_tenants",
                columns: new[] { "user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_user_id_tenant_id_expires_at",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ix_user_tenant_roles_role_id_user_tenant_id",
                table: "user_tenant_roles");

            migrationBuilder.DropIndex(
                name: "ix_user_tenants_tenant_id_status",
                table: "user_tenants");

            migrationBuilder.DropIndex(
                name: "ix_user_tenants_user_id_status",
                table: "user_tenants");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_user_tenant_roles_role_id",
                table: "user_tenant_roles",
                column: "role_id");
        }
    }
}
