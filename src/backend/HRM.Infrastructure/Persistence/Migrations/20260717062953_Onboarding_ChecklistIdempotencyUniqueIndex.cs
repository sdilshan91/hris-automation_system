using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Onboarding_ChecklistIdempotencyUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_onboarding_checklist_instance_tenant_id_employee_id_templat",
                table: "onboarding_checklist_instance");

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_checklist_instance_tenant_id_employee_id_templat",
                table: "onboarding_checklist_instance",
                columns: new[] { "tenant_id", "employee_id", "template_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL AND status = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_onboarding_checklist_instance_tenant_id_employee_id_templat",
                table: "onboarding_checklist_instance");

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_checklist_instance_tenant_id_employee_id_templat",
                table: "onboarding_checklist_instance",
                columns: new[] { "tenant_id", "employee_id", "template_id", "idempotency_key" });
        }
    }
}
