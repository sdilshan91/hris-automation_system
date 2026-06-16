using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Performance_ReviewSignoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_locked",
                table: "manager_review",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "signoff_completed_at",
                table: "manager_review",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "signoff_requested_at",
                table: "manager_review",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signoff_status",
                table: "manager_review",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "meeting_notes_template",
                table: "appraisal_cycle",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "signoff_auto_close_days",
                table: "appraisal_cycle",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.CreateTable(
                name: "review_meeting_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "text", nullable: true),
                    strengths = table.Column<string>(type: "text", nullable: true),
                    development_areas = table.Column<string>(type: "text", nullable: true),
                    summary = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_meeting_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_meeting_notes_manager_reviews_manager_review_id",
                        column: x => x.manager_review_id,
                        principalTable: "manager_review",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_signoff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    signer_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    signer_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    signer_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    signed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    client_ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    comments = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_signoff", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_signoff_manager_review_manager_review_id",
                        column: x => x.manager_review_id,
                        principalTable: "manager_review",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_meeting_notes_action",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_meeting_notes_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_meeting_notes_action", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_meeting_notes_action_review_meeting_notes_review_mee",
                        column: x => x.review_meeting_notes_id,
                        principalTable: "review_meeting_notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_review_meeting_notes_manager_review_id",
                table: "review_meeting_notes",
                column: "manager_review_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_review_meeting_notes_tenant_id_manager_review_id",
                table: "review_meeting_notes",
                columns: new[] { "tenant_id", "manager_review_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_review_meeting_notes_action_review_meeting_notes_id",
                table: "review_meeting_notes_action",
                column: "review_meeting_notes_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_meeting_notes_action_tenant_id_review_meeting_notes_",
                table: "review_meeting_notes_action",
                columns: new[] { "tenant_id", "review_meeting_notes_id" });

            migrationBuilder.CreateIndex(
                name: "ix_review_signoff_manager_review_id",
                table: "review_signoff",
                column: "manager_review_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_signoff_tenant_id_manager_review_id_signed_at",
                table: "review_signoff",
                columns: new[] { "tenant_id", "manager_review_id", "signed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "review_meeting_notes_action");

            migrationBuilder.DropTable(
                name: "review_signoff");

            migrationBuilder.DropTable(
                name: "review_meeting_notes");

            migrationBuilder.DropColumn(
                name: "is_locked",
                table: "manager_review");

            migrationBuilder.DropColumn(
                name: "signoff_completed_at",
                table: "manager_review");

            migrationBuilder.DropColumn(
                name: "signoff_requested_at",
                table: "manager_review");

            migrationBuilder.DropColumn(
                name: "signoff_status",
                table: "manager_review");

            migrationBuilder.DropColumn(
                name: "meeting_notes_template",
                table: "appraisal_cycle");

            migrationBuilder.DropColumn(
                name: "signoff_auto_close_days",
                table: "appraisal_cycle");
        }
    }
}
