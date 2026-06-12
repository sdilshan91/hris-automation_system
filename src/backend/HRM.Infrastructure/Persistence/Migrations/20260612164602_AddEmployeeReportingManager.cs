using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeReportingManager : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "reports_to_employee_id",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_reports_to_employee_id",
                table: "employees",
                column: "reports_to_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_reports_to_employee_id",
                table: "employees",
                columns: new[] { "tenant_id", "reports_to_employee_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_employees_employees_reports_to_employee_id",
                table: "employees",
                column: "reports_to_employee_id",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_employees_employees_reports_to_employee_id",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "ix_employees_reports_to_employee_id",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "ix_employees_tenant_id_reports_to_employee_id",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "reports_to_employee_id",
                table: "employees");
        }
    }
}
