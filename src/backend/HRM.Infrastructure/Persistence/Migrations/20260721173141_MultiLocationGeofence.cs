using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiLocationGeofence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attendance_geofence_locations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attendance_settings_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(10,7)", nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(10,7)", nullable: false),
                    radius_meters = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_geofence_locations", x => x.id);
                    table.ForeignKey(
                        name: "fk_attendance_geofence_locations_attendance_settings_attendanc",
                        column: x => x.attendance_settings_id,
                        principalTable: "attendance_settings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_geofence_locations_attendance_settings_id",
                table: "attendance_geofence_locations",
                column: "attendance_settings_id");

            // DORMANT tenant-isolation RLS policy for the new tenant_id table (NEW-TENANT-TABLE rule): the
            // RlsIsolation coverage-guard test fails for any tenant_id table without a policy. tenant_id is
            // NOT NULL → strict USING + WITH CHECK (NULLIF → unset/reset GUC = NULL = fail-closed).
            // DORMANT: no ENABLE — the Rls:Enabled-gated reconciler enforces it. Idempotent. Mirrors
            // salary_grades / employee_education / employee_work_history / employee_dependents.
            migrationBuilder.Sql("""
                DO $do$
                DECLARE
                    v_expr text := $q$tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid$q$;
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_policies
                        WHERE schemaname = 'public' AND tablename = 'attendance_geofence_locations' AND policyname = 'tenant_isolation'
                    ) THEN
                        EXECUTE format(
                            'CREATE POLICY tenant_isolation ON public.%I USING (%s) WITH CHECK (%s)',
                            'attendance_geofence_locations', v_expr, v_expr);
                    END IF;
                END
                $do$;
                """);

            // DF-23 / ISSUE-068 BACKFILL: every existing single-center geofence becomes ONE allowed clock-in
            // location, so those tenants land on the multi-location path with byte-identical behaviour. Idempotent
            // (WHERE NOT EXISTS guards a re-run). One 'Primary' row per enabled settings row that has a center.
            migrationBuilder.Sql("""
                INSERT INTO attendance_geofence_locations
                    (id, attendance_settings_id, name, latitude, longitude, radius_meters, tenant_id, created_at, is_deleted)
                SELECT
                    gen_random_uuid(), s.id, 'Primary', s.geo_fence_latitude, s.geo_fence_longitude,
                    s.geo_fence_radius_meters, s.tenant_id, now(), false
                FROM attendance_settings s
                WHERE s.geo_fence_enabled = true
                  AND s.geo_fence_latitude IS NOT NULL
                  AND s.geo_fence_longitude IS NOT NULL
                  AND s.is_deleted = false
                  AND NOT EXISTS (
                      SELECT 1 FROM attendance_geofence_locations g
                      WHERE g.attendance_settings_id = s.id AND g.is_deleted = false);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_geofence_locations");
        }
    }
}
