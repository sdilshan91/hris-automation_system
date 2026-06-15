using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Recruitment_ApplicantConversion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "converted_at",
                table: "applicant",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "converted_by_user_id",
                table: "applicant",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "converted_to_employee_id",
                table: "applicant",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_applicant_tenant_id_converted_to_employee_id",
                table: "applicant",
                columns: new[] { "tenant_id", "converted_to_employee_id" },
                unique: true,
                filter: "converted_to_employee_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_applicant_tenant_id_converted_to_employee_id",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "converted_at",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "converted_by_user_id",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "converted_to_employee_id",
                table: "applicant");
        }
    }
}
