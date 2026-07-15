using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Attendance_LocationDefaultShift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "default_shift_id",
                table: "locations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_locations_default_shift_id",
                table: "locations",
                column: "default_shift_id");

            migrationBuilder.AddForeignKey(
                name: "fk_locations_shifts_default_shift_id",
                table: "locations",
                column: "default_shift_id",
                principalTable: "shift",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_locations_shifts_default_shift_id",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "ix_locations_default_shift_id",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "default_shift_id",
                table: "locations");
        }
    }
}
