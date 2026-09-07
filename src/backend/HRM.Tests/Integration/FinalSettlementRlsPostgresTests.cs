// ============================================================================
// ISSUE-303 (gap 2) — the F&F settlement tables' RLS policies actually SEPARATE two tenants.
//
// WHAT WAS MISSING. FinalSettlementPostgresTests.DormantTenantIsolationPolicies_ExistForAllThreeTables
// asserted only that a `tenant_isolation` row EXISTS in pg_policies for tenant_fnf_policy /
// final_settlement / final_settlement_line. That is a claim about the CATALOG, not about behaviour: a
// policy whose USING clause names the wrong column, compares against the wrong GUC, or is simply
// `USING (true)` produces an identical pg_policies row and separates nothing. No `tenantB` identifier
// appeared anywhere in the three F&F test files, so the separation itself was never exercised.
//
// WHY A SEPARATE CLASS AND A SEPARATE CONTAINER. The shipped policies are DORMANT — the FnF migration
// (20260714155807_Payroll_FnFSettlement) creates them with no ENABLE ROW LEVEL SECURITY, and only
// DbInitializer.ReconcileRowLevelSecurityAsync (gated on Rls:Enabled) turns them on. Proving them
// requires hand-running that reconciler's DDL (ENABLE + FORCE) and provisioning the two production
// roles, which mutates the whole database — it cannot share a container with the plain-EF F&F suite.
// This mirrors the established harness in RlsIsolationPostgresTests / NotificationRlsPostgresTests.
//
// WHY THE hrm_app ROLE. A superuser — which is what Testcontainers hands you by default — ALWAYS
// bypasses RLS. Reading as `postgres` would return "0 rows of tenant A" only because the query said so,
// and would stay green with every policy dropped. The proof therefore connects as the NOBYPASSRLS
// `hrm_app` role with FORCE ROW LEVEL SECURITY set, i.e. the shape the application actually runs in
// once Rls:Enabled.
//
// THE CONTROL ARMS ARE LOAD-BEARING. "B sees none of A's rows" is also satisfied by a policy that denies
// EVERYTHING, and by a seed that silently inserted nothing. Both are ruled out here: the same connection
// under tenant A's GUC DOES see A's rows, and the BYPASSRLS owner sees both tenants' rows on disk.
//
// The EF-global-query-filter half of this control (the layer live on a default, RLS-off deployment) is
// proved by FinalSettlementPostgresTests.Settlements_AndTheirLines_AreTenantIsolated_AcrossContexts.
// ============================================================================

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

[Trait("TC", "TC-PAY-013-07")]
[Trait("Category", "RlsIsolation")]
public sealed class FinalSettlementRlsPostgresTests : IAsyncLifetime
{
    private const string AppRole = "hrm_app";
    private const string AppPassword = "app_pw_fnf_303";
    private const string OwnerRole = "hrm_owner";
    private const string OwnerPassword = "owner_pw_fnf_303";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private string _appConnString = null!;   // hrm_app   — RLS ENFORCED (NOBYPASSRLS)
    private string _ownerConnString = null!; // hrm_owner — BYPASSRLS

    private Guid _settlementA;
    private Guid _settlementB;

    /// <summary>Drives the EF global query filter + TenantInterceptor during the privileged seed only.</summary>
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

        // (1) Provision the two production roles as the superuser (mirrors roles.sql).
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

        // (2) Migrate as the superuser so the schema + the DORMANT tenant_isolation policies exist.
        await using (var migrate = OwnerAwareDb(superCs, new MutableTenantContext()))
        {
            await migrate.Database.MigrateAsync();
        }

        await ExecAsync(superCs,
            $"""
             GRANT USAGE ON SCHEMA public TO {AppRole}, {OwnerRole};
             GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRole}, {OwnerRole};
             GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {AppRole}, {OwnerRole};
             """);

        // (3) Hand-run the increment-3 reconciler's DDL: ENABLE + FORCE RLS on every tenant_id table. This
        //     is what DbInitializer.ReconcileRowLevelSecurityAsync does when Rls:Enabled — including on
        //     final_settlement and final_settlement_line, the tables under test.
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

