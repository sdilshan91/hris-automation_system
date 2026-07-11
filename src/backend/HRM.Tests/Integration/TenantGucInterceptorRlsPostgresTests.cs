// ============================================================================
// US-PLT-002 (ISSUE-277) — the pipeline-under-retry RLS-on proof that the existing RLS suites OMITTED, and whose
// omission hid ISSUE-277. TenantGucConnectionInterceptor sets the app.current_tenant GUC at SESSION scope on every
// connection open — a single tx-less set_config — replacing the retired TenantTransactionBehavior (whose per-request
// transaction threw "does not support user-initiated transactions" under EnableRetryOnFailure and nested with the
// handlers that open their own transaction).
//
// THE GAP THIS CLOSES: RlsIsolationPostgresTests / RlsReconcilerPostgresTests build the AppDbContext WITHOUT
// EnableRetryOnFailure, so they never exercised the retry-strategy conflict that broke production. This suite builds
// the DbContext on hrm_app WITH BOTH EnableRetryOnFailure(3) AND the interceptor — the actual production shape — and
// proves:
//   (1) Retry-strategy face: a plain tenant-scoped query SUCCEEDS (the interceptor sets the GUC with no tx, so the
//       retrying execution strategy is happy) and returns only the tenant's rows.
//   (2) Own-tx face: a CreateExecutionStrategy().ExecuteAsync(BeginTransaction + tenant INSERT + read) unit — the
//       pattern the ~5 own-tx handlers use — SUCCEEDS (no request-wide tx wraps it), and the INSERT lands
//       tenant-scoped (WITH CHECK satisfied because the GUC is set on that connection).
//   (3) Isolation: a second tenant on a fresh open sees only ITS rows — the GUC is re-set per connection open,
//       never leaked from the first tenant.
//
// The own-tx unit (CreateExecutionStrategy + BeginTransactionAsync) is a faithful proxy for the real own-tx handlers
// (TrainingService.EnrollAsync, WorkflowRuntimeService, ApplicantConversionService, …) — same EF mechanism, without
// dragging the full service graph into the test.
//
// WHY REAL POSTGRES: RLS is a database-engine feature the EF InMemory provider does not implement; the container's
// default `postgres` user is a SUPERUSER (always bypasses RLS), so the app path is tested as the non-bypass hrm_app
// role while migrations/seeding run as the BYPASSRLS hrm_owner.
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
using Npgsql;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-PLT-002-RLS")]
[Trait("Category", "RlsGucInterceptor")]
public sealed class TenantGucInterceptorRlsPostgresTests : IAsyncLifetime
{
    private const string AppRole = "hrm_app";
    private const string AppPassword = "app_pw_rls_277";
    private const string OwnerRole = "hrm_owner";
    private const string OwnerPassword = "owner_pw_rls_277";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private const int EmployeesA = 2;
    private const int EmployeesB = 3;

    private string _appConnString = null!;   // connects as hrm_app   (RLS ENFORCED)
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

        // Provision the two production roles (mirrors roles.sql; the app never runs role DDL).
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

        // Apply migrations (schema + DORMANT policies) as the superuser/owner.
        await using (var migrate = OwnerAwareDb(superCs, new MutableTenantContext()))
        {
            await migrate.Database.MigrateAsync();
        }

        // Grants for both roles (owner has BYPASSRLS but still needs table privileges).
        await ExecAsync(superCs,
            $"""
             GRANT USAGE ON SCHEMA public TO {AppRole}, {OwnerRole};
             GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRole}, {OwnerRole};
             GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {AppRole}, {OwnerRole};
             """);

        // Simulate the increment-3 reconciler: ENABLE + FORCE RLS on every tenant_id table (excl. users/tenants).
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

        // Seed as the superuser (bypasses RLS + WITH CHECK) — mirrors the privileged migration/seed path.
        await SeedAsync(superCs);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ─────────────────────────────────────────────────────────────────────────
    // ONE ordered end-to-end flow (single test so the AmbientTenant transitions + the tenant-B insert never race a
    // sibling test sharing this fixture's live database).
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Interceptor_UnderRetryStrategy_SetsGuc_SupportsOwnTx_AndIsolatesPerOpen()
    {
        // ── (1) RETRY-STRATEGY FACE — a plain tenant-scoped query on the retrying DbContext SUCCEEDS. Under the old
        //        TenantTransactionBehavior design this threw "does not support user-initiated transactions"; the
        //        interceptor sets the GUC on connection open with NO transaction, so the retry strategy is happy. ──
        AmbientTenant.SetTenant(_tenantA);
        try
        {
            await using var appDb = AppDbWithRetryAndGuc(_appConnString, new MutableTenantContext { TenantId = _tenantA });

            var countA = await appDb.Employees.CountAsync();
            countA.Should().Be(EmployeesA, "the interceptor set the GUC to A, so the tenant-scoped query returns A's rows");

            // ── (2) OWN-TX FACE — the pattern the ~5 own-tx handlers use: CreateExecutionStrategy().ExecuteAsync
            //        wrapping BeginTransactionAsync + a tenant-scoped INSERT + read. Under the old design a
            //        request-wide tx already held the connection → "connection is already in a transaction"; now
            //        nothing wraps it, and the INSERT satisfies WITH CHECK because the GUC is set on this connection. ──
            var newDeptId = Guid.NewGuid();
            var strategy = appDb.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await appDb.Database.BeginTransactionAsync();
                appDb.Departments.Add(new Department
                {
                    Id = newDeptId, TenantId = _tenantA, Name = "Own-Tx Dept", Code = "OTX",
                    IsActive = true, IsDeleted = false,
                });
                await appDb.SaveChangesAsync();
                await tx.CommitAsync();
            });

            var landed = await appDb.Departments.IgnoreQueryFilters().AnyAsync(d => d.Id == newDeptId);
            landed.Should().BeTrue("the own-tx INSERT landed tenant-scoped — the GUC on the connection satisfied WITH CHECK");
        }
        finally
        {
            AmbientTenant.Clear();
        }

        // ── (3) ISOLATION — a SECOND tenant on a fresh connection open sees only its own rows. Proves the GUC is
        //        re-set per open (never leaked from tenant A). Unresolved EF context + IgnoreQueryFilters ⇒ RLS
        //        (the interceptor-set GUC) is the SOLE isolator. ──
        AmbientTenant.SetTenant(_tenantB);
        try
        {
            await using var appDb = AppDbWithRetryAndGuc(_appConnString, new MutableTenantContext());
            var countB = await appDb.Employees.IgnoreQueryFilters().CountAsync();
            countB.Should().Be(EmployeesB, "a fresh open re-set the GUC to B, so RLS caps the query to B's rows (no A leak)");
        }
        finally
        {
            AmbientTenant.Clear();
        }
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

    // Owner/superuser DbContext for migrate + seed — no retry strategy, no GUC interceptor (BYPASSRLS path).
    private AppDbContext OwnerAwareDb(string connString, ITenantContext tc) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connString, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options, tc);

    // THE PRODUCTION SHAPE the other RLS suites omit: EnableRetryOnFailure(3) + the TenantGucConnectionInterceptor.
    private AppDbContext AppDbWithRetryAndGuc(string connString, ITenantContext tc) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connString, n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(new TenantGucConnectionInterceptor(RlsConfig(enabled: true)))
            .Options, tc);

    private async Task SeedAsync(string superCs)
    {
        await using var db = OwnerAwareDb(superCs, new MutableTenantContext());

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
