// ============================================================================
// Per-tenant API-call counter — the WRITE side, on real Postgres.
//
// Written after the implementing agent was killed mid-task having covered only the GAUGE (the read side).
// That left the hardest part of the slice untested: the concurrent upsert. Discovered by mutating
// `call_count = tenant_api_usage.call_count + EXCLUDED.call_count` to last-write-wins and finding NOTHING
// failed — because no concurrency arm existed at all.
//
// These arms MUST run on real Postgres. The whole correctness argument is `INSERT … ON CONFLICT … DO UPDATE
// SET call_count = existing + excluded`, which is a Postgres upsert primitive: the EF InMemory provider does
// not implement ON CONFLICT, does not enforce the unique constraint the conflict target relies on, and cannot
// exhibit the lost-update race being guarded against. An InMemory version of this file would pass against a
// read-modify-write implementation that silently loses counts under load — the exact bug.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-PLT-004")]
[Trait("Category", "Monitoring")]
public sealed class TenantApiCallUsagePostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private string _cs = null!;

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _cs = _postgres.GetConnectionString() + ";Include Error Detail=true";
        await using var db = Db();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── THE arm the slice was missing ─────────────────────────────────────────
    // Many flushes hitting the SAME (tenant, month) row concurrently must sum EXACTLY. A read-modify-write
    // implementation passes a serial test and loses counts here; that is the whole point of running it parallel.
    [Fact]
    public async Task Concurrent_upserts_to_the_same_bucket_sum_exactly_and_lose_nothing()
    {
        var month = TenantApiUsage.ToYearMonth(DateTime.UtcNow);
        const int flushes = 20;
        const long perFlush = 50L;

        // Each task uses its OWN DbContext — a shared one is not thread-safe and would make this test about
        // EF's concurrency guard rather than about the SQL upsert.
        var tasks = Enumerable.Range(0, flushes).Select(async _ =>
        {
            await using var db = Db();
            await TenantApiCallUsage.UpsertAsync(
                db, [new ApiCallCountDelta(_tenantA, month, perFlush)], DateTime.UtcNow);
        });

        await Task.WhenAll(tasks);

        (await CountOf(_tenantA, month)).Should().Be(flushes * perFlush,
            "each concurrent flush must ADD to the existing total. Under a last-write-wins upsert "
            + $"(call_count = EXCLUDED.call_count) this lands on {perFlush} instead of {flushes * perFlush}, "
            + "silently under-reporting every tenant's usage in exact proportion to how busy they are");
    }

    // ── tenant isolation — non-negotiable in this codebase ────────────────────
    [Fact]
    public async Task One_tenants_calls_never_land_in_another_tenants_bucket()
    {
        var month = TenantApiUsage.ToYearMonth(DateTime.UtcNow);

        await using (var db = Db())
        {
            await TenantApiCallUsage.UpsertAsync(db, [
                new ApiCallCountDelta(_tenantA, month, 7L),
                new ApiCallCountDelta(_tenantB, month, 3L),
            ], DateTime.UtcNow);
        }

        (await CountOf(_tenantA, month)).Should().Be(7L);
        (await CountOf(_tenantB, month)).Should().Be(3L,
            "a cross-tenant leak here would bill one tenant for another's traffic");
    }

    // ── month bucketing ───────────────────────────────────────────────────────
    // The gauge reports MONTH-TO-DATE against a monthly plan limit, so a prior month's calls must occupy a
    // separate row. If they collapsed into one bucket, usage would ratchet up forever and every long-lived
    // tenant would eventually appear over quota.
    [Fact]
    public async Task Different_months_accumulate_in_separate_buckets()
    {
        var thisMonth = TenantApiUsage.ToYearMonth(DateTime.UtcNow);
        var lastMonth = TenantApiUsage.ToYearMonth(DateTime.UtcNow.AddMonths(-1));
        thisMonth.Should().NotBe(lastMonth, "the fixture is only meaningful if the two keys differ");

        await using (var db = Db())
        {
            await TenantApiCallUsage.UpsertAsync(db, [
                new ApiCallCountDelta(_tenantA, thisMonth, 11L),
                new ApiCallCountDelta(_tenantA, lastMonth, 99L),
            ], DateTime.UtcNow);
        }

        (await CountOf(_tenantA, thisMonth)).Should().Be(11L,
            "last month's 99 calls must not inflate this month's usage against a MONTHLY limit");
        (await CountOf(_tenantA, lastMonth)).Should().Be(99L);
    }

    // ── repeated flushes accumulate rather than overwrite ─────────────────────
    [Fact]
    public async Task Sequential_flushes_accumulate_into_the_same_bucket()
    {
        var month = TenantApiUsage.ToYearMonth(DateTime.UtcNow);

        for (int i = 0; i < 3; i++)
        {
            await using var db = Db();
            await TenantApiCallUsage.UpsertAsync(db, [new ApiCallCountDelta(_tenantA, month, 5L)], DateTime.UtcNow);
        }

        (await CountOf(_tenantA, month)).Should().Be(15L, "three flushes of 5 are 15, not 5");
    }

    // ── a zero delta must not create a spurious row ───────────────────────────
    // An idle tenant should have no bucket at all rather than a 0-count row, so "no usage" and "usage of zero"
    // stay distinguishable in the gauge.
    [Fact]
    public async Task A_zero_delta_writes_nothing()
    {
        var month = TenantApiUsage.ToYearMonth(DateTime.UtcNow);

        await using (var db = Db())
        {
            await TenantApiCallUsage.UpsertAsync(db, [new ApiCallCountDelta(_tenantB, month, 0L)], DateTime.UtcNow);
        }

        await using var check = Db();
        (await check.TenantApiUsages.IgnoreQueryFilters()
            .AnyAsync(u => u.TenantId == _tenantB && u.YearMonth == month))
            .Should().BeFalse("an idle tenant must not get a row; absent and zero must stay distinguishable");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<long> CountOf(Guid tenantId, int yearMonth)
    {
        await using var db = Db();
        // IgnoreQueryFilters: this harness runs with a system (unresolved) tenant context, and the assertion is
        // about the stored row itself, not about filter behaviour.
        return await db.TenantApiUsages.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && u.YearMonth == yearMonth)
            .Select(u => u.CallCount)
            .FirstOrDefaultAsync();
    }

    private AppDbContext Db() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_cs, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options,
        new SystemTenantContext());

    private sealed class SystemTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public string Subdomain => "admin";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => true;
        public bool IsResolved => false;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status, string? plan = null,
            IReadOnlyCollection<string>? enabledModules = null, string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }
}
