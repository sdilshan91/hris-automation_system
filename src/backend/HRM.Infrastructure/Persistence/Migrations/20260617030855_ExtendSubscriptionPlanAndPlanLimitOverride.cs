using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendSubscriptionPlanAndPlanLimitOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "audit_log_retention_days",
                table: "subscription_plans",
                type: "integer",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "subscription_plans",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "subscription_plans",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "enabled_modules",
                table: "subscription_plans",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "feature_flags",
                table: "subscription_plans",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<bool>(
                name: "is_public",
                table: "subscription_plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "max_api_calls_per_month",
                table: "subscription_plans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_custom_fields_per_entity",
                table: "subscription_plans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_custom_roles",
                table: "subscription_plans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_email_sends_per_month",
                table: "subscription_plans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_storage_gb",
                table: "subscription_plans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_workflows",
                table: "subscription_plans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "price_yearly",
                table: "subscription_plans",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "sla_tier",
                table: "subscription_plans",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "standard");

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "subscription_plans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "plan_limit_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    limit_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<long>(type: "bigint", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_limit_overrides", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_plan_limit_overrides_tenant_id_limit_key",
                table: "plan_limit_overrides",
                columns: new[] { "tenant_id", "limit_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plan_limit_overrides");

            migrationBuilder.DropColumn(
                name: "audit_log_retention_days",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "description",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "enabled_modules",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "feature_flags",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "is_public",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "max_api_calls_per_month",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "max_custom_fields_per_entity",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "max_custom_roles",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "max_email_sends_per_month",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "max_storage_gb",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "max_workflows",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "price_yearly",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "sla_tier",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "subscription_plans");
        }
    }
}
