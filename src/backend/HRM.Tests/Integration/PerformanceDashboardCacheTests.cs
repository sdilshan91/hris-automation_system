// ============================================================================
// ISSUE-129 (US-PRF-007 / NFR-3): Redis read-through cache over the performance dashboard aggregates.
//
// Two things are under test, and only one of them is about speed:
//
//   1. The read-through actually reads through — a second identical request is served from the cache
//      rather than recomputed. Asserted by OBSERVABLE STALENESS (the DB changes under the cache and the
//      cached answer does not move), not by timing, because a timing assertion is flaky and proves nothing
//      about which code path ran.
//
//   2. The cache key isolates. This is the part that turns a perf fix into a data leak if it is wrong,
//      so it is tested three ways, each pinning a DIFFERENT segment of the key:
//        - two TENANTS never share an entry           (the CacheTenantPrefix segment)
//        - an HR viewer and a manager never share one (the scope KIND segment)
//        - two managers with different teams never share one (the scope RESTRICT-SET segment)
//      The last two run with tenant, cycle and filter all held IDENTICAL, so the scope segment is the
//      only thing that can separate them — which is what makes them go red when it is removed.
//
// NOTE ON THE CACHE: these tests inject a REAL IDistributedCache (the in-memory implementation, which is
// also the production fallback when no Redis connection string is configured) and deliberately SHARE ONE
// INSTANCE across the callers being compared. A per-caller cache would make every isolation assertion pass
// vacuously. RecordingCache wraps it to expose the keys, so the tenant segment can be asserted structurally.
//
// NOTE ON PROVIDER: same rationale as PerformanceDashboardIntegrationTests — the verify gate runs with no
// PostgreSQL bound and no Docker, so these use the InMemory provider through the real composed MediatR
// pipeline (which is what carries the tenant query filters and the scope resolution).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Performance.DTOs;
using HRM.Application.Features.Performance.Queries;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HRM.Tests.Integration;

public sealed class PerformanceDashboardCacheTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private readonly Guid _deptA = Guid.NewGuid();
    private readonly Guid _deptB = Guid.NewGuid();
    private readonly Guid _cycleA = Guid.NewGuid();
    private readonly Guid _cycleB = Guid.NewGuid();

    // Tenant A: two managers, one direct report each. Both hold Performance.View.Team, so both resolve to
    // scope KIND = Team and differ ONLY in their restricted employee-id set.
    private readonly Guid _mgr1UserId = Guid.NewGuid();
    private readonly Guid _mgr1EmpId = Guid.NewGuid();
    private readonly Guid _mgr1ReportId = Guid.NewGuid();   // scores 4.0

    private readonly Guid _mgr2UserId = Guid.NewGuid();
    private readonly Guid _mgr2EmpId = Guid.NewGuid();
    private readonly Guid _mgr2ReportId = Guid.NewGuid();   // scores 2.0

    private readonly Guid _tenantBEmpId = Guid.NewGuid();   // scores 5.0

    public PerformanceDashboardCacheTests() => Seed();

    // ── Test doubles ────────────────────────────────────────────────────────

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "test";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid UserId { get; init; }
        public string Email => "hr@t.com";
        public Guid TenantId { get; init; }
        public Guid UserTenantId => TenantId;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions { get; init; } = [];
        public bool IsAuthenticated => true;
        public bool IsImpersonating => false;
        public Guid? ImpersonatorId => null;
        public Guid? ImpersonationSessionId => null;
        public bool ImpersonationReadOnly => false;
    }

    private sealed class NoopFileStorage : IFileStorage
    {
        public Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType,
            CancellationToken cancellationToken = default) => Task.FromResult(relativePath);
        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath,
            CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null) => string.Empty;
        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>
    /// A real (in-memory) distributed cache that records the keys it is asked for. The recording is what lets
    /// the tenant-isolation test assert on key STRUCTURE: two tenants issuing byte-identical requests produce
    /// GUID-bearing payloads that would incidentally differ anyway, so a purely behavioural assertion would
    /// still pass with the tenant segment deleted. Asserting the segment is present is what actually pins it.
    /// </summary>
    private sealed class RecordingCache : IDistributedCache
    {
        private readonly IDistributedCache _inner;
        public List<string> ReadKeys { get; } = [];
        public List<string> WrittenKeys { get; } = [];

        public RecordingCache()
        {
            var sc = new ServiceCollection();
            sc.AddDistributedMemoryCache();
            _inner = sc.BuildServiceProvider().GetRequiredService<IDistributedCache>();
        }

        public byte[]? Get(string key) { ReadKeys.Add(key); return _inner.Get(key); }

        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            ReadKeys.Add(key);
            return await _inner.GetAsync(key, token);
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            WrittenKeys.Add(key);
            _inner.Set(key, value, options);
        }

        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            WrittenKeys.Add(key);
            await _inner.SetAsync(key, value, options, token);
        }

        public void Refresh(string key) => _inner.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);
        public void Remove(string key) => _inner.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(key, token);
    }

    // ── Pipeline composition ────────────────────────────────────────────────

    private IMediator BuildPipeline(Guid tenantId, ICurrentUser user, IDistributedCache cache)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(new MutableTenantContext { TenantId = tenantId });
        services.AddSingleton(user);
        services.AddSingleton<IFileStorage, NoopFileStorage>();
        services.AddSingleton(cache);   // SHARED across callers — the leak vector under test.
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IPerformanceDashboardService, PerformanceDashboardService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(PerformanceDashboardOverviewQuery).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private static ICurrentUser Hr(Guid tenantId) => new FakeCurrentUser
    {
        UserId = Guid.NewGuid(), TenantId = tenantId,
        Permissions = new[] { PermissionCatalog.Performance.ViewAll },
    };

    private static ICurrentUser Manager(Guid tenantId, Guid userId) => new FakeCurrentUser
    {
        UserId = userId, TenantId = tenantId,
        Permissions = new[] { PermissionCatalog.Performance.ViewTeam },
    };

    private AppDbContext Db(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, new MutableTenantContext { TenantId = tenantId });
    }

    private static PerformanceDashboardFilter Filter(Guid cycleId) => new() { CycleId = cycleId };

    private void Seed()
    {
        var now = DateTime.UtcNow;

        using (var db = Db(_tenantA))
        {
            db.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = "acme", Name = "Acme" });
            db.Departments.Add(new Department { Id = _deptA, TenantId = _tenantA, Name = "Engineering" });

            db.Employees.Add(NewEmployee(_mgr1EmpId, _tenantA, _deptA, "A-MGR1", "Grace", userId: _mgr1UserId));
            db.Employees.Add(NewEmployee(_mgr1ReportId, _tenantA, _deptA, "A-RPT1", "Ada", reportsTo: _mgr1EmpId));
            db.Employees.Add(NewEmployee(_mgr2EmpId, _tenantA, _deptA, "A-MGR2", "Alan", userId: _mgr2UserId));
            db.Employees.Add(NewEmployee(_mgr2ReportId, _tenantA, _deptA, "A-RPT2", "Edsger", reportsTo: _mgr2EmpId));

            db.AppraisalCycles.Add(new AppraisalCycle
            {
                Id = _cycleA, TenantId = _tenantA, Name = "A-FY2026", Status = AppraisalCycleStatus.Active,
                Type = CycleType.Annual, StartDate = now.AddDays(-90), EndDate = now.AddDays(10),
                RatingScaleMax = 5,
            });

            // Only the two REPORTS are scored: manager 1's report 4.0, manager 2's report 2.0.
            // => Organization scope averages 3.0 over 2; manager 1 sees 4.0 over 1; manager 2 sees 2.0 over 1.
            db.ManagerReviews.Add(NewReview(_tenantA, _cycleA, _mgr1ReportId, 4.0m, now));
            db.ManagerReviews.Add(NewReview(_tenantA, _cycleA, _mgr2ReportId, 2.0m, now));

            db.SaveChanges();
        }

        using (var db = Db(_tenantB))
        {
            db.Tenants.Add(new Tenant { Id = _tenantB, Subdomain = "globex", Name = "Globex" });
            db.Departments.Add(new Department { Id = _deptB, TenantId = _tenantB, Name = "Ops" });
            db.Employees.Add(NewEmployee(_tenantBEmpId, _tenantB, _deptB, "B-1", "Bee"));

            db.AppraisalCycles.Add(new AppraisalCycle
            {
                Id = _cycleB, TenantId = _tenantB, Name = "B-FY2026", Status = AppraisalCycleStatus.Active,
                Type = CycleType.Annual, StartDate = now.AddDays(-90), EndDate = now.AddDays(10),
                RatingScaleMax = 5,
            });
            db.ManagerReviews.Add(NewReview(_tenantB, _cycleB, _tenantBEmpId, 5.0m, now));

            db.SaveChanges();
        }
    }

    private static Employee NewEmployee(
        Guid id, Guid tenantId, Guid deptId, string no, string firstName,
        Guid? userId = null, Guid? reportsTo = null) => new()
        {
            Id = id, TenantId = tenantId, UserId = userId, EmployeeNo = no,
            FirstName = firstName, LastName = "T", Email = $"{no}@t.com", DepartmentId = deptId,
            ReportsToEmployeeId = reportsTo, Status = EmployeeStatus.Active, IsActive = true,
            EmploymentType = EmploymentType.FullTime,
        };

    private static ManagerReview NewReview(Guid tenantId, Guid cycleId, Guid employeeId, decimal score, DateTime at)
        => new()
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, CycleId = cycleId, EmployeeId = employeeId,
            Status = ManagerReviewStatus.Submitted, FinalScore = score, SubmittedAt = at,
        };

    // ════════════════════════════════════════════════════════════════════════
    //  1. The read-through actually reads through (MISS then HIT).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task Overview_second_identical_request_is_served_from_cache_Issue129()
    {
        var cache = new RecordingCache();
        var mediator = BuildPipeline(_tenantA, Hr(_tenantA), cache);
        var query = new PerformanceDashboardOverviewQuery(Filter(_cycleA));

        // MISS: computed live off the DB (4.0 + 2.0) / 2.
        var first = (await mediator.Send(query)).Value!;
        first.AverageScore.Should().Be(3.0m);
        first.ScoredEmployeeCount.Should().Be(2);
        cache.WrittenKeys.Should().ContainSingle("the miss must populate exactly one entry");

        // Change the underlying data so a RECOMPUTE would give a different answer: manager 1's own record
        // is now scored 1.0, which drops the org-wide average to (4.0 + 2.0 + 1.0) / 3 = 2.33.
        using (var db = Db(_tenantA))
        {
            db.ManagerReviews.Add(NewReview(_tenantA, _cycleA, _mgr1EmpId, 1.0m, DateTime.UtcNow));
            db.SaveChanges();
        }

        // HIT: same key, so the pre-change aggregate comes back untouched. WITHOUT the read-through this
        // returns the recomputed 2.33 and the assertion fails — that is what makes this test fail without
        // the fix rather than merely pass faster with it.
        var second = (await mediator.Send(query)).Value!;
        second.AverageScore.Should().Be(3.0m);
        second.ScoredEmployeeCount.Should().Be(2);
        cache.WrittenKeys.Should().ContainSingle("a hit must not rewrite the entry");

        // Control: a cold cache over the SAME data recomputes and does see the new review, proving the
        // staleness above came from the cache and not from a broken/frozen query.
        var cold = BuildPipeline(_tenantA, Hr(_tenantA), new RecordingCache());
        var recomputed = (await cold.Send(query)).Value!;
        recomputed.ScoredEmployeeCount.Should().Be(3);
        recomputed.AverageScore.Should().Be(2.33m);
    }

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task Trend_second_identical_request_is_served_from_cache_Issue129()
    {
        // The trend is the N+1 path: LoadPopulationAsync (6-7 queries) once PER CYCLE.
        var cache = new RecordingCache();
        var mediator = BuildPipeline(_tenantA, Hr(_tenantA), cache);
        var query = new PerformanceTrendQuery([_cycleA], Filter(_cycleA), IncludeDepartmentSeries: false);

        var first = (await mediator.Send(query)).Value!;
        first.Points.Should().ContainSingle();
        first.Points[0].AverageScore.Should().Be(3.0m);
        cache.WrittenKeys.Should().ContainSingle();

        using (var db = Db(_tenantA))
        {
            db.ManagerReviews.Add(NewReview(_tenantA, _cycleA, _mgr1EmpId, 1.0m, DateTime.UtcNow));
            db.SaveChanges();
        }

        var second = (await mediator.Send(query)).Value!;
        second.Points[0].AverageScore.Should().Be(3.0m, "the second identical trend request is a cache hit");
        second.Points[0].ScoredEmployeeCount.Should().Be(2);
        cache.WrittenKeys.Should().ContainSingle();

        // The department-overlay variant is a DIFFERENT payload shape and must not be served from the
        // no-overlay entry.
        var withDept = new PerformanceTrendQuery([_cycleA], Filter(_cycleA), IncludeDepartmentSeries: true);
        var overlay = (await mediator.Send(withDept)).Value!;
        overlay.DepartmentSeries.Should().NotBeEmpty("the overlay variant must be keyed separately");
        cache.WrittenKeys.Should().HaveCount(2);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  2. The key isolates. One test per key segment.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task Two_tenants_never_share_a_cached_dashboard_Issue129()
    {
        // ONE cache instance serving both tenants — the leak vector.
        var cache = new RecordingCache();

        var aResult = (await BuildPipeline(_tenantA, Hr(_tenantA), cache)
            .Send(new PerformanceDashboardOverviewQuery(Filter(_cycleA)))).Value!;
        var bResult = (await BuildPipeline(_tenantB, Hr(_tenantB), cache)
            .Send(new PerformanceDashboardOverviewQuery(Filter(_cycleB)))).Value!;

        // Behavioural: neither tenant's numbers bleed into the other.
        aResult.AverageScore.Should().Be(3.0m);
        aResult.ScoredEmployeeCount.Should().Be(2);
        bResult.AverageScore.Should().Be(5.0m);
        bResult.ScoredEmployeeCount.Should().Be(1);
        bResult.CycleName.Should().Be("B-FY2026");

        // Structural: each entry is written under ITS OWN tenant prefix, and neither tenant's key is
        // reachable from the other's prefix. This is the assertion that pins the tenant segment — the
        // behavioural half above would still pass without it, because tenant-unique cycle GUIDs feed the
        // hash and would separate the two keys by accident.
        cache.WrittenKeys.Should().HaveCount(2);
        cache.WrittenKeys.Should().ContainSingle(k => k.StartsWith($"t:{_tenantA}:"));
        cache.WrittenKeys.Should().ContainSingle(k => k.StartsWith($"t:{_tenantB}:"));
    }

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task An_hr_viewer_and_a_manager_never_share_a_cached_dashboard_Issue129()
    {
        // SAME tenant, SAME cycle, SAME filter, SAME shared cache. Scope KIND is the only difference, so if
        // the scope segment leaves the key these two collide and the manager reads org-wide numbers —
        // including the bottom-performer list for people outside their team.
        var cache = new RecordingCache();
        var filter = Filter(_cycleA);

        var hr = (await BuildPipeline(_tenantA, Hr(_tenantA), cache)
            .Send(new PerformanceDashboardOverviewQuery(filter))).Value!;
        var mgr = (await BuildPipeline(_tenantA, Manager(_tenantA, _mgr1UserId), cache)
            .Send(new PerformanceDashboardOverviewQuery(filter))).Value!;

        hr.Scope.Should().Be("Organization");
        hr.AverageScore.Should().Be(3.0m);
        hr.ScoredEmployeeCount.Should().Be(2);

        mgr.Scope.Should().Be("Team");
        mgr.AverageScore.Should().Be(4.0m, "the manager must see only their own direct report, not the org");
        mgr.ScoredEmployeeCount.Should().Be(1);
        mgr.TopPerformers.Should().ContainSingle(p => p.EmployeeId == _mgr1ReportId);
        mgr.BottomPerformers.Should().BeEmpty("BR-3: a manager gets no org-wide bottom list, cached or not");

        cache.WrittenKeys.Should().HaveCount(2).And.OnlyHaveUniqueItems();
    }

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task Two_managers_with_different_teams_never_share_a_cached_dashboard_Issue129()
    {
        // SAME tenant, SAME cycle, SAME filter, SAME scope KIND (both Team) — the ONLY difference is which
        // employees each manager may see. A key that carried just the scope kind would let manager 2 read
        // manager 1's team aggregate. This is why the key hashes the resolved restrict-set, not the kind.
        var cache = new RecordingCache();
        var filter = Filter(_cycleA);

        var one = (await BuildPipeline(_tenantA, Manager(_tenantA, _mgr1UserId), cache)
            .Send(new PerformanceDashboardOverviewQuery(filter))).Value!;
        var two = (await BuildPipeline(_tenantA, Manager(_tenantA, _mgr2UserId), cache)
            .Send(new PerformanceDashboardOverviewQuery(filter))).Value!;

        one.Scope.Should().Be("Team");
        two.Scope.Should().Be("Team");

        one.AverageScore.Should().Be(4.0m);
        one.TopPerformers.Should().ContainSingle(p => p.EmployeeId == _mgr1ReportId);

        two.AverageScore.Should().Be(2.0m, "manager 2 must not be served manager 1's cached team aggregate");
        two.TopPerformers.Should().ContainSingle(p => p.EmployeeId == _mgr2ReportId);
        two.TopPerformers.Should().NotContain(p => p.EmployeeId == _mgr1ReportId);

        cache.WrittenKeys.Should().HaveCount(2).And.OnlyHaveUniqueItems();
    }

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task A_managers_cached_entry_follows_their_team_changing_Issue129()
    {
        // The restrict-set is IN the key, so a roster change re-keys automatically. Without that property a
        // manager would keep seeing their pre-change team for the whole TTL.
        var cache = new RecordingCache();
        var mediator = BuildPipeline(_tenantA, Manager(_tenantA, _mgr1UserId), cache);
        var query = new PerformanceDashboardOverviewQuery(Filter(_cycleA));

        var before = (await mediator.Send(query)).Value!;
        before.ScoredEmployeeCount.Should().Be(1);
        before.AverageScore.Should().Be(4.0m);

        // Manager 2's report is reassigned to manager 1.
        using (var db = Db(_tenantA))
        {
            var moved = db.Employees.Single(e => e.Id == _mgr2ReportId);
            moved.ReportsToEmployeeId = _mgr1EmpId;
            db.SaveChanges();
        }

        var after = (await BuildPipeline(_tenantA, Manager(_tenantA, _mgr1UserId), cache).Send(query)).Value!;
        after.ScoredEmployeeCount.Should().Be(2, "the enlarged team must re-key rather than serve the old entry");
        after.AverageScore.Should().Be(3.0m);
        cache.WrittenKeys.Should().HaveCount(2).And.OnlyHaveUniqueItems();
    }
}