        // (4) Seed both tenants privileged (superuser bypasses RLS + WITH CHECK), mirroring the seed path.
        await using var seed = OwnerAwareDb(superCs, new MutableTenantContext());
        seed.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = "fnf-tenant-a", Name = "Tenant A" });
        seed.Tenants.Add(new Tenant { Id = _tenantB, Subdomain = "fnf-tenant-b", Name = "Tenant B" });
        await seed.SaveChangesAsync();

        _settlementA = await SeedSettlementAsync(superCs, _tenantA, 11_111.11m);
        _settlementB = await SeedSettlementAsync(superCs, _tenantB, 22_222.22m);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>
    /// One ordered fact rather than several, deliberately: every arm shares the same enabled-RLS database and
    /// xUnit builds a NEW class instance (hence a new ~20s container + full migration replay) per <c>[Fact]</c>.
    /// Splitting this would multiply the gate's cost without adding a single assertion.
    /// </summary>
    [Fact]
    public async Task FnFSettlementTables_SeparateTwoTenants_UnderEnforcedRls()
    {
        // ── Vacuity guard 0: the policies are present AND now actually enabled+forced on the tables. A
        //    proof run against tables with RLS still dormant would be worthless.
        foreach (var table in new[] { "final_settlement", "final_settlement_line" })
        {
            (await HasTenantIsolationPolicyAsync(table)).Should().BeTrue(
                $"{table} must carry the tenant_isolation policy, or this whole proof is vacuous");
            (await IsRlsEnforcedAsync(table)).Should().BeTrue(
                $"{table} must have RLS ENABLED and FORCED, or hrm_app would read it unrestricted");
        }

        // ── Vacuity guard 1: both tenants' rows are physically on disk (read as the BYPASSRLS owner). So a
        //    later "0 rows" can only be RLS filtering, never a failed seed.
        (await OwnerCountAsync("final_settlement", $"id IN ('{_settlementA}', '{_settlementB}')"))
            .Should().Be(2, "both settlements were seeded");
        (await OwnerCountAsync("final_settlement_line", $"final_settlement_id IN ('{_settlementA}', '{_settlementB}')"))
            .Should().Be(2, "both settlement lines were seeded");

        // ── THE CONTROL UNDER TEST: as hrm_app under TENANT B's GUC, tenant A is invisible. ──
        (await AppCountAsync(_tenantB, "final_settlement", $"id = '{_settlementA}'"))
            .Should().Be(0, "tenant B must not be able to read tenant A's final settlement");
        (await AppCountAsync(_tenantB, "final_settlement_line", $"final_settlement_id = '{_settlementA}'"))
            .Should().Be(0, "tenant B must not be able to read the LINES of tenant A's settlement — the line "
                            + "table carries the money figures and is policied independently of its parent");

        // Not merely "A is hidden": B's UNQUALIFIED read returns ONLY B's rows. An unfiltered SELECT is the
        // shape a leak actually takes (a report, an export, a COUNT), so assert the whole visible set.
        (await AppIdsAsync(_tenantB, "SELECT id FROM final_settlement"))
            .Should().BeEquivalentTo(new[] { _settlementB },
                "an unqualified read as tenant B returns exactly B's settlement");
        (await AppIdsAsync(_tenantB, "SELECT final_settlement_id FROM final_settlement_line"))
            .Should().BeEquivalentTo(new[] { _settlementB }, "…and exactly B's settlement line");
        (await AppScalarDecimalAsync(_tenantB, "SELECT amount FROM final_settlement_line"))
            .Should().Be(22_222.22m, "B sees its own money figure, not A's 11,111.11");

        // ── Vacuity guard 2 (the one that matters most): the SAME connection, same query, tenant A's GUC —
        //    A's rows ARE visible. This is what distinguishes "the policy separates tenants" from "the
        //    policy denies everything", which would satisfy every assertion above.
        (await AppCountAsync(_tenantA, "final_settlement", $"id = '{_settlementA}'"))
            .Should().Be(1, "tenant A can read its OWN settlement — otherwise the policy is a blanket deny "
                            + "and the isolation assertions above prove nothing");
        (await AppIdsAsync(_tenantA, "SELECT id FROM final_settlement"))
            .Should().BeEquivalentTo(new[] { _settlementA }, "and symmetrically, A sees only A");
        (await AppScalarDecimalAsync(_tenantA, "SELECT amount FROM final_settlement_line"))
            .Should().Be(11_111.11m);

        // ── And the policy is not merely hiding A from B: with NO tenant GUC set at all, the app role sees
        //    NOTHING (fail-closed). An unset GUC must never mean "all tenants".
        (await AppCountNoGucAsync("final_settlement", "true"))
            .Should().Be(0, "an unresolved tenant must read zero settlements, not every tenant's");
        (await AppCountNoGucAsync("final_settlement_line", "true"))
            .Should().Be(0, "an unresolved tenant must read zero settlement lines, not every tenant's");
    }

    // ── seeding ──────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds one settlement + one line for <paramref name="tenantId"/> through a context whose tenant is that
    /// tenant, so TenantInterceptor stamps TenantId exactly as production does.
    /// </summary>
    private static async Task<Guid> SeedSettlementAsync(string superCs, Guid tenantId, decimal lineAmount)
    {
        await using var db = OwnerAwareDb(superCs, new MutableTenantContext { TenantId = tenantId });

        var settlement = new FinalSettlement
        {
            Id = BaseEntity.NewUuidV7(),
            EmployeeId = Guid.NewGuid(),
            OffboardingInstanceId = Guid.NewGuid(),
            LastWorkingDay = new DateOnly(2026, 6, 15),
            FiscalYear = string.Empty,
            ProRatedGross = lineAmount,
            NetPayable = lineAmount,
            PolicyEffectiveFrom = new DateOnly(2026, 1, 1),
            FinalPeriodOwnedBySettlement = true,
            ComputedAtUtc = DateTime.UtcNow,
            Status = FinalSettlementStatus.Computed,
        };
        settlement.Lines.Add(new FinalSettlementLine
        {
            Id = BaseEntity.NewUuidV7(),
            Label = "Basic Salary",
            Amount = lineAmount,
            Type = FinalSettlementLineType.Earning,
        });

        db.FinalSettlements.Add(settlement);
        await db.SaveChangesAsync();
        return settlement.Id;
    }

    // ── harness helpers (mirror NotificationRlsPostgresTests / RlsIsolationPostgresTests) ──

    private static string WithRole(string baseConnString, string user, string password) =>
        new NpgsqlConnectionStringBuilder(baseConnString) { Username = user, Password = password }.ToString();

    private static async Task ExecAsync(string connString, string sql)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static AppDbContext OwnerAwareDb(string connString, ITenantContext tc) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connString, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(new HRM.Infrastructure.Persistence.Interceptors.TenantInterceptor(tc))
            .Options, tc);

    private async Task<bool> HasTenantIsolationPolicyAsync(string table)
    {
        await using var conn = new NpgsqlConnection(_ownerConnString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM pg_policies WHERE schemaname = 'public' "
            + "AND policyname = 'tenant_isolation' AND tablename = @t", conn);
        cmd.Parameters.AddWithValue("t", table);
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
    }

    /// <summary>True only when the table has RLS both ENABLED and FORCED — a policy on a table with RLS off
    /// is inert, which is precisely the dormant state this suite must not silently run against.</summary>
    private async Task<bool> IsRlsEnforcedAsync(string table)
    {
        await using var conn = new NpgsqlConnection(_ownerConnString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class "
            + "WHERE oid = format('public.%I', @t)::regclass", conn);
        cmd.Parameters.AddWithValue("t", table);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Count read as the BYPASSRLS owner — "what is physically on disk", unaffected by any policy.</summary>
    private async Task<long> OwnerCountAsync(string table, string predicate)
    {
        await using var conn = new NpgsqlConnection(_ownerConnString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE {predicate}", conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Count read as the NOBYPASSRLS app role with <paramref name="guc"/> as the current tenant.</summary>
    private async Task<long> AppCountAsync(Guid guc, string table, string predicate)
    {
        await using var conn = new NpgsqlConnection(_appConnString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantGucAsync(conn, tx, guc);
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE {predicate}", conn, tx);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        await tx.CommitAsync();
        return count;
    }

    /// <summary>Count read as the app role with NO tenant GUC set at all (the fail-closed case).</summary>
    private async Task<long> AppCountNoGucAsync(string table, string predicate)
    {
        await using var conn = new NpgsqlConnection(_appConnString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE {predicate}", conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Every Guid an UNQUALIFIED select returns as the app role under <paramref name="guc"/>.</summary>
    private async Task<List<Guid>> AppIdsAsync(Guid guc, string sql)
    {
        await using var conn = new NpgsqlConnection(_appConnString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantGucAsync(conn, tx, guc);

        var ids = new List<Guid>();
        await using (var cmd = new NpgsqlCommand(sql, conn, tx))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        }

        await tx.CommitAsync();
        return ids;
    }

    private async Task<decimal?> AppScalarDecimalAsync(Guid guc, string sql)
    {
        await using var conn = new NpgsqlConnection(_appConnString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantGucAsync(conn, tx, guc);
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        var value = await cmd.ExecuteScalarAsync();
        await tx.CommitAsync();
        return value as decimal?;
    }

    private static async Task SetTenantGucAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid tenantId)
    {
        await using var set = new NpgsqlCommand("SELECT set_config('app.current_tenant', @t, true)", conn, tx);
        set.Parameters.AddWithValue("t", tenantId.ToString());
        await set.ExecuteNonQueryAsync();
    }
}
