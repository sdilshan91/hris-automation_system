using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExitInterview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exit_interview",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    offboarding_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interview_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    conducted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    interview_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    overall_experience_rating = table.Column<int>(type: "integer", nullable: true),
                    would_recommend_employer = table.Column<bool>(type: "boolean", nullable: true),
                    additional_comments = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    is_superseded = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    supersedes_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exit_interview", x => x.id);
                    table.ForeignKey(
                        name: "fk_exit_interview_offboarding_instances_offboarding_instance_id",
                        column: x => x.offboarding_instance_id,
                        principalTable: "offboarding_instance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exit_interview_template",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("pk_exit_interview_template", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exit_interview_response",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exit_interview_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: true),
                    selected_option = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    free_text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exit_interview_response", x => x.id);
                    table.ForeignKey(
                        name: "fk_exit_interview_response_exit_interview_exit_interview_id",
                        column: x => x.exit_interview_id,
                        principalTable: "exit_interview",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exit_interview_question",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    options = table.Column<List<string>>(type: "text[]", nullable: false),
                    required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exit_interview_question", x => x.id);
                    table.ForeignKey(
                        name: "fk_exit_interview_question_exit_interview_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "exit_interview_template",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exit_interview_offboarding_instance_id",
                table: "exit_interview",
                column: "offboarding_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_exit_interview_tenant_id_offboarding_instance_id",
                table: "exit_interview",
                columns: new[] { "tenant_id", "offboarding_instance_id" },
                unique: true,
                filter: "is_superseded = false AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_exit_interview_question_template_id",
                table: "exit_interview_question",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_exit_interview_question_tenant_id_template_id_sort_order",
                table: "exit_interview_question",
                columns: new[] { "tenant_id", "template_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_exit_interview_response_exit_interview_id",
                table: "exit_interview_response",
                column: "exit_interview_id");

            migrationBuilder.CreateIndex(
                name: "ix_exit_interview_response_tenant_id_exit_interview_id",
                table: "exit_interview_response",
                columns: new[] { "tenant_id", "exit_interview_id" });

            migrationBuilder.CreateIndex(
                name: "ix_exit_interview_template_tenant_id_is_active",
                table: "exit_interview_template",
                columns: new[] { "tenant_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exit_interview_question");

            migrationBuilder.DropTable(
                name: "exit_interview_response");

            migrationBuilder.DropTable(
                name: "exit_interview_template");

            migrationBuilder.DropTable(
                name: "exit_interview");
        }
    }
}
