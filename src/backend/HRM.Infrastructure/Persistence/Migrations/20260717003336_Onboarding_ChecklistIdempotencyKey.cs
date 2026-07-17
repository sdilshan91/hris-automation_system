using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Onboarding_ChecklistIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "onboarding_checklist_instance",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_checklist_instance_tenant_id_employee_id_templat",
                table: "onboarding_checklist_instance",
                columns: new[] { "tenant_id", "employee_id", "template_id", "idempotency_key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_onboarding_checklist_instance_tenant_id_employee_id_templat",
                table: "onboarding_checklist_instance");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "onboarding_checklist_instance");
        }
    }
}
