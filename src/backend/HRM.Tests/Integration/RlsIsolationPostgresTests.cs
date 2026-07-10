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
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
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
        await ExecAsync(superCs,
            $"""
             GRANT USAGE ON SCHEMA public TO {AppRole}, {OwnerRole};
             GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRole}, {OwnerRole};
             GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {AppRole}, {OwnerRole};
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

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string WithRole(string baseConnString, string user, string password) =>
        new NpgsqlConnectionStringBuilder(baseConnString) { Username = user, Password = password }.ToString();

    private static async Task ExecAsync(string connString, string sql)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
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
