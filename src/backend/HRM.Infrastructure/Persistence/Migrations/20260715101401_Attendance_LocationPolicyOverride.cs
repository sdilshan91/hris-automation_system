using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Attendance_LocationPolicyOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_attendance_settings_tenant_unique",
                table: "attendance_settings");

            migrationBuilder.AddColumn<Guid>(
                name: "location_id",
                table: "attendance_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_attendance_settings_location_id",
                table: "attendance_settings",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_settings_tenant_default_unique",
                table: "attendance_settings",
                column: "tenant_id",
                unique: true,
                filter: "is_deleted = false AND location_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_settings_tenant_location_unique",
                table: "attendance_settings",
                columns: new[] { "tenant_id", "location_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_settings_locations_location_id",
                table: "attendance_settings",
                column: "location_id",
                principalTable: "locations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_attendance_settings_locations_location_id",
                table: "attendance_settings");

            migrationBuilder.DropIndex(
                name: "ix_attendance_settings_location_id",
                table: "attendance_settings");

            migrationBuilder.DropIndex(
                name: "ix_attendance_settings_tenant_default_unique",
                table: "attendance_settings");

            migrationBuilder.DropIndex(
                name: "ix_attendance_settings_tenant_location_unique",
                table: "attendance_settings");

            migrationBuilder.DropColumn(
                name: "location_id",
                table: "attendance_settings");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_settings_tenant_unique",
                table: "attendance_settings",
                column: "tenant_id",
                unique: true,
                filter: "is_deleted = false");
        }
    }
}
