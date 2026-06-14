using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLopAndCompulsoryLeave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "system_category",
                table: "leave_types",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<bool>(
                name: "is_lop",
                table: "leave_request",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "lop_source",
                table: "leave_request",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "compulsory_leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    assigned_count = table.Column<int>(type: "integer", nullable: false),
                    lop_count = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compulsory_leave", x => x.id);
                    table.ForeignKey(
                        name: "fk_compulsory_leave_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_lop",
                table: "leave_request",
                columns: new[] { "tenant_id", "employee_id", "start_date" },
                filter: "is_lop = true");

            migrationBuilder.CreateIndex(
                name: "ix_compulsory_leave_leave_type_id",
                table: "compulsory_leave",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_compulsory_leave_tenant_date",
                table: "compulsory_leave",
                columns: new[] { "tenant_id", "date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "compulsory_leave");

            migrationBuilder.DropIndex(
                name: "ix_leave_request_lop",
                table: "leave_request");

            migrationBuilder.DropColumn(
                name: "system_category",
                table: "leave_types");

            migrationBuilder.DropColumn(
                name: "is_lop",
                table: "leave_request");

            migrationBuilder.DropColumn(
                name: "lop_source",
                table: "leave_request");
        }
    }
}
