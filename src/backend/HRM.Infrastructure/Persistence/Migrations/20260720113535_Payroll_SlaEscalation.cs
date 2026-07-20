using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll_SlaEscalation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "escalated_at",
                table: "payroll_run",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "sla_due_at",
                table: "payroll_run",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "backup_role_id",
                table: "payroll_approval_step_config",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sla_hours",
                table: "payroll_approval_step_config",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "escalated_at",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "sla_due_at",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "backup_role_id",
                table: "payroll_approval_step_config");

            migrationBuilder.DropColumn(
                name: "sla_hours",
                table: "payroll_approval_step_config");
        }
    }
}
