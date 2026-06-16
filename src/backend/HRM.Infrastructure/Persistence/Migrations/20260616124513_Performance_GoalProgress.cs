using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Performance_GoalProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "stale_goal_nudge_days",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "goal_progress_update",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    goal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    progress_pct = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goal_progress_update", x => x.id);
                    table.ForeignKey(
                        name: "fk_goal_progress_update_goal_goal_id",
                        column: x => x.goal_id,
                        principalTable: "goal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "goal_comment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    goal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    progress_update_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goal_comment", x => x.id);
                    table.ForeignKey(
                        name: "fk_goal_comment_goal_progress_updates_progress_update_id",
                        column: x => x.progress_update_id,
                        principalTable: "goal_progress_update",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_goal_comment_goals_goal_id",
                        column: x => x.goal_id,
                        principalTable: "goal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "goal_progress_attachment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    progress_update_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goal_progress_attachment", x => x.id);
                    table.ForeignKey(
                        name: "fk_goal_progress_attachment_goal_progress_updates_progress_upd",
                        column: x => x.progress_update_id,
                        principalTable: "goal_progress_update",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_goal_comment_goal_id",
                table: "goal_comment",
                column: "goal_id");

            migrationBuilder.CreateIndex(
                name: "ix_goal_comment_progress_update_id",
                table: "goal_comment",
                column: "progress_update_id");

            migrationBuilder.CreateIndex(
                name: "ix_goal_comment_tenant_id_goal_id_created_at_utc",
                table: "goal_comment",
                columns: new[] { "tenant_id", "goal_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_goal_progress_attachment_progress_update_id",
                table: "goal_progress_attachment",
                column: "progress_update_id");

            migrationBuilder.CreateIndex(
                name: "ix_goal_progress_attachment_tenant_id_progress_update_id",
                table: "goal_progress_attachment",
                columns: new[] { "tenant_id", "progress_update_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goal_progress_update_goal_id",
                table: "goal_progress_update",
                column: "goal_id");

            migrationBuilder.CreateIndex(
                name: "ix_goal_progress_update_tenant_id_goal_id_created_at_utc",
                table: "goal_progress_update",
                columns: new[] { "tenant_id", "goal_id", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "goal_comment");

            migrationBuilder.DropTable(
                name: "goal_progress_attachment");

            migrationBuilder.DropTable(
                name: "goal_progress_update");

            migrationBuilder.DropColumn(
                name: "stale_goal_nudge_days",
                table: "tenants");
        }
    }
}
