using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Recruitment_Vacancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "public_careers_enabled",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "vacancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_title_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hiring_manager_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employment_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    headcount = table.Column<int>(type: "integer", nullable: false),
                    filled_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    salary_min = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    salary_max = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    salary_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    qualifications = table.Column<string>(type: "text", nullable: true),
                    application_deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    publish_to_public_careers = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vacancy", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vacancy_tenant_id_department_id",
                table: "vacancy",
                columns: new[] { "tenant_id", "department_id" });

            migrationBuilder.CreateIndex(
                name: "ix_vacancy_tenant_id_reference_number",
                table: "vacancy",
                columns: new[] { "tenant_id", "reference_number" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_vacancy_tenant_id_slug",
                table: "vacancy",
                columns: new[] { "tenant_id", "slug" },
                unique: true,
                filter: "slug IS NOT NULL AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_vacancy_tenant_id_status",
                table: "vacancy",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vacancy");

            migrationBuilder.DropColumn(
                name: "public_careers_enabled",
                table: "tenants");
        }
    }
}
