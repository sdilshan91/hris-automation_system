using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Monitoring_TenantLatencyHistogram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_latency_bucket",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hour_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    bucket_index = table.Column<int>(type: "integer", nullable: false),
                    count = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_latency_bucket", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_latency_bucket_hour_utc",
                table: "tenant_latency_bucket",
                column: "hour_utc");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_latency_bucket_tenant_id_hour_utc_bucket_index",
                table: "tenant_latency_bucket",
                columns: new[] { "tenant_id", "hour_utc", "bucket_index" },
                unique: true);

            // Critical Rule #1 (three-layer tenant isolation): ship the DORMANT tenant_isolation policy
            // with the table, exactly as tenant_api_usage does. Inert until Rls:Enabled flips it on; a new
            // tenant-scoped table without a policy would be a silent hole the day RLS is enabled.
            migrationBuilder.Sql("""
                DO $do$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_policies
                        WHERE schemaname = 'public'
                          AND tablename  = 'tenant_latency_bucket'
                          AND policyname = 'tenant_isolation'
                    ) THEN
                        EXECUTE $q$
                            CREATE POLICY tenant_isolation ON public.tenant_latency_bucket
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
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON public.tenant_latency_bucket;");
            migrationBuilder.DropTable(
                name: "tenant_latency_bucket");
        }
    }
}
