using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll_PayslipPdfFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "pdf_file_size_bytes",
                table: "payroll_slip",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "pdf_generated_at",
                table: "payroll_slip",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pdf_status",
                table: "payroll_slip",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pdf_storage_path",
                table: "payroll_slip",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pdf_file_size_bytes",
                table: "payroll_slip");

            migrationBuilder.DropColumn(
                name: "pdf_generated_at",
                table: "payroll_slip");

            migrationBuilder.DropColumn(
                name: "pdf_status",
                table: "payroll_slip");

            migrationBuilder.DropColumn(
                name: "pdf_storage_path",
                table: "payroll_slip");
        }
    }
}
