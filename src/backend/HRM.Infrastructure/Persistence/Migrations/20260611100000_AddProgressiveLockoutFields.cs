using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// US-AUTH-010: Add progressive lockout tracking fields and MFA failed attempt count to users table.
    /// - lockout_count: number of lockout cycles (for progressive lockout doubling)
    /// - last_lockout_at: timestamp of most recent lockout event
    /// - mfa_failed_attempt_count: consecutive failed MFA verification attempts (was missing from prior migrations)
    /// </summary>
    public partial class AddProgressiveLockoutFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "lockout_count",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_lockout_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "mfa_failed_attempt_count",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lockout_count",
                table: "users");

            migrationBuilder.DropColumn(
                name: "last_lockout_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "mfa_failed_attempt_count",
                table: "users");
        }
    }
}
