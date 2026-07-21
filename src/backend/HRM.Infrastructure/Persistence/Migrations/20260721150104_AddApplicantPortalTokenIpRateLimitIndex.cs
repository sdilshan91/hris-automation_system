using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicantPortalTokenIpRateLimitIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_applicant_portal_token_tenant_id_request_ip_created_at",
                table: "applicant_portal_token",
                columns: new[] { "tenant_id", "request_ip", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_applicant_portal_token_tenant_id_request_ip_created_at",
                table: "applicant_portal_token");
        }
    }
}
