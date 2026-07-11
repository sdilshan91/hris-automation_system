// ============================================================================
// US-PLT-002 — RLS increment 3a: end-to-end proof that the FLAG-GATED reconciler + the request-path GUC behavior
// work together on real Postgres, and that the flip is REVERSIBLE by config.
//
// Unlike RlsIsolationPostgresTests (2a) — which hand-runs `ENABLE + FORCE` to exercise the DORMANT policies — this
// suite drives the ACTUAL production reconciler (DbInitializer.ReconcileRowLevelSecurityAsync) to turn enforcement
// on and off, and the ACTUAL production request-path GUC mechanism (TenantGucConnectionInterceptor, ISSUE-277 — the
// replacement for the retired TenantTransactionBehavior) to set the GUC on a request. It proves the whole stack,
// not just the policies:
//   • Rls:Enabled=true  ⇒ the reconciler ENABLE/FORCEs every tenant table (pg_class flags flip).
//   • A request through TenantGucConnectionInterceptor on hrm_app is tenant-isolated (GUC set at connection open).
//   • The privileged (hrm_owner/BYPASSRLS) path spans tenants — startup/system/cross-tenant work keeps working.
//   • Rls:Enabled=false ⇒ the reconciler DISABLEs enforcement (pg_class flags clear) → hrm_app behaves pre-RLS
//     (sees all rows with no GUC) — the reversibility / rollback proof (critical R7).
//
// WHY REAL POSTGRES: RLS is a database-engine feature the EF InMemory provider does not implement. The container's
// default `postgres` user is a SUPERUSER (always bypasses RLS), so the app path is tested as the non-bypass
// `hrm_app` role provisioned from roles.sql; the reconciler DDL + seeding run as the BYPASSRLS `hrm_owner`.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Multitenancy;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-PLT-002-RLS")]
[Trait("Category", "RlsReconciler")]
public sealed class RlsReconcilerPostgresTests : IAsyncLifetime
{
    private const string AppRole = "hrm_app";
    private const string AppPassword = "app_pw_rls_3a";
    private const string OwnerRole = "hrm_owner";
    private const string OwnerPassword = "owner_pw_rls_3a";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private const int EmployeesA = 2;
    private const int EmployeesB = 3;

    private string _appConnString = null!;   // connects as hrm_app   (RLS ENFORCED once the reconciler ENABLEs)
    private string _ownerConnString = null!; // connects as hrm_owner (BYPASSRLS)

