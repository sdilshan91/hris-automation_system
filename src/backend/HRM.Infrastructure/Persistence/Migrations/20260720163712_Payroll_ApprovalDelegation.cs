using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll_ApprovalDelegation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "delegated_to_user_id",
                table: "payroll_run",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "delegate_user_id",
                table: "payroll_approval_step_config",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "primary_approver_user_id",
                table: "payroll_approval_step_config",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "delegated_to_user_id",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "delegate_user_id",
                table: "payroll_approval_step_config");

            migrationBuilder.DropColumn(
                name: "primary_approver_user_id",
                table: "payroll_approval_step_config");
        }
    }
}
