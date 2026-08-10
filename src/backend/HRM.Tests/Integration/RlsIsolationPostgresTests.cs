// ============================================================================
// US-PLT-002 — RLS increment 2a: real-Postgres isolation tests for the DORMANT tenant_isolation policies
// shipped by migration 20260710120000_Platform_RlsPolicies_Dormant.
//
// WHY REAL POSTGRES (not InMemory): RLS is a database-engine feature. The EF InMemory provider implements
// none of it, so the only honest proof is a live Postgres where the policies are ENABLED + FORCED and the
// session connects as a NON-superuser, NON-BYPASSRLS role. The container's default `postgres` user is a
// SUPERUSER and superusers ALWAYS bypass RLS — a test run as `postgres` would prove nothing. So the fixture
// provisions the two production roles from roles.sql:
//   • hrm_app   — LOGIN, NOBYPASSRLS  → the runtime path; RLS is enforced against it (this is what we test).
//   • hrm_owner — LOGIN, BYPASSRLS    → the privileged path (migrations/seeding/system/cross-tenant jobs).
//
// The migration ships the policies DORMANT (no ENABLE). To exercise enforcement here we simulate increment 3's
// reconciler by running `ENABLE + FORCE ROW LEVEL SECURITY` on every tenant_id table (excluding `users`) after
// migrating — the policies themselves are the migration's, unchanged.
//
// These are genuine, load-bearing assertions: #2 (fail-closed), #3 (IgnoreQueryFilters still isolated) and #4
// (WITH CHECK rejects) all FAIL outright if the policies are missing or mis-shaped.
// ============================================================================

using System.Reflection;
using FluentAssertions;
using HRM.Api.Jobs;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Hangfire;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using HRM.Infrastructure.Services;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Multitenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-PLT-002-RLS")]
[Trait("Category", "RlsIsolation")]
public sealed class RlsIsolationPostgresTests : IAsyncLifetime
{
    private const string AppRole = "hrm_app";
    private const string AppPassword = "app_pw_rls_2a";
    private const string OwnerRole = "hrm_owner";
    private const string OwnerPassword = "owner_pw_rls_2a";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    // Two tenants with DELIBERATELY different row counts so a cross-tenant bleed changes the number.
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private const int EmployeesA = 2;
    private const int EmployeesB = 3;

    private readonly Guid _roleA = Guid.NewGuid();
    private readonly Guid _roleB = Guid.NewGuid();
    private readonly Guid _roleSystem = Guid.NewGuid(); // tenant_id = NULL (visible to every tenant via USING)

    private string _appConnString = null!;   // connects as hrm_app   (RLS ENFORCED)
    private string _ownerConnString = null!; // connects as hrm_owner (BYPASSRLS)