    private static IConfiguration RlsConfig(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Rls:Enabled"] = enabled ? "true" : "false" })
            .Build();

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

        var superCs = _postgres.GetConnectionString();
        _appConnString = WithRole(superCs, AppRole, AppPassword);
        _ownerConnString = WithRole(superCs, OwnerRole, OwnerPassword);

        // Provision the two production roles (mirrors roles.sql). hrm_owner is additionally granted CREATE on the
        // public schema so it can OWN the tables it migrates — the faithful production model where migrations run
        // as hrm_owner (ownership is what lets the reconciler's ALTER TABLE ... ENABLE ROW LEVEL SECURITY succeed).
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
             GRANT CREATE, USAGE ON SCHEMA public TO {OwnerRole};
             """);

        // Apply migrations (schema + DORMANT policies) as hrm_owner so it OWNS the resulting tables/policies —
        // exactly as in production, where the reconciler then runs on that owner connection.
        await using (var migrate = OwnerAwareDb(_ownerConnString, new MutableTenantContext()))
        {
            await migrate.Database.MigrateAsync();
        }

        await ExecAsync(superCs,
            $"""
             GRANT USAGE ON SCHEMA public TO {AppRole}, {OwnerRole};
             GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRole}, {OwnerRole};
             GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {AppRole}, {OwnerRole};
             """);

        // Seed as the superuser (bypasses RLS + WITH CHECK) — mirrors the privileged migration/seed path.
        await SeedAsync(superCs);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ─────────────────────────────────────────────────────────────────────────
    // ONE ordered end-to-end flow (single test so the ENABLE→prove→DISABLE→prove state transitions are
    // deterministic and never race a sibling test sharing this fixture's live database).
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Reconciler_EnablesEnforces_RequestIsIsolated_ThenDisables_Reversibly()
    {
        // ── (1) Reconciler ENABLE (Rls:Enabled=true), run on the PRIVILEGED (owner) connection as at startup. ──
        await using (var ownerDb = OwnerAwareDb(_ownerConnString, new MutableTenantContext()))
        {
            await DbInitializer.ReconcileRowLevelSecurityAsync(
                ownerDb, RlsConfig(enabled: true), NullLogger.Instance, CancellationToken.None);
        }

        // The reconciler ENABLED + FORCED RLS — pg_class flags reflect it on a representative tenant table.
        (await RowSecurityFlagsAsync("employees")).Should().Be((true, true),
            "the reconciler must ENABLE and FORCE row-level security on tenant tables when Rls:Enabled=true");

        // ── (2) Enforcement is real on hrm_app: raw SELECT with the GUC set sees only that tenant's rows. ──
        (await RawCountAsync(_appConnString, _tenantA, "employees")).Should().Be(EmployeesA);
        (await RawCountAsync(_appConnString, _tenantB, "employees")).Should().Be(EmployeesB);

        // hrm_app with NO GUC ⇒ fail-closed (0 rows) — the inverse of the EF filter's unresolved=see-all.
        (await RawCountAsync(_appConnString, guc: null, "employees")).Should().Be(0);

        // ── (3) A REQUEST-PATH read through the real TenantGucConnectionInterceptor on hrm_app is tenant-isolated
        //        end-to-end. The interceptor sets app.current_tenant at SESSION scope on every connection open
        //        (ISSUE-277 — a single tx-less set_config, replacing the retired per-request transaction), driven
        //        by the AmbientTenant. The read uses IgnoreQueryFilters() with an UNRESOLVED EF tenant context, so
        //        the interceptor-set GUC (RLS) is the SOLE isolation — the headline proof that the request path
        //        enforces tenant isolation at the database engine. ──
        AmbientTenant.SetTenant(_tenantA);
        try
        {
            await using var appDb = AppDbWithGucInterceptor(_appConnString, new MutableTenantContext());
            var seen = await appDb.Employees.IgnoreQueryFilters().CountAsync();
            seen.Should().Be(EmployeesA,
                "the interceptor set the GUC to tenant A on connection open, so RLS caps the query to A's rows");
        }
        finally
        {
            AmbientTenant.Clear();
        }

        // ── (4) The privileged (hrm_owner/BYPASSRLS) path spans tenants with no GUC — startup/system/cross-tenant
        //        work keeps functioning under enforcement. ──
        (await RawCountAsync(_ownerConnString, guc: null, "employees")).Should().Be(EmployeesA + EmployeesB);

        // ── (5) Reconciler DISABLE (Rls:Enabled=false) — the rollback path (R7). ──
        await using (var ownerDb = OwnerAwareDb(_ownerConnString, new MutableTenantContext()))
        {
            await DbInitializer.ReconcileRowLevelSecurityAsync(
                ownerDb, RlsConfig(enabled: false), NullLogger.Instance, CancellationToken.None);
        }

        // Enforcement flags cleared …
        (await RowSecurityFlagsAsync("employees")).Should().Be((false, false),
            "the reconciler must DISABLE + NO FORCE row-level security when Rls:Enabled=false (reversible rollback)");

        // … and hrm_app now behaves exactly as PRE-RLS: with no GUC it sees ALL rows (enforcement is off), so a
        // config rollback + restart leaves the app working while the interceptor is inert (Rls:Enabled=false).
        (await RawCountAsync(_appConnString, guc: null, "employees")).Should().Be(EmployeesA + EmployeesB,
            "with RLS disabled the app connection is unconstrained again — the flip is reversible by config");
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
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options, tc);

    // Same as OwnerAwareDb but WITH the production TenantGucConnectionInterceptor (Rls:Enabled=true) — it sets the
    // app.current_tenant GUC from the AmbientTenant on every connection open, the ISSUE-277 request-path mechanism.
    private AppDbContext AppDbWithGucInterceptor(string connString, ITenantContext tc) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connString, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(new TenantGucConnectionInterceptor(RlsConfig(enabled: true)))
            .Options, tc);

    // (relrowsecurity, relforcerowsecurity) for a table — the two pg_class flags the reconciler toggles.
    private async Task<(bool Enabled, bool Forced)> RowSecurityFlagsAsync(string table)
    {
        await using var conn = new NpgsqlConnection(_ownerConnString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE relname = @t", conn);
        cmd.Parameters.AddWithValue("t", table);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue($"table {table} should exist");
        return (reader.GetBoolean(0), reader.GetBoolean(1));
    }

    private static async Task SetGucAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid tenantId)
    {
        await using var cmd = new NpgsqlCommand("SELECT set_config('app.current_tenant', @t, true)", conn, tx);
        cmd.Parameters.AddWithValue("t", tenantId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> RawCountAsync(string connString, Guid? guc, string table)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        if (guc is { } g) await SetGucAsync(conn, tx, g);
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table}", conn, tx);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        await tx.CommitAsync();
        return count;
    }

    private async Task SeedAsync(string superCs)
    {
        var tc = new MutableTenantContext();
        await using var db = OwnerAwareDb(superCs, tc);

        db.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = "tenant-a", Name = "Tenant A" });
        db.Tenants.Add(new Tenant { Id = _tenantB, Subdomain = "tenant-b", Name = "Tenant B" });

        SeedTenantEmployees(db, _tenantA, EmployeesA, "A");
        SeedTenantEmployees(db, _tenantB, EmployeesB, "B");

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
