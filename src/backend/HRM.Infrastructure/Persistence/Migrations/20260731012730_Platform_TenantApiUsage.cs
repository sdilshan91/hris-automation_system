using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Platform_TenantApiUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_api_usage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    year_month = table.Column<int>(type: "integer", nullable: false),
                    call_count = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_api_usage", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_api_usage_tenant_id_year_month",
                table: "tenant_api_usage",
                columns: new[] { "tenant_id", "year_month" },
                unique: true);

            // US-PLT-004 (tenant isolation): create the DORMANT `tenant_isolation` RLS policy for the new
            // tenant_api_usage table, matching the strict (tenant_id NOT NULL) form shipped by
            // 20260710120000_Platform_RlsPolicies_Dormant. It is INERT until the increment-3 reconciler ENABLEs
            // + FORCEs RLS on the table (gated by Rls:Enabled), so this is enforcement-neutral. Without it,
            // RlsIsolationPostgresTests — which force-enables RLS on every tenant_id table — would leave this
            // table policy-less (default-deny). Idempotent via IF NOT EXISTS on pg_policies.
            migrationBuilder.Sql("""
                DO $do$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_policies
                        WHERE schemaname = 'public'
                          AND tablename  = 'tenant_api_usage'
                          AND policyname = 'tenant_isolation'
                    ) THEN
                        EXECUTE $q$
                            CREATE POLICY tenant_isolation ON public.tenant_api_usage
                            USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                            WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                        $q$;
                    END IF;
                END
                $do$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON public.tenant_api_usage;");
            migrationBuilder.DropTable(
                name: "tenant_api_usage");
        }
    }
}
