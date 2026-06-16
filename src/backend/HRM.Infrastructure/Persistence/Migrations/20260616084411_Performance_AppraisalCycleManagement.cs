using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Performance_AppraisalCycleManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                table: "appraisal_cycle",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "end_date",
                table: "appraisal_cycle",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "is360enabled",
                table: "appraisal_cycle",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_anonymous_feedback",
                table: "appraisal_cycle",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_calibration_enabled",
                table: "appraisal_cycle",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "participant_scope",
                table: "appraisal_cycle",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "AllEmployees");

            migrationBuilder.AddColumn<DateTime>(
                name: "start_date",
                table: "appraisal_cycle",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "type",
                table: "appraisal_cycle",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Annual");

            migrationBuilder.CreateTable(
                name: "cycle_participant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cycle_participant", x => x.id);
                    table.ForeignKey(
                        name: "fk_cycle_participant_appraisal_cycle_cycle_id",
                        column: x => x.cycle_id,
                        principalTable: "appraisal_cycle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cycle_phase",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phase_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cycle_phase", x => x.id);
                    table.ForeignKey(
                        name: "fk_cycle_phase_appraisal_cycle_cycle_id",
                        column: x => x.cycle_id,
                        principalTable: "appraisal_cycle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appraisal_cycle_tenant_id_type_status",
                table: "appraisal_cycle",
                columns: new[] { "tenant_id", "type", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_cycle_participant_cycle_id",
                table: "cycle_participant",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_participant_tenant_id_cycle_id_employee_id",
                table: "cycle_participant",
                columns: new[] { "tenant_id", "cycle_id", "employee_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_participant_tenant_id_employee_id",
                table: "cycle_participant",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cycle_phase_cycle_id",
                table: "cycle_phase",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_phase_tenant_id_cycle_id",
                table: "cycle_phase",
                columns: new[] { "tenant_id", "cycle_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cycle_phase_tenant_id_cycle_id_phase_type",
                table: "cycle_phase",
                columns: new[] { "tenant_id", "cycle_id", "phase_type" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cycle_participant");

            migrationBuilder.DropTable(
                name: "cycle_phase");

            migrationBuilder.DropIndex(
                name: "ix_appraisal_cycle_tenant_id_type_status",
                table: "appraisal_cycle");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                table: "appraisal_cycle");

            migrationBuilder.DropColumn(
                name: "end_date",
                table: "appraisal_cycle");

            migrationBuilder.DropColumn(
                name: "is360enabled",
                table: "appraisal_cycle");

            migrationBuilder.DropColumn(
                name: "is_anonymous_feedback",
                table: "appraisal_cycle");

            migrationBuilder.DropColumn(
                name: "is_calibration_enabled",
                table: "appraisal_cycle");

            migrationBuilder.DropColumn(
                name: "participant_scope",
                table: "appraisal_cycle");

            migrationBuilder.DropColumn(
                name: "start_date",
                table: "appraisal_cycle");

            migrationBuilder.DropColumn(
                name: "type",
                table: "appraisal_cycle");
        }
    }
}
