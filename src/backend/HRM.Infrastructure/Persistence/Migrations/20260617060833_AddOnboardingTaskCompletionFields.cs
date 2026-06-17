using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingTaskCompletionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "requires_document",
                table: "onboarding_template_task",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "attachment_file_name",
                table: "onboarding_task_instance",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attachment_storage_key",
                table: "onboarding_task_instance",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "completed_at",
                table: "onboarding_task_instance",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "completed_by_user_id",
                table: "onboarding_task_instance",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "completion_comment",
                table: "onboarding_task_instance",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_document",
                table: "onboarding_task_instance",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "requires_document",
                table: "onboarding_template_task");

            migrationBuilder.DropColumn(
                name: "attachment_file_name",
                table: "onboarding_task_instance");

            migrationBuilder.DropColumn(
                name: "attachment_storage_key",
                table: "onboarding_task_instance");

            migrationBuilder.DropColumn(
                name: "completed_at",
                table: "onboarding_task_instance");

            migrationBuilder.DropColumn(
                name: "completed_by_user_id",
                table: "onboarding_task_instance");

            migrationBuilder.DropColumn(
                name: "completion_comment",
                table: "onboarding_task_instance");

            migrationBuilder.DropColumn(
                name: "requires_document",
                table: "onboarding_task_instance");
        }
    }
}
