using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ISSUE-335 — normalize <c>tenants.enabled_modules</c> onto the canonical <c>PlanModules</c> vocabulary.
    ///
    /// <para><b>Why this exists.</b> The column was written by two sources using two non-overlapping key
    /// vocabularies: <c>DbInitializer</c> seeded it from <c>PermissionCatalog.ByModule.Keys</c> (PERMISSION
    /// prefixes — <c>Audit</c>, <c>CustomField</c>, <c>Department</c>, <c>Roles</c>, <c>Tenant</c>, …), while
    /// <c>TenantProvisioningService.DeriveTenantModules</c> used <c>PlanModules</c> (the canonical product
    /// modules). Nothing ever READ the column, so the two writers drifted with nothing failing. Live data
    /// carried both shapes simultaneously.</para>
    ///
    /// <para>The permission vocabulary has no <c>CoreHR</c>, <c>Asset</c>, <c>CustomReportBuilder</c> or
    /// <c>PublicCareersPage</c>, and spells <c>Reporting</c> as <c>Reports</c>. So the US-ADM-012 module gate —
    /// the first code to read this column — would have denied <b>every</b> request for a seeded tenant, since
    /// <c>CoreHR</c> covers employees, departments and the dashboard. This migration removes that landmine
    /// before the gate ships.</para>
    ///
    /// <para><b>Deliberately generous, not restrictive.</b> A tenant whose row is in the legacy vocabulary is
    /// granted the FULL canonical module set. Those tenants are ungated today — nothing reads the column — so
    /// granting preserves exactly the access they currently have. Withholding <c>Asset</c> /
    /// <c>CustomReportBuilder</c> / <c>PublicCareersPage</c> merely because the old permission list happened not
    /// to contain them would make this migration a silent entitlement DOWNGRADE, which is not its job. Selling a
    /// customer a smaller plan is a commercial decision, not a data-repair side effect.</para>
    ///
    /// <para><b>What is left alone.</b> A row already in the canonical vocabulary AND containing <c>CoreHR</c>
    /// is untouched, even when it is a strict subset — that is a legitimately restricted plan and must be
    /// preserved. This is what makes the migration idempotent and safe to re-run.</para>
    /// </summary>
    public partial class Platform_NormalizeTenantEnabledModules : Migration
    {
        /// <summary>
        /// The canonical set, kept in sync with <c>PlanModules.All</c>. Duplicated as a SQL literal rather than
        /// bound to the live constant because a migration must describe the data as of ITS point in history —
        /// binding it would silently change what this migration did the next time the module list changes.
        /// </summary>
        private const string CanonicalModulesJson =
            """["CoreHR","Leave","Attendance","Recruitment","Onboarding","Payroll","Performance","Training","Asset","Benefits","Reporting","CustomReportBuilder","PublicCareersPage"]""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A row needs normalizing when it is empty/absent, carries ANY token outside the canonical set
            // (i.e. it is legacy permission-prefix data), or is missing the always-on CoreHR module.
            // jsonb_exists(...) is used rather than the `?` operator so the statement can never be mistaken
            // for a parameter placeholder anywhere in the pipeline.
            migrationBuilder.Sql($"""
                UPDATE tenants AS t
                SET enabled_modules = '{CanonicalModulesJson}'::jsonb
                WHERE t.enabled_modules IS NULL
                   OR jsonb_typeof(t.enabled_modules) <> 'array'
                   OR jsonb_array_length(t.enabled_modules) = 0
                   OR NOT jsonb_exists(t.enabled_modules, 'CoreHR')
                   OR EXISTS (
                        SELECT 1
                        FROM jsonb_array_elements_text(t.enabled_modules) AS e(val)
                        WHERE e.val NOT IN (
                            'CoreHR','Leave','Attendance','Recruitment','Onboarding','Payroll','Performance',
                            'Training','Asset','Benefits','Reporting','CustomReportBuilder','PublicCareersPage'
                        )
                   );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally a no-op. This migration repairs corrupt DATA; it makes no schema change, and the
            // pre-normalization values were a mix of two vocabularies with no per-row record of which was
            // which, so they cannot be reconstructed. Re-introducing the permission-prefix vocabulary would
            // also re-arm the outage this migration exists to prevent. Rolling the schema back is safe;
            // rolling the data back is neither possible nor desirable.
        }
    }
}
