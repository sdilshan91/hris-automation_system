using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveTypeEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leave_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    annual_entitlement = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    accrual_frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    carry_forward_limit = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    carry_forward_expiry_months = table.Column<int>(type: "integer", nullable: true),
                    probation_eligible = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    documents_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    document_day_threshold = table.Column<int>(type: "integer", nullable: true),
                    encashable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    max_encash_days = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    half_day_allowed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    hourly_allowed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    max_consecutive_days = table.Column<int>(type: "integer", nullable: true),
                    negative_balance_allowed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    negative_balance_limit = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_types", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leave_types_tenant_id_is_active_display_order",
                table: "leave_types",
                columns: new[] { "tenant_id", "is_active", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_types_tenant_id_name",
                table: "leave_types",
                columns: new[] { "tenant_id", "name" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leave_types");
        }
    }
}