    // Drives the EF global query filter. Kept minimal — we vary its TenantId per test.
    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "acme";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status, string? plan = null,
            IReadOnlyCollection<string>? enabledModules = null, string? logoUrl = null, string? primaryColor = null)
            => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var superCs = _postgres.GetConnectionString(); // container default = superuser 'postgres'
        _appConnString = WithRole(superCs, AppRole, AppPassword);
        _ownerConnString = WithRole(superCs, OwnerRole, OwnerPassword);

        // (1) Provision the two roles as the superuser (mirrors roles.sql; app never runs role DDL).
        await ExecAsync(superCs,
            $"""
             DO $r$
             BEGIN
                 IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                     CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}';
                 END IF;
                 IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{OwnerRole}') THEN
                     CREATE ROLE {OwnerRole} LOGIN PASSWORD '{OwnerPassword}' BYPASSRLS;
                 END IF;
             END
             $r$;
             ALTER ROLE {AppRole} NOBYPASSRLS;
             """);

        // (2) Apply migrations (as the superuser/owner) so the schema + DORMANT policies exist.
        await using (var migrate = OwnerAwareDb(superCs, new MutableTenantContext()))
        {
            await migrate.Database.MigrateAsync();
        }

        // Grants for the app + owner roles (owner has BYPASSRLS but still needs table privileges).
        //
        // ⚠ This block MIRRORS Rls/roles.sql by hand — roles.sql cannot be executed here because it uses
        // psql-only constructs (\gexec and :'var' interpolation) that Npgsql will not parse. Any privilege
        // change made there must be made here too, or this suite validates a permission set production
        // does not have. (The same hand-maintained-mirror hazard as finding S-2; the audit-immutability
        // arm below is what keeps THIS half of the mirror honest.)
        await ExecAsync(superCs,
            $"""
             GRANT USAGE ON SCHEMA public TO {AppRole}, {OwnerRole};
             GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRole}, {OwnerRole};
             GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {AppRole}, {OwnerRole};
             """);

        // GAP-005: audit immutability. roles.sql revokes UPDATE/DELETE on the audit tables from the runtime
        // role AFTER the broad grant above; mirrored here in the same order for the same reason.
        await ExecAsync(superCs,
            $"""
             REVOKE UPDATE, DELETE ON audit_logs FROM {AppRole};
             REVOKE UPDATE, DELETE ON employee_field_audit_logs FROM {AppRole};
             """);

        // (3) Simulate the increment-3 reconciler: ENABLE + FORCE RLS on every tenant_id table (excl. users).
        await ExecAsync(superCs,
            """
            DO $e$
            DECLARE r record;
            BEGIN
                FOR r IN
                    SELECT c.table_name
                    FROM information_schema.columns c
                    JOIN information_schema.tables t
                      ON t.table_schema = c.table_schema AND t.table_name = c.table_name
                    WHERE c.table_schema = 'public'
                      AND c.column_name  = 'tenant_id'
                      AND t.table_type   = 'BASE TABLE'
                      AND c.table_name NOT IN ('users', 'tenants')
                LOOP
                    EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', r.table_name);
                    EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY', r.table_name);
                END LOOP;
            END
            $e$;
            """);

        // (4) Seed as the superuser (bypasses RLS + WITH CHECK) — mirrors the privileged migration/seed path.
        await SeedAsync(superCs);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ─────────────────────────────────────────────────────────────────────────
    // 1. GUC-set ⇒ own tenant only (raw SELECT + EF), for BOTH tenants.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GucSetToTenant_RawSqlAndEf_ReturnOnlyThatTenantsRows()
    {
        // Raw SQL over hrm_app with the GUC set to A ⇒ only A's employees.
        (await RawCountAsync(_appConnString, _tenantA, "employees")).Should().Be(EmployeesA);
        (await RawCountAsync(_appConnString, _tenantB, "employees")).Should().Be(EmployeesB);

        // EF over hrm_app (filter + RLS both scope to the tenant) ⇒ same numbers.
        (await EfEmployeeCountAsync(_tenantA, ignoreFilters: false)).Should().Be(EmployeesA);
        (await EfEmployeeCountAsync(_tenantB, ignoreFilters: false)).Should().Be(EmployeesB);

        // roles is the NULLABLE-tenant table: USING admits the NULL (system) role for BOTH tenants, but each
        // tenant's own role is hidden from the other.
        var visibleToA = await EfRoleIdsAsync(_tenantA);
        visibleToA.Should().Contain(_roleA).And.Contain(_roleSystem).And.NotContain(_roleB);

        var visibleToB = await EfRoleIdsAsync(_tenantB);
        visibleToB.Should().Contain(_roleB).And.Contain(_roleSystem).And.NotContain(_roleA);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. GUC-unset on hrm_app ⇒ 0 rows (fail-closed). The unset GUC is SQL NULL, and `tenant_id = NULL`
    //    is never true, so the non-bypass role sees NOTHING — the inverse of the EF filter's see-all.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GucUnset_OnAppRole_ReturnsZeroRows_FailClosed()
    {
        (await RawCountAsync(_appConnString, guc: null, "employees")).Should().Be(0);
        (await RawCountAsync(_appConnString, guc: null, "departments")).Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. HEADLINE BACKSTOP — a misused IgnoreQueryFilters() cannot cross tenants: EF's filter is removed,
    //    yet RLS still caps the result to the GUC tenant.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task IgnoreQueryFilters_OnAppRole_IsStillTenantIsolatedByRls()
    {
        // Unresolved EF tenant context ⇒ WITHOUT IgnoreQueryFilters EF's filter would "see all". We strip the
        // filter AND leave the context unresolved, so the ONLY thing isolating the result is RLS.
        var count = await EfEmployeeCountAsync(gucTenant: _tenantA, ignoreFilters: true, efTenant: Guid.Empty);
        count.Should().Be(EmployeesA, "RLS must isolate even when the EF query filter is bypassed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4a. WITH CHECK rejects an insert whose tenant_id ≠ the GUC (strict table: departments).
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task WithCheck_RejectsForeignTenantInsert_OnStrictTable()
    {
        // GUC = A, but we try to write a departments row stamped for tenant B.
        var act = async () => await RawInsertAsync(_appConnString, guc: _tenantA,
            "INSERT INTO departments (id, name, code, tenant_id, created_at) VALUES (@id, 'X', 'X', @tid, now())",
            tenantId: _tenantB);

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege,
                "an RLS WITH CHECK violation surfaces as SQLSTATE 42501");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4b. WITH CHECK is STRICT even on the NULLABLE-tenant `roles` table: an app session may READ NULL/system
    //     rows (USING) but must NEVER MINT one (WITH CHECK). Proves the R6 strict-check requirement.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task WithCheck_RejectsNullTenantRoleInsert_FromAppSession()
    {
        var act = async () => await RawInsertAsync(_appConnString, guc: _tenantA,
            "INSERT INTO roles (id, name, is_built_in, tenant_id, created_at) VALUES (@id, 'Sneaky System', false, @tid, now())",
            tenantId: null); // NULL tenant_id (a system role)

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Privileged bypass — hrm_owner (BYPASSRLS) with NO GUC spans both tenants. Proves the migration/seed
    //    and system/cross-tenant paths keep working under enforcement.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task PrivilegedOwnerRole_NoGuc_SpansAllTenants()
    {
        (await RawCountAsync(_ownerConnString, guc: null, "employees")).Should().Be(EmployeesA + EmployeesB);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. No cross-tenant bleed across a POOLED connection: on ONE physical connection, tx1 (GUC=A) sees A,
    //    tx2 (GUC=B) sees B, then a NO-tx query sees 0 — proving is_local=>true resets on commit and never
    //    leaks into a later reuse of the same connection.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task IsLocalGuc_DoesNotBleedAcrossPooledConnectionReuse()
    {
        await using var conn = new NpgsqlConnection(_appConnString);
        await conn.OpenAsync();

        // tx1: GUC=A
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await SetGucAsync(conn, tx, _tenantA);
            (await CountInAsync(conn, tx, "employees")).Should().Be(EmployeesA);
            await tx.CommitAsync();
        }

        // tx2: GUC=B on the SAME connection
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await SetGucAsync(conn, tx, _tenantB);
            (await CountInAsync(conn, tx, "employees")).Should().Be(EmployeesB);
            await tx.CommitAsync();
        }

        // No transaction ⇒ no is_local GUC in effect ⇒ fail-closed 0 (nothing leaked from tx1/tx2).
        await using (var cmd = new NpgsqlCommand("SELECT count(*) FROM employees", conn))
        {
            ((long)(await cmd.ExecuteScalarAsync())!).Should().Be(0);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6b. AUDIT IMMUTABILITY (GAP-005) — the runtime role may APPEND to the audit trail but never rewrite or
    //     erase it. Before this, "append-only" was a code convention (no update/delete endpoint) while
    //     hrm_app held UPDATE and DELETE on every table, so a SQL-injection foothold or a stray
    //     ExecuteDelete could silently rewrite audit history. Deletion is still needed by the retention
    //     purge, which runs on the PRIVILEGED connection — asserted here too, because a revoke that also
    //     broke the purge would be a different bug rather than a fix.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task AuditTables_AreAppendOnly_ForTheRuntimeRole_ButPurgeableByOwner_GAP005()
    {
        foreach (var table in new[] { "audit_logs", "employee_field_audit_logs" })
        {
            // SELECT and INSERT must survive — the app writes an audit row on essentially every request,
            // so an over-broad revoke would take the product down rather than harden it.
            var canSelect = await ScalarBoolAsync(_appConnString,
                $"SELECT has_table_privilege('{AppRole}', '{table}', 'SELECT')");
            var canInsert = await ScalarBoolAsync(_appConnString,
                $"SELECT has_table_privilege('{AppRole}', '{table}', 'INSERT')");
            canSelect.Should().BeTrue($"{table} must stay readable by the runtime role");
            canInsert.Should().BeTrue($"{table} must stay writable (append) by the runtime role");

            var canUpdate = await ScalarBoolAsync(_appConnString,
                $"SELECT has_table_privilege('{AppRole}', '{table}', 'UPDATE')");
            var canDelete = await ScalarBoolAsync(_appConnString,
                $"SELECT has_table_privilege('{AppRole}', '{table}', 'DELETE')");
            canUpdate.Should().BeFalse($"{table} is append-only: the runtime role must not be able to rewrite audit history");
            canDelete.Should().BeFalse($"{table} is append-only: the runtime role must not be able to erase audit history");

            // The privileged role keeps DELETE so AuditLogPurgeService (US-ADM-008 FR-6) still works.
            var ownerCanDelete = await ScalarBoolAsync(_ownerConnString,
                $"SELECT has_table_privilege('{OwnerRole}', '{table}', 'DELETE')");
            ownerCanDelete.Should().BeTrue($"the retention purge deletes {table} rows on the privileged connection");
        }

        // Belt and braces: the privilege bits above are what Postgres CONSULTS, but assert the real
        // statement is actually refused, so a future GRANT cannot make the bits lie.
        await using var conn = new NpgsqlConnection(_appConnString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE audit_logs SET event_type = 'tampered'", conn);
        var act = async () => await cmd.ExecuteNonQueryAsync();
        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == "42501", "insufficient_privilege");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. COVERAGE GUARD — every mapped entity carrying a TenantId (except the global `users` table) must have a
    //    tenant_isolation policy. Fails loudly when a future tenant entity forgets its policy (or when `users`
    //    accidentally acquires one).
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task EveryTenantScopedEntity_HasRlsPolicy_CoverageGuard()
    {
        HashSet<string> expected;
        await using (var db = OwnerAwareDb(_ownerConnString, new MutableTenantContext()))
        {
            expected = db.Model.GetEntityTypes()
                .Where(et => et.FindProperty("TenantId") is not null)
                .Select(et => et.GetTableName())
                .Where(t => t is not null && t != "users") // global identity table — deliberately unpoliced
                .Select(t => t!)
                .ToHashSet();
        }

        expected.Should().NotBeEmpty();

        var policied = new HashSet<string>();
        await using (var conn = new NpgsqlConnection(_ownerConnString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT tablename FROM pg_policies WHERE schemaname = 'public' AND policyname = 'tenant_isolation'", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) policied.Add(reader.GetString(0));
        }

        // Every tenant-scoped entity is policied …
        expected.Should().BeSubsetOf(policied,
            "every entity with a TenantId must have a tenant_isolation RLS policy");
        // … and there are no stray policies (e.g. the global `users` table must NOT be policied).
        policied.Should().BeEquivalentTo(expected,
            "the policy set must match the tenant-scoped entity set exactly (no missing, no extra)");
        policied.Should().NotContain("users");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9. PER-JOB GUC — the RLS increment 2c TenantJobRunner. RunForTenantAsync(A) on hrm_app (RLS enabled) must
    //    set the app.current_tenant GUC so a background job sees ONLY A's rows even with IgnoreQueryFilters
    //    (RLS is the sole isolation), and a subsequent RunForTenantAsync(B) rescopes to B. This is the job-side
    //    equivalent of the request-path TenantTransactionBehavior; without it a per-tenant job on hrm_app with no
    //    GUC would be fail-closed (0 rows, test #2).
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task TenantJobRunner_OnAppRole_RlsEnabled_SetsGuc_SeesOnlyItsTenant()
    {
        var tenant = new HRM.Infrastructure.Services.TenantContext();
        await using var db = OwnerAwareDb(_appConnString, tenant);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Rls:Enabled"] = "true" })
            .Build();
        var runner = new HRM.Infrastructure.Services.TenantJobRunner(db, tenant, config);

        // IgnoreQueryFilters() ⇒ EF's tenant filter is off, so ONLY the runner-set GUC (RLS) isolates the result.
        var seenA = -1;
        await runner.RunForTenantAsync(_tenantA, "tenant-a",
            async _ => seenA = await db.Employees.IgnoreQueryFilters().CountAsync());
        seenA.Should().Be(EmployeesA, "the runner set the GUC to A, so RLS caps the job to A's rows");

        var seenB = -1;
        await runner.RunForTenantAsync(_tenantB, "tenant-b",
            async _ => seenB = await db.Employees.IgnoreQueryFilters().CountAsync());
        seenB.Should().Be(EmployeesB, "a subsequent run rescopes the GUC to B");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 10. DF-63-rls — AutoClockOutJob shift resolution + open-log sweep under RLS-ON.
    //
    // DF-63 (#428) shipped the shift-aware auto-clock-out and proved the shift math on real Postgres with RLS
    // OFF (the default). The residual DF-63-rls closes the RLS-ON path: when row visibility is enforced by the
    // app.current_tenant GUC + FORCE ROW LEVEL SECURITY (not just the EF global query filter), the job must
    //   (a) NOT fail-closed — it resolves the tenant's shift and sweeps the tenant's open logs INSIDE the GUC the
    //       TenantJobRunner sets (RLS increment 2c), so tenant A's open log is actually closed; and
    //   (b) resolve ONLY the current tenant's shift — A's system-closed minutes reflect A's shift knob, never a
    //       fail-closed fallback nor a cross-tenant shift.
    //
    // Setup (seeded via the BYPASSRLS owner): tenant ACTIVE has a FR-5 default shift with AutoBreakMinutes=120
    // (→ 599 − 120 = 479) that DIFFERS from its tenant AttendanceSettings break of 60 (→ 539); tenant SUSPENDED
    // (excluded from the job's active-tenant scan) has a default shift with a distinctive break of 30 (→ 569) so a
    // cross-tenant shift bleed would surface a third, unique number. Each tenant has ONE OPEN log clocked in
    // yesterday 14:00 UTC (a fixed 599-min gross span) — old enough to be auto-clock-out eligible.
    //
    // The job runs through RunForTenantAsync (Rls:Enabled=true), so the GUC is set. Assertions:
    //   • ACTIVE's open log is closed as ANOMALY  ⇒ the sweep ran INSIDE the GUC (a missing GUC ⇒ fail-closed 0
    //     rows, test #2, ⇒ nothing closed). RLS-decisive.
    //   • ACTIVE's TotalWorkMinutes == 479 (its OWN shift's 120 break) — not 539 (tenant fallback, i.e. the shift
    //     read fail-closed inside ResolveForEmployeeAsync) and not 569 (SUSPENDED's shift). Proves DF-63's shift
    //     resolution read the RIGHT tenant's shift under the GUC. RLS-decisive — the core of this residual.
    //   • SUSPENDED's open log is UNTOUCHED — no cross-tenant over-close (defense-in-depth; the EF filter also
    //     scopes to ACTIVE here, so this rides on top of the two RLS-decisive checks above).
    //   • The wired TenantJobRunner sets the GUC so a job body on hrm_app sees ONLY ACTIVE's rows (mirrors #9).
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    [Trait("TC", "TC-ATT-162")]
    public async Task AutoClockOutJob_OnAppRole_RlsEnabled_ResolvesOwnTenantShift_AndSweepsOnlyOwnLogs_DF63()
    {
        var tenantActive = Guid.NewGuid();
        var tenantSuspended = Guid.NewGuid();
        var empActive = Guid.NewGuid();
        var empSuspended = Guid.NewGuid();

        // Open logs clocked in yesterday 14:00 UTC → the job closes them at yesterday 23:59:59 → a fixed 599-min
        // gross span independent of the wall clock at run time.
        var clockInUtc = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-1).AddHours(14), DateTimeKind.Utc);

        // Seed via the BYPASSRLS owner (bypasses RLS + WITH CHECK); TenantId set explicitly on every row.
        Guid activeLogId, suspendedLogId;
        await using (var seed = OwnerAwareDb(_ownerConnString, new MutableTenantContext()))
        {
            // ACTIVE tenant: default shift break 120 (→479); tenant settings break 60 (the 539 fallback).
            SeedAutoClockOutTenant(seed, tenantActive, "acjob-active", TenantStatus.Active, empActive,
                shiftAutoBreak: 120, settingsAutoBreak: 60);
            activeLogId = SeedOpenAttendanceLog(seed, tenantActive, empActive, clockInUtc);

            // SUSPENDED tenant: excluded from the active-tenant scan; distinctive shift break 30 (→569).
            SeedAutoClockOutTenant(seed, tenantSuspended, "acjob-susp", TenantStatus.Suspended, empSuspended,
                shiftAutoBreak: 30, settingsAutoBreak: 45);
            suspendedLogId = SeedOpenAttendanceLog(seed, tenantSuspended, empSuspended, clockInUtc);

            await seed.SaveChangesAsync();
        }

        // Build the job's DI graph on the APP role (RLS enforced) with Rls:Enabled=true, so the runner sets the GUC.
        await using var provider = BuildAutoClockOutRlsJobProvider();
        var job = new AutoClockOutJob(provider.GetRequiredService<IServiceScopeFactory>());
        await job.RunAsync();

        // Verify with the BYPASSRLS owner + IgnoreQueryFilters so the assertions can see BOTH tenants' rows (an
        // unset GUC / cross-tenant bleed cannot hide the failure from the check).
        await using (var verify = OwnerAwareDb(_ownerConnString, new MutableTenantContext()))
        {
            var activeLog = await verify.AttendanceLogs.IgnoreQueryFilters().SingleAsync(a => a.Id == activeLogId);
            var suspendedLog = await verify.AttendanceLogs.IgnoreQueryFilters().SingleAsync(a => a.Id == suspendedLogId);

            // (a) ACTIVE's log closed INSIDE the runner-set GUC — a missing GUC fail-closes to 0 rows (test #2) and
            //     would leave it open.
            activeLog.ClockOut.Should().NotBeNull(
                "the sweep ran inside the runner-set GUC; without it hrm_app fail-closes to 0 rows and closes nothing");
            activeLog.Status.Should().Be("ANOMALY");

            // (b) minutes reflect ACTIVE's OWN shift (120 break → 479), not the tenant fallback (60 → 539, which
            //     would mean ResolveForEmployeeAsync fail-closed) nor SUSPENDED's shift (30 → 569, a cross-tenant bleed).
            activeLog.TotalWorkMinutes.Should().Be(479,
                "ResolveForEmployeeAsync read ACTIVE's own shift under the GUC (120 break → 479); 539 == shift read fail-closed, 569 == cross-tenant bleed");

            // (c) SUSPENDED's log untouched — no cross-tenant over-close.
            suspendedLog.ClockOut.Should().BeNull(
                "a suspended tenant is not swept and its rows are never visible to ACTIVE's GUC-scoped sweep");
            suspendedLog.TotalWorkMinutes.Should().BeNull();
        }

        // (d) The wired TenantJobRunner sets the GUC so a job body on hrm_app sees ONLY ACTIVE's rows even with
        //     IgnoreQueryFilters (RLS is the sole isolation) — mirrors test #9, using the job's own DI graph.
        using (var scope = provider.CreateScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seenActive = -1;
            await runner.RunForTenantAsync(tenantActive, "acjob-active",
                async _ => seenActive = await db.Employees.IgnoreQueryFilters().CountAsync());
            seenActive.Should().Be(1,
                "the runner set the GUC to ACTIVE, so RLS caps the job body to ACTIVE's single employee (0 would mean the GUC was never set → fail-closed)");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// DF-63-rls: the job resolves ITenantJobRunner + AppDbContext + IShiftService from the per-tenant scope it
    /// creates, so the DI graph must register the concrete TenantContext (the runner flips it via SetTenant), the
    /// AppDbContext on the hrm_app connection (RLS enforced), the real runner + shift service, and Rls:Enabled=true
    /// so RunForTenantAsync sets the app.current_tenant GUC around the sweep. Mirrors the InMemory job provider.
    /// </summary>
    private ServiceProvider BuildAutoClockOutRlsJobProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ITenantContext, HRM.Infrastructure.Services.TenantContext>();
        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(_appConnString, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddSingleton(Substitute.For<ICurrentUser>());
        services.AddScoped<IShiftService, HRM.Infrastructure.Services.ShiftService>();
        services.AddScoped<ITenantJobRunner, HRM.Infrastructure.Services.TenantJobRunner>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Rls:Enabled"] = "true" })
            .Build());
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Seeds a tenant with one employee (Dept + JobTitle parents), a tenant AttendanceSettings row (the shift-not-
    /// resolved fallback break) and a FR-5 default shift carrying the discriminating explicit AutoBreakMinutes.
    /// TenantId is stamped explicitly on every row; the owner (BYPASSRLS) is used for the write. UserId is left
    /// null (the fk_employees_users_user_id FK is nullable), so no User parent is needed.
    /// </summary>
    private static void SeedAutoClockOutTenant(AppDbContext db, Guid tenantId, string subdomain,
        TenantStatus status, Guid employeeId, int shiftAutoBreak, int settingsAutoBreak)
    {
        db.Tenants.Add(new Tenant { Id = tenantId, Subdomain = subdomain, Name = subdomain, Status = status });

        var deptId = Guid.NewGuid();
        var jobTitleId = Guid.NewGuid();
        db.Departments.Add(new Department
        {
            Id = deptId, TenantId = tenantId, Name = "Dept", Code = "GEN", IsActive = true, IsDeleted = false,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = jobTitleId, TenantId = tenantId, TitleName = "Title", IsActive = true, IsDeleted = false,
        });
        db.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNo = $"{subdomain}-1",
            FirstName = "Emp", LastName = subdomain, Email = $"{subdomain}@example.com",
            Status = EmployeeStatus.Active, DepartmentId = deptId, JobTitleId = jobTitleId,
        });

        // Tenant AttendanceSettings — the break used ONLY if the shift is not resolved (the 539 fallback).
        db.AttendanceSettings.Add(new AttendanceSettings
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
            AutoBreakThresholdMinutes = 180, AutoBreakMinutes = settingsAutoBreak,
        });

        // FR-5 tenant-default shift carrying the discriminating explicit auto-break override (DF-56 knob).
        db.Shifts.Add(new Shift
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "General",
            Type = ShiftType.Single, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0),
            WorkingDays = new List<int> { 1, 2, 3, 4, 5 }, IsDefault = true, IsActive = true,
            AutoBreakMinutes = shiftAutoBreak,
        });
    }

    /// <summary>Seeds an OPEN attendance log (ClockOut null) at a fixed UTC instant for a deterministic span.</summary>
    private static Guid SeedOpenAttendanceLog(AppDbContext db, Guid tenantId, Guid employeeId, DateTime clockInUtc)
    {
        var id = BaseEntity.NewUuidV7();
        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = id, TenantId = tenantId, EmployeeId = employeeId,
            ClockIn = clockInUtc, Source = "WEB",
        });
        return id;
    }

    private static string WithRole(string baseConnString, string user, string password) =>
        new NpgsqlConnectionStringBuilder(baseConnString) { Username = user, Password = password }.ToString();

    private static async Task<bool> ScalarBoolAsync(string connString, string sql)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task ExecAsync(string connString, string sql)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 11. CrossTenantScope — the fix for the fail-open → fail-closed inversion (Wave 5 flip prep).
    //
    // The EF filter is FAIL-OPEN (`!IsResolved || TenantId == current`), so `IgnoreQueryFilters()` has always
    // been enough to reach across tenants. RLS is FAIL-CLOSED: the same call returns ZERO ROWS. Five auth
    // flows depend on genuinely cross-tenant reads/writes — tenant switching, listing a user's workspaces, and
    // three "revoke this user's sessions everywhere" paths — and each of them silently stops working, looking
    // exactly like "no such record". `CrossTenantScope.Enter()` re-routes to the BYPASSRLS role for the block.
    //
    // These arms drive the REAL interceptors (routing + GUC) rather than hand-built connections, because the
    // scope's entire mechanism IS those interceptors reading AmbientTenant.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Production-shaped context: both interceptors, RLS on, default⇒hrm_app, privileged⇒hrm_owner.</summary>
    private AppDbContext RoutedDb(ITenantContext tc)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Rls:Enabled"] = "true",
                ["ConnectionStrings:DefaultConnection"] = _appConnString,
                ["ConnectionStrings:PrivilegedConnection"] = _ownerConnString,
            })
            .Build();

        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_appConnString, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .AddInterceptors(
                    new ConnectionRoutingInterceptor(config),
                    new TenantGucConnectionInterceptor(config))
                .Options,
            tc);
    }

    [Fact]
    public async Task WithoutCrossTenantScope_ACrossTenantReadIsSilentlyEmpty_WaveFivePrep()
    {
        // This is the bug, reproduced: the exact shape of SwitchTenantAsync's membership lookup — read another
        // tenant's row while the ambient is this tenant. No error, no exception; just nothing.
        AmbientTenant.SetTenant(_tenantA);
        try
        {
            await using var db = RoutedDb(new MutableTenantContext { TenantId = _tenantA });

            var otherTenantsRows = await db.Employees
                .IgnoreQueryFilters()
                .CountAsync(e => e.TenantId == _tenantB);

            otherTenantsRows.Should().Be(0,
                "RLS is fail-closed, so IgnoreQueryFilters() reaches nothing — which is why every "
                + "cross-tenant auth flow breaks silently the moment the flag flips");
        }
        finally
        {
            AmbientTenant.Clear();
        }
    }

    [Fact]
    public async Task WithCrossTenantScope_TheSameReadSeesTheOtherTenant_WaveFivePrep()
    {
        AmbientTenant.SetTenant(_tenantA);
        try
        {
            await using var db = RoutedDb(new MutableTenantContext { TenantId = _tenantA });

            int seen;
            using (CrossTenantScope.Enter())
            {
                seen = await db.Employees
                    .IgnoreQueryFilters()
                    .CountAsync(e => e.TenantId == _tenantB);
            }

            seen.Should().Be(EmployeesB,
                "inside the scope the connection routes to the BYPASSRLS role, so a legitimately "
                + "cross-tenant read works again");
        }
        finally
        {
            AmbientTenant.Clear();
        }
    }

    [Fact]
    public async Task CrossTenantScope_RESTORES_isolation_afterwards_WaveFivePrep()
    {
        // THE arm. A scope that entered system context and never restored would leave the rest of the request
        // running privileged — tenant isolation silently off for everything downstream. That is a far worse
        // bug than the one the scope fixes, and it would not show up in any of the flows above.
        AmbientTenant.SetTenant(_tenantA);
        try
        {
            await using var db = RoutedDb(new MutableTenantContext { TenantId = _tenantA });

            using (CrossTenantScope.Enter())
            {
                (await db.Employees.IgnoreQueryFilters().CountAsync(e => e.TenantId == _tenantB))
                    .Should().Be(EmployeesB);
            }

            AmbientTenant.Current.Should().NotBeNull();
            AmbientTenant.Current!.Value.IsSystemContext.Should().BeFalse(
                "the ambient must be exactly what it was before the scope");
            AmbientTenant.Current!.Value.TenantId.Should().Be(_tenantA);

            (await db.Employees.IgnoreQueryFilters().CountAsync(e => e.TenantId == _tenantB))
                .Should().Be(0, "isolation must be back in force the moment the scope is disposed");
        }
        finally
        {
            AmbientTenant.Clear();
        }
    }

    [Fact]
    public async Task CrossTenantScope_restores_even_when_the_block_THROWS_WaveFivePrep()
    {
        // Auth flows throw (a denied switch, a failed save). If an exception escaped the scope without
        // restoring, the request would continue privileged — so the leak would happen precisely on the error
        // paths nobody exercises.
        AmbientTenant.SetTenant(_tenantA);
        try
        {
            await using var db = RoutedDb(new MutableTenantContext { TenantId = _tenantA });

            // Deliberately INLINE, not via an `async () => …` lambda handed to Should().ThrowAsync().
            // AmbientTenant is AsyncLocal: a value set inside a nested async flow never propagates back to the
            // caller, so the lambda form would report "restored" no matter what Dispose did — it passed under a
            // mutation that removed the restore entirely. The leak is only observable in the same async context
            // that entered the scope.
            var threw = false;
            try
            {
                using (CrossTenantScope.Enter())
                {
                    await db.Employees.IgnoreQueryFilters().CountAsync(e => e.TenantId == _tenantB);
                    throw new InvalidOperationException("boom");
                }
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            threw.Should().BeTrue("the arm is worthless if the exception never happened");
            AmbientTenant.Current!.Value.IsSystemContext.Should().BeFalse();
            (await db.Employees.IgnoreQueryFilters().CountAsync(e => e.TenantId == _tenantB))
                .Should().Be(0, "an exception must not strand the request in privileged context");
        }
        finally
        {
            AmbientTenant.Clear();
        }
    }

    [Fact]
    public async Task CrossTenantScope_allows_a_write_that_the_strict_WITH_CHECK_would_reject_WaveFivePrep()
    {
        // SwitchTenantAsync inserts a RefreshToken carrying the TARGET tenant's id. On hrm_app that violates
        // the strict WITH CHECK (42501) — a 500 rather than a graceful failure. Employees stands in for the
        // same shape: a row stamped for a tenant other than the ambient one.
        AmbientTenant.SetTenant(_tenantA);
        try
        {
            await using var db = RoutedDb(new MutableTenantContext { TenantId = _tenantA });
            var departmentId = Guid.NewGuid();

            using (CrossTenantScope.Enter())
            {
                db.Departments.Add(new Department
                {
                    Id = departmentId,
                    TenantId = _tenantB,
                    Name = "Cross-tenant write " + Guid.NewGuid().ToString("N")[..8],
                    Code = "X" + Guid.NewGuid().ToString("N")[..6],
                    IsActive = true,
                });
                await db.SaveChangesAsync();
            }

            // Assert on the row that was actually written. An earlier version of this arm counted EMPLOYEES to
            // "prove" a DEPARTMENT insert — true regardless of what SaveChanges did, so the only real signal
            // was the absence of a 42501. Now the landed row is checked explicitly, read back on the owner
            // connection so RLS cannot hide it.
            (await RawCountAsync(_ownerConnString, _tenantB, "departments"))
                .Should().BeGreaterThan(0, "the cross-tenant department write must actually land");

            await using (var ownerDb = OwnerAwareDb(_ownerConnString, new MutableTenantContext { TenantId = _tenantB }))
            {
                (await ownerDb.Departments.IgnoreQueryFilters().AnyAsync(d => d.Id == departmentId))
                    .Should().BeTrue("the exact row inserted inside the scope must exist, stamped for tenant B");
            }
        }
        finally
        {
            AmbientTenant.Clear();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12. THE CALL SITES, not just the mechanism.
    //
    // Arms #11 prove CrossTenantScope works. They do NOT prove it is still PLUGGED IN at the five production
    // sites it was built for — delete the `using` from GetMyTenantsAsync and every one of those arms stays
    // green. Every unit test that exercises these methods runs on EF InMemory, which has no RLS at all, so the
    // whole existing suite is structurally blind to a removed scope.
    //
    // That is the dangerous shape: invisible today (RLS off ⇒ prod and tests agree), detonating only after the
    // flip, and looking exactly like "you are not a member of any other workspace". This arm drives the real
    // AuthService against hrm_app with ENABLE + FORCE RLS, so removing the scope FAILS a test.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetMyTenantsAsync_StillListsEVERYWorkspace_UnderRls_WaveFivePrep()
    {
        var userId = Guid.NewGuid();
        await SeedUserInBothTenantsAsync(userId);

        // Ambient = tenant A, exactly as a request arriving on acme.* would be.
        AmbientTenant.SetTenant(_tenantA);
        try
        {
            var tenantContext = new MutableTenantContext { TenantId = _tenantA };
            await using var db = RoutedDb(tenantContext);
            var auth = new AuthService(
                db,
                Substitute.For<IJwtService>(),
                tenantContext,
                Substitute.For<ITotpService>(),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
                new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
                NullLogger<AuthService>.Instance,
                Substitute.For<IBackgroundJobClient>());

            var result = await auth.GetMyTenantsAsync(userId, _tenantA);

            result.IsSuccess.Should().BeTrue();
            result.Value!.Select(m => m.TenantId).Should().BeEquivalentTo(
                new[] { _tenantA, _tenantB },
                "the workspace switcher must list BOTH memberships. Without CrossTenantScope this returns only "
                + "tenant A — and that truncated list is then cached, so one request poisons the switcher for "
                + "every later one.");
        }
        finally
        {
            AmbientTenant.Clear();
        }
    }

    /// <summary>Seeds one user holding an ACTIVE membership in both tenants, written on the owner connection.</summary>
    private async Task SeedUserInBothTenantsAsync(Guid userId)
    {
        await using var db = OwnerAwareDb(_ownerConnString, new MutableTenantContext { TenantId = _tenantA });

        db.Users.Add(new User
        {
            Id = userId,
            Email = $"switcher-{userId:N}@acme.test",
            DisplayName = "Workspace Switcher",
            PasswordHash = "x",
            IsActive = true,
        });

        foreach (var tenantId in new[] { _tenantA, _tenantB })
        {
            db.UserTenants.Add(new UserTenant
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TenantId = tenantId,
                Status = UserTenantStatus.Active,
            });
        }

        await db.SaveChangesAsync();
    }

    private AppDbContext OwnerAwareDb(string connString, ITenantContext tc) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connString, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            // Defensive: a sibling's uncommitted model tweak shouldn't fail the RLS suite on Migrate.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options, tc);

    private static async Task SetGucAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid tenantId)
    {
        await using var cmd = new NpgsqlCommand("SELECT set_config('app.current_tenant', @t, true)", conn, tx);
        cmd.Parameters.AddWithValue("t", tenantId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountInAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string table)
    {
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table}", conn, tx);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    // Raw count on a fresh connection, optionally inside a GUC-set transaction.
    private static async Task<long> RawCountAsync(string connString, Guid? guc, string table)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        if (guc is { } g) await SetGucAsync(conn, tx, g);
        var count = await CountInAsync(conn, tx, table);
        await tx.CommitAsync();
        return count;
    }

    private static async Task RawInsertAsync(string connString, Guid guc, string insertSql, Guid? tenantId)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetGucAsync(conn, tx, guc);
        await using var cmd = new NpgsqlCommand(insertSql, conn, tx);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("tid", (object?)tenantId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    // EF employee count as hrm_app with the GUC set (in an explicit tx so EF reuses that connection).
    private async Task<int> EfEmployeeCountAsync(Guid gucTenant, bool ignoreFilters, Guid? efTenant = null)
    {
        var tc = new MutableTenantContext { TenantId = efTenant ?? gucTenant };
        await using var db = OwnerAwareDb(_appConnString, tc);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.current_tenant', {0}, true)", gucTenant.ToString());
        IQueryable<Employee> q = db.Employees;
        if (ignoreFilters) q = q.IgnoreQueryFilters();
        var count = await q.CountAsync();
        await tx.CommitAsync();
        return count;
    }

    private async Task<List<Guid>> EfRoleIdsAsync(Guid gucTenant)
    {
        var tc = new MutableTenantContext { TenantId = Guid.Empty }; // unresolved ⇒ isolation is RLS-only
        await using var db = OwnerAwareDb(_appConnString, tc);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.current_tenant', {0}, true)", gucTenant.ToString());
        var ids = await db.Roles.IgnoreQueryFilters().Select(r => r.Id).ToListAsync();
        await tx.CommitAsync();
        return ids;
    }

    // Seed both tenants (as the superuser ⇒ RLS + WITH CHECK are bypassed, mirroring the privileged seed path).
    private async Task SeedAsync(string superCs)
    {
        var tc = new MutableTenantContext(); // unresolved ⇒ no filter interference; TenantId set explicitly below.
        await using var db = OwnerAwareDb(superCs, tc);

        db.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = "tenant-a", Name = "Tenant A" });
        db.Tenants.Add(new Tenant { Id = _tenantB, Subdomain = "tenant-b", Name = "Tenant B" });

        SeedTenantEmployees(db, _tenantA, EmployeesA, "A");
        SeedTenantEmployees(db, _tenantB, EmployeesB, "B");

        // Two tenant-scoped roles + one system (NULL-tenant) role.
        db.Roles.Add(new Role { Id = _roleA, TenantId = _tenantA, Name = "A Role", IsBuiltIn = false });
        db.Roles.Add(new Role { Id = _roleB, TenantId = _tenantB, Name = "B Role", IsBuiltIn = false });
        db.Roles.Add(new Role { Id = _roleSystem, TenantId = null, Name = "System Role", IsBuiltIn = true });

        await db.SaveChangesAsync();
    }

    private static void SeedTenantEmployees(AppDbContext db, Guid tenantId, int count, string tag)
    {
        var deptId = Guid.NewGuid();
        var jobTitleId = Guid.NewGuid();
        db.Departments.Add(new Department
        {
            Id = deptId, TenantId = tenantId, Name = $"Dept {tag}", Code = $"D{tag}", IsActive = true, IsDeleted = false,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = jobTitleId, TenantId = tenantId, TitleName = $"Title {tag}", IsActive = true, IsDeleted = false,
        });
        for (var i = 0; i < count; i++)
        {
            db.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNo = $"{tag}-{i}",
                FirstName = tag, LastName = $"Emp{i}", Email = $"{tag}{i}@example.com",
                Status = EmployeeStatus.Active, DepartmentId = deptId, JobTitleId = jobTitleId,
            });
        }
    }
}
