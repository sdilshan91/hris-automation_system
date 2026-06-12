using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations;

/// <summary>
/// US-CHR-003: Add Location column to employees table and indexes for directory search/filter.
/// </summary>
public partial class AddEmployeeDirectoryFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Add location column
        migrationBuilder.AddColumn<string>(
            name: "location",
            table: "employees",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        // Index for employment_type filter
        migrationBuilder.CreateIndex(
            name: "ix_employees_tenant_id_employment_type",
            table: "employees",
            columns: new[] { "tenant_id", "employment_type" });

        // Index for date_of_joining range filter / sort
        migrationBuilder.CreateIndex(
            name: "ix_employees_tenant_id_date_of_joining",
            table: "employees",
            columns: new[] { "tenant_id", "date_of_joining" });

        // Index for location filter
        migrationBuilder.CreateIndex(
            name: "ix_employees_tenant_id_location",
            table: "employees",
            columns: new[] { "tenant_id", "location" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_employees_tenant_id_location",
            table: "employees");

        migrationBuilder.DropIndex(
            name: "ix_employees_tenant_id_date_of_joining",
            table: "employees");

        migrationBuilder.DropIndex(
            name: "ix_employees_tenant_id_employment_type",
            table: "employees");

        migrationBuilder.DropColumn(
            name: "location",
            table: "employees");
    }
}
