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
//      so there is one test per SEGMENT of the key, each holding every other segment identical so that the
//      segment under test is the only thing that can separate the two requests:
//        - two TENANTS never share an entry                   (the CacheTenantPrefix segment)
//        - an HR viewer and a manager never share one         (the scope KIND segment)
//        - two managers with different teams never share one  (the scope RESTRICT-SET segment)
//        - the key follows the tenant whose DATA was loaded, not the caller's TOKEN  (section 3)
//        - department / grade / employment-type / location / top-bottom-count / include-probation
//          each separate two otherwise-identical requests     (section 4)
//
//      Section 3 exists because every test in sections 1-2 wires FakeCurrentUser.TenantId EQUAL to
//      MutableTenantContext.TenantId, so `CacheTenantPrefix.For(_tenantContext)` and `_currentUser.TenantId`
//      are indistinguishable and swapping one for the other leaves them all green — while filing one
//      tenant's payload in another tenant's bucket for the whole TTL.
//
//      Section 4 exists because every test in sections 1-3 builds its filter with `Filter(cycleId)`, which
//      leaves all six filter fields at their defaults — so deleting any one of them from BuildCacheKey also
//      left the file green, while two callers asking DIFFERENT questions got one shared answer.
//
//   3. What comes back from a hit is the WHOLE payload (a BeEquivalentTo over the full DTO after the JSON
//      round trip, not two scalars), and a cache OUTAGE degrades to a correct live read rather than to a
//      wrong or empty one (section 5).
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
using System.Text.Json;
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

    // ── Filter-segment fixture (tenant A) ───────────────────────────────────
    // A DEDICATED cycle with an explicit CycleParticipant roster, so its population is exactly the two
    // employees below and is unaffected by anything else in the seed. The two differ on EVERY one of the six
    // filter axes at once, which is what lets one pair of requests isolate one axis at a time:
    //
    //   employee   department  location  grade    employmentType  score
    //   _filterEmp1  _deptA      _locA    _gradeA  FullTime         5.0
    //   _filterEmp2  _deptA2     _locB    _gradeB  Contract         1.0
    //
    // Both are UNSCORED in _cycleA, so adding them cannot move any assertion the six original tests make.
    private readonly Guid _deptA2 = Guid.NewGuid();
    private readonly Guid _locA = Guid.NewGuid();
    private readonly Guid _locB = Guid.NewGuid();
    private readonly Guid _gradeA = Guid.NewGuid();
    private readonly Guid _gradeB = Guid.NewGuid();
    private readonly Guid _jobTitleA = Guid.NewGuid();
    private readonly Guid _jobTitleB = Guid.NewGuid();
    private readonly Guid _cycleFilters = Guid.NewGuid();
    private readonly Guid _cycleProbation = Guid.NewGuid();
    private readonly Guid _filterEmp1 = Guid.NewGuid();
    private readonly Guid _filterEmp2 = Guid.NewGuid();

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

        /// <summary>
        /// Reads an entry WITHOUT recording the read, and deserializes it with the service's own naming
        /// policy. This is what lets a test assert on the PAYLOAD stored under a given key, not just on the
        /// key's spelling — i.e. "no entry under tenant A's prefix holds tenant B's dashboard".
        /// </summary>
        public T? PeekPayload<T>(string key) where T : class
        {
            var bytes = _inner.Get(key);
            return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes, PayloadJsonOptions);
        }

        private static readonly JsonSerializerOptions PayloadJsonOptions =
            new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }

    /// <summary>
    /// An <see cref="IDistributedCache"/> whose every read AND write throws — a Redis outage. The service
    /// promises to FAIL OPEN (BUG-115): a cache outage may cost latency, never correctness or availability.
    /// Nothing in this file exercised that promise before, so the catch blocks were dead weight under test.
    /// </summary>
    private sealed class ThrowingCache : IDistributedCache
    {
        public int GetAttempts { get; private set; }
        public int SetAttempts { get; private set; }

        private InvalidOperationException Boom() => new("cache backend is unreachable");

        public byte[]? Get(string key) { GetAttempts++; throw Boom(); }
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) { GetAttempts++; throw Boom(); }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { SetAttempts++; throw Boom(); }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
            CancellationToken token = default) { SetAttempts++; throw Boom(); }
        public void Refresh(string key) => throw Boom();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw Boom();
        public void Remove(string key) => throw Boom();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw Boom();
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

            SeedFilterFixture(db, now);

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

    /// <summary>
    /// Tenant-A fixture for the six FILTER key segments (DepartmentId / GradeId / EmploymentType /
    /// LocationId / TopBottomCount / IncludeProbation), each of which every pre-existing test leaves at its
    /// default — so deleting any one of them from BuildCacheKey left the whole file green.
    ///
    /// <para>Deliberately ADDITIVE: the two employees carry no manager review in <c>_cycleA</c>, so they are
    /// unscored there and cannot move <c>_cycleA</c>'s average, scored count, distribution, department bars
    /// or performer lists. Their own cycles carry an explicit CycleParticipant roster, which pins those
    /// populations to exactly these two and makes the filter tests independent of the rest of the seed. Both
    /// new cycles start EARLIER than <c>_cycleA</c> so "the most recently started cycle" (the null-CycleId
    /// fallback) still resolves to <c>_cycleA</c>.</para>
    /// </summary>
    private void SeedFilterFixture(AppDbContext db, DateTime now)
    {
        db.Departments.Add(new Department { Id = _deptA2, TenantId = _tenantA, Name = "Support" });

        db.Locations.Add(new Location { Id = _locA, TenantId = _tenantA, Name = "Colombo" });
        db.Locations.Add(new Location { Id = _locB, TenantId = _tenantA, Name = "Kandy" });

        db.SalaryGrades.Add(new SalaryGrade
        {
            Id = _gradeA, TenantId = _tenantA, Code = "G1", Name = "Grade 1",
            MinAmount = 100m, MaxAmount = 200m, Currency = "LKR",
        });
        db.SalaryGrades.Add(new SalaryGrade
        {
            Id = _gradeB, TenantId = _tenantA, Code = "G2", Name = "Grade 2",
            MinAmount = 200m, MaxAmount = 300m, Currency = "LKR",
        });

        db.JobTitles.Add(new JobTitle
        { Id = _jobTitleA, TenantId = _tenantA, TitleName = "Engineer", GradeId = _gradeA });
        db.JobTitles.Add(new JobTitle
        { Id = _jobTitleB, TenantId = _tenantA, TitleName = "Analyst", GradeId = _gradeB });

        db.Employees.Add(NewEmployee(
            _filterEmp1, _tenantA, _deptA, "A-F1", "Fran",
            jobTitleId: _jobTitleA, locationId: _locA, employmentType: EmploymentType.FullTime));
        db.Employees.Add(NewEmployee(
            _filterEmp2, _tenantA, _deptA2, "A-F2", "Finn",
            jobTitleId: _jobTitleB, locationId: _locB, employmentType: EmploymentType.Contract));

        // Calibration is ENABLED here so CycleProgressDto.CalibrationCompleted is a real number rather than
        // null — the round-trip test needs the nullable int to carry a value it could actually lose.
        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleFilters, TenantId = _tenantA, Name = "A-FILTERS", Status = AppraisalCycleStatus.Active,
            Type = CycleType.Annual, StartDate = now.AddDays(-200), EndDate = now.AddDays(10),
            RatingScaleMax = 5, IsCalibrationEnabled = true,
        });
        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleProbation, TenantId = _tenantA, Name = "A-PROBATION",
            Status = AppraisalCycleStatus.Active, Type = CycleType.Probation,
            StartDate = now.AddDays(-210), EndDate = now.AddDays(10), RatingScaleMax = 5,
        });

        foreach (var (cycleId, employeeId) in new[]
                 {
                     (_cycleFilters, _filterEmp1), (_cycleFilters, _filterEmp2),
                     (_cycleProbation, _filterEmp1),
                 })
        {
            db.CycleParticipants.Add(new CycleParticipant
            { Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, CycleId = cycleId, EmployeeId = employeeId });
        }

        var f1Review = NewReview(_tenantA, _cycleFilters, _filterEmp1, 5.0m, now);
        db.ManagerReviews.Add(f1Review);
        db.ManagerReviews.Add(NewReview(_tenantA, _cycleFilters, _filterEmp2, 1.0m, now));
        db.ManagerReviews.Add(NewReview(_tenantA, _cycleProbation, _filterEmp1, 4.0m, now));

        db.RatingCalibrations.Add(new RatingCalibration
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, CycleId = _cycleFilters,
            EmployeeId = _filterEmp1, ManagerReviewId = f1Review.Id,
            OriginalScore = 5.0m, CalibratedScore = 4.5m, Reason = "moderated",
            CalibratedByUserId = Guid.NewGuid(),
        });
    }

    private static Employee NewEmployee(
        Guid id, Guid tenantId, Guid deptId, string no, string firstName,
        Guid? userId = null, Guid? reportsTo = null,
        Guid? jobTitleId = null, Guid? locationId = null,
        EmploymentType employmentType = EmploymentType.FullTime) => new()
        {
            Id = id, TenantId = tenantId, UserId = userId, EmployeeNo = no,
            FirstName = firstName, LastName = "T", Email = $"{no}@t.com", DepartmentId = deptId,
            ReportsToEmployeeId = reportsTo, Status = EmployeeStatus.Active, IsActive = true,
            EmploymentType = employmentType,
            JobTitleId = jobTitleId ?? Guid.Empty, LocationId = locationId,
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

    // ════════════════════════════════════════════════════════════════════════
    //  3. The key follows the DATA's tenant, not the JWT's.
    //
    //  Every test above wires FakeCurrentUser.TenantId EQUAL to MutableTenantContext.TenantId, so the two
    //  are indistinguishable and swapping `CacheTenantPrefix.For(_tenantContext)` for `_currentUser.TenantId`
    //  in BuildCacheKey leaves all of them green. That swap is a one-token edit that reads as equally correct
    //  at review, and it is the BUG-003 shape on a dashboard that already has a confirmed cross-tenant
    //  finding of exactly this form.
    //
    //  It is worse than BUG-003, because it is PERSISTENT rather than per-request: the DATA follows
    //  _tenantContext (the EF global query filter reads that same object, AppDbContext.cs:302) while the KEY
    //  would follow the JWT — so a request whose subdomain-resolved tenant differs from its token's tenant
    //  writes one tenant's payload into the OTHER tenant's bucket, where it is then served to legitimate
    //  users of that other tenant for the rest of the TTL.
    //
    //  These tests are the only ones in the file where the two tenant sources are DISTINCT.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>A caller whose token names one tenant while the request context resolves another.</summary>
    private static ICurrentUser HrWithTokenTenant(Guid tokenTenantId) => new FakeCurrentUser
    {
        UserId = Guid.NewGuid(), TenantId = tokenTenantId,
        Permissions = new[] { PermissionCatalog.Performance.ViewAll },
    };

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task The_cache_key_follows_the_tenant_whose_data_was_loaded_not_the_token_Issue129()
    {
        // Context = A, token = B. The EF query filter reads the CONTEXT, so this loads tenant A's dashboard.
        // The key must therefore land in tenant A's bucket — the bucket whose data it actually holds.
        var cache = new RecordingCache();

        var mismatched = await BuildPipeline(_tenantA, HrWithTokenTenant(_tenantB), cache)
            .Send(new PerformanceDashboardOverviewQuery(Filter(_cycleA)));

        mismatched.IsSuccess.Should().BeTrue();
        mismatched.Value!.CycleName.Should().Be("A-FY2026", "the DATA follows the tenant CONTEXT");
        mismatched.Value.AverageScore.Should().Be(3.0m);
        mismatched.Value.ScoredEmployeeCount.Should().Be(2);

        var key = cache.WrittenKeys.Should().ContainSingle().Subject;
        key.Should().StartWith($"t:{_tenantA}:",
            "the key must carry the tenant whose data it holds (the CONTEXT), never the token's tenant — " +
            "keying on _currentUser.TenantId files tenant A's payload under tenant B's prefix");
        key.Should().NotStartWith($"t:{_tenantB}:");

        // The entry in tenant A's bucket really does hold tenant A's dashboard.
        var payload = cache.PeekPayload<PerformanceDashboardDto>(key);
        payload.Should().NotBeNull();
        payload!.CycleId.Should().Be(_cycleA);
        payload.CycleName.Should().Be("A-FY2026");
    }

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task A_token_mismatched_write_and_a_genuine_read_of_the_same_tenant_share_one_entry_Issue129()
    {
        // The behavioural half of the merge condition, stated as cache TOPOLOGY rather than key spelling.
        // Both requests load tenant A's data (the context is A in both), with the same scope, same resolved
        // cycle and same filter — so they must address ONE entry. Keyed on the token they address two, and
        // the tenant-A bucket ends up holding an entry written for a caller from another tenant.
        var cache = new RecordingCache();
        var query = new PerformanceDashboardOverviewQuery(Filter(_cycleA));

        var mismatched = await BuildPipeline(_tenantA, HrWithTokenTenant(_tenantB), cache).Send(query);
        mismatched.IsSuccess.Should().BeTrue();
        cache.WrittenKeys.Should().ContainSingle();

        var genuineA = await BuildPipeline(_tenantA, Hr(_tenantA), cache).Send(query);

        genuineA.IsSuccess.Should().BeTrue();
        genuineA.Value!.AverageScore.Should().Be(3.0m);
        cache.WrittenKeys.Should().ContainSingle(
            "the genuine tenant-A read must HIT the entry the tenant-A data was written to; a second write " +
            "here means the two requests were bucketed by their tokens instead of by the data's tenant");
    }

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task No_entry_in_tenant_As_bucket_ever_holds_tenant_Bs_dashboard_Issue129()
    {
        // The leak stated as DATA. One shared cache; a genuine tenant-A read populates tenant A's bucket,
        // then a request whose context resolves tenant B while its token names tenant A loads tenant B's
        // dashboard. Keyed on the context that lands in tenant B's bucket. Keyed on the token it lands in
        // tenant A's — where the next legitimate tenant-A user with the same filters reads it, and is served
        // another tenant's numbers, names and employee numbers for the rest of the TTL.
        var cache = new RecordingCache();

        var genuineA = await BuildPipeline(_tenantA, Hr(_tenantA), cache)
            .Send(new PerformanceDashboardOverviewQuery(Filter(_cycleA)));
        genuineA.IsSuccess.Should().BeTrue();

        var spoofed = await BuildPipeline(_tenantB, HrWithTokenTenant(_tenantA), cache)
            .Send(new PerformanceDashboardOverviewQuery(Filter(_cycleB)));

        spoofed.IsSuccess.Should().BeTrue();
        spoofed.Value!.CycleName.Should().Be("B-FY2026", "the DATA follows the tenant CONTEXT");
        spoofed.Value.AverageScore.Should().Be(5.0m);

        cache.WrittenKeys.Should().HaveCount(2).And.OnlyHaveUniqueItems();

        var tenantAKeys = cache.WrittenKeys.Where(k => k.StartsWith($"t:{_tenantA}:")).ToList();

        // Content FIRST, count second: this ordering is deliberate, so that when the key stops following the
        // data's tenant the failure that surfaces is the LEAK ("this entry in tenant A's bucket says
        // B-FY2026") rather than a bookkeeping complaint about how many entries there are.
        // Non-vacuous either way: there is always at least one entry here to inspect.
        tenantAKeys.Should().NotBeEmpty();
        foreach (var k in tenantAKeys)
        {
            var payload = cache.PeekPayload<PerformanceDashboardDto>(k);
            payload.Should().NotBeNull();
            payload!.CycleName.Should().Be("A-FY2026",
                $"the entry at {k} sits in tenant A's bucket, so a tenant-A user will be served it");
            payload.CycleId.Should().Be(_cycleA);
            payload.AverageScore.Should().Be(3.0m);
            payload.TopPerformers.Should().NotContain(p => p.EmployeeId == _tenantBEmpId,
                "no tenant-B employee may appear in a payload a tenant-A user can be served");
            payload.TopPerformers.Should().NotContain(p => p.EmployeeNo == "B-1");
        }

        tenantAKeys.Should().ContainSingle(
            "exactly one of these two reads loaded tenant A's data, so exactly one entry belongs in tenant " +
            "A's bucket — a second means tenant B's payload was filed there too");
    }

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task The_trend_cache_key_also_follows_the_tenant_context_not_the_token_Issue129()
    {
        // Same swap, second cached entry point. The trend is the expensive path, so it is the one most likely
        // to be touched by a later perf change — and it builds its key through the same BuildCacheKey.
        var cache = new RecordingCache();

        var mismatched = await BuildPipeline(_tenantA, HrWithTokenTenant(_tenantB), cache)
            .Send(new PerformanceTrendQuery([_cycleA], Filter(_cycleA), IncludeDepartmentSeries: false));

        mismatched.IsSuccess.Should().BeTrue();
        mismatched.Value!.Points.Should().ContainSingle().Which.CycleName.Should().Be("A-FY2026");

        cache.WrittenKeys.Should().ContainSingle().Which.Should().StartWith($"t:{_tenantA}:");

        var genuineA = await BuildPipeline(_tenantA, Hr(_tenantA), cache)
            .Send(new PerformanceTrendQuery([_cycleA], Filter(_cycleA), IncludeDepartmentSeries: false));

        genuineA.IsSuccess.Should().BeTrue();
        genuineA.Value!.Points[0].AverageScore.Should().Be(3.0m);
        cache.WrittenKeys.Should().ContainSingle(
            "the token-mismatched write and the genuine tenant-A read must address ONE entry");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  4. Every FILTER segment of the key is pinned.
    //
    //  BuildCacheKey folds six filter fields into the hash beyond the cycle id: DepartmentId, GradeId,
    //  EmploymentType, LocationId, TopBottomCount and IncludeProbation. Every test above builds its filter
    //  with `Filter(cycleId)`, which leaves all six at their defaults — so deleting any one of those six
    //  lines from BuildCacheKey left the entire file green while two callers who asked DIFFERENT questions
    //  started sharing one answer.
    //
    //  Each test below issues two requests from the SAME caller on ONE shared cache, differing in exactly
    //  one field, and asserts both halves: two distinct entries were written (structural), and the second
    //  request got its OWN answer rather than the first's (behavioural). Dropping the corresponding line
    //  from the key makes the second request a hit on the first's entry and fails both halves.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Issues two overview requests from ONE caller over ONE shared cache and returns both payloads plus the
    /// cache, so a test can assert on the answers AND on how many entries the pair produced.
    /// </summary>
    private async Task<(PerformanceDashboardDto First, PerformanceDashboardDto Second, RecordingCache Cache)>
        TwoOverviews(PerformanceDashboardFilter a, PerformanceDashboardFilter b)
    {
        var cache = new RecordingCache();
        var mediator = BuildPipeline(_tenantA, Hr(_tenantA), cache);

        var first = await mediator.Send(new PerformanceDashboardOverviewQuery(a));
        var second = await mediator.Send(new PerformanceDashboardOverviewQuery(b));

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        return (first.Value!, second.Value!, cache);
    }

    private static void ShouldNotShareAnEntry(RecordingCache cache, string segment) =>
        cache.WrittenKeys.Should().HaveCount(2).And.OnlyHaveUniqueItems(
            $"two requests differing only in {segment} must not share a cache entry — that segment is in " +
            "the key precisely because it changes the answer");

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task Two_departments_never_share_a_cached_dashboard_Issue129()
    {
        var (a, b, cache) = await TwoOverviews(
            Filter(_cycleFilters) with { DepartmentId = _deptA },
            Filter(_cycleFilters) with { DepartmentId = _deptA2 });

        a.ScoredEmployeeCount.Should().Be(1);
        a.AverageScore.Should().Be(5.0m);
        b.ScoredEmployeeCount.Should().Be(1);
        b.AverageScore.Should().Be(1.0m, "the Support drill-down must not be served Engineering's aggregate");
        b.TopPerformers.Should().ContainSingle(p => p.EmployeeId == _filterEmp2);

        ShouldNotShareAnEntry(cache, "DepartmentId");
    }

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task Two_grades_never_share_a_cached_dashboard_Issue129()
    {
        var (a, b, cache) = await TwoOverviews(
            Filter(_cycleFilters) with { GradeId = _gradeA },
            Filter(_cycleFilters) with { GradeId = _gradeB });

        a.ScoredEmployeeCount.Should().Be(1);
        a.AverageScore.Should().Be(5.0m);
        b.ScoredEmployeeCount.Should().Be(1);
        b.AverageScore.Should().Be(1.0m, "the Grade 2 band must not be served the Grade 1 aggregate");
        b.TopPerformers.Should().ContainSingle(p => p.EmployeeId == _filterEmp2);

        ShouldNotShareAnEntry(cache, "GradeId");
    }

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task Two_employment_types_never_share_a_cached_dashboard_Issue129()
    {
        var (a, b, cache) = await TwoOverviews(
            Filter(_cycleFilters) with { EmploymentType = "FullTime" },
            Filter(_cycleFilters) with { EmploymentType = "Contract" });

        a.ScoredEmployeeCount.Should().Be(1);
        a.AverageScore.Should().Be(5.0m);
        b.ScoredEmployeeCount.Should().Be(1);
        b.AverageScore.Should().Be(1.0m, "the contractor view must not be served the full-time aggregate");
        b.TopPerformers.Should().ContainSingle(p => p.EmployeeId == _filterEmp2);

        ShouldNotShareAnEntry(cache, "EmploymentType");
    }

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task The_employment_type_segment_is_case_insensitive_like_the_filter_itself_Issue129()
    {
        // The counterpart to the test above, and the reason the segment is lower-cased rather than dropped:
        // ParseEmploymentType is case-INSENSITIVE, so "fulltime" and "FullTime" select the same population
        // and MUST share one entry. A key that folded in the raw string would fragment the cache on casing —
        // not a leak, but a silent halving of the hit rate this whole change exists to buy.
        var (a, b, cache) = await TwoOverviews(
            Filter(_cycleFilters) with { EmploymentType = "FullTime" },
            Filter(_cycleFilters) with { EmploymentType = "fulltime" });

        a.AverageScore.Should().Be(5.0m);
        b.AverageScore.Should().Be(5.0m);
        cache.WrittenKeys.Should().ContainSingle(
            "two spellings that select the SAME population must share one entry");
    }

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task Two_locations_never_share_a_cached_dashboard_Issue129()
    {
        var (a, b, cache) = await TwoOverviews(
            Filter(_cycleFilters) with { LocationId = _locA },
            Filter(_cycleFilters) with { LocationId = _locB });

        a.ScoredEmployeeCount.Should().Be(1);
        a.AverageScore.Should().Be(5.0m);
        b.ScoredEmployeeCount.Should().Be(1);
        b.AverageScore.Should().Be(1.0m, "the Kandy branch must not be served Colombo's aggregate");
        b.TopPerformers.Should().ContainSingle(p => p.EmployeeId == _filterEmp2);

        ShouldNotShareAnEntry(cache, "LocationId");
    }

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task Two_top_bottom_counts_never_share_a_cached_dashboard_Issue129()
    {
        // The one segment that does NOT narrow the population — which is exactly why it is the easiest of the
        // six to argue away in a refactor ("it's only a page size, the aggregate is identical"). It is not:
        // it sizes TopPerformers/BottomPerformers, so a collision silently truncates or pads the very lists
        // FR-3 is about, while every headline number still looks right.
        var (a, b, cache) = await TwoOverviews(
            Filter(_cycleFilters) with { TopBottomCount = 1 },
            Filter(_cycleFilters) with { TopBottomCount = 2 });

        a.AverageScore.Should().Be(3.0m);
        b.AverageScore.Should().Be(3.0m, "the aggregate is identical — only the list SIZE differs");

        a.TopPerformers.Should().ContainSingle().Which.EmployeeId.Should().Be(_filterEmp1);
        a.BottomPerformers.Should().ContainSingle();
        b.TopPerformers.Should().HaveCount(2, "a top-2 request must not be served the top-1 list");
        b.BottomPerformers.Should().HaveCount(2);

        ShouldNotShareAnEntry(cache, "TopBottomCount");
    }

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task Including_and_excluding_probation_never_share_a_cached_dashboard_Issue129()
    {
        // BR-2 (ISSUE-128): a probation-type cycle contributes NO population unless the caller opts it back
        // in. So on a probation cycle the two answers are "nothing" and "everything" — the widest possible
        // gap between two requests that differ in one boolean.
        var (excluded, included, cache) = await TwoOverviews(
            Filter(_cycleProbation) with { IncludeProbation = false },
            Filter(_cycleProbation) with { IncludeProbation = true });

        excluded.ScoredEmployeeCount.Should().Be(0);
        excluded.AverageScore.Should().Be(0m);
        excluded.TopPerformers.Should().BeEmpty();

        included.ScoredEmployeeCount.Should().Be(1, "opting probation back in must recompute, not reuse");
        included.AverageScore.Should().Be(4.0m);
        included.TopPerformers.Should().ContainSingle(p => p.EmployeeId == _filterEmp1);

        ShouldNotShareAnEntry(cache, "IncludeProbation");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  5. What comes BACK from the cache is the whole payload, and an outage is survivable.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task A_cache_hit_round_trips_the_ENTIRE_payload_Issue129()
    {
        // The hit-path tests above assert two scalars (AverageScore / ScoredEmployeeCount) and one trend
        // point. Everything else on the DTO — Progress, ScoreDistribution, DepartmentAverages, the performer
        // lists, AvailableExportFormats, RatingScaleMax, the nullable CalibrationCompleted — crossed a JSON
        // round trip entirely unasserted, so a serialization regression (a renamed field, a dropped
        // collection, an int? collapsing to 0) would have been invisible.
        var cache = new RecordingCache();
        var mediator = BuildPipeline(_tenantA, Hr(_tenantA), cache);
        var query = new PerformanceDashboardOverviewQuery(Filter(_cycleFilters));

        var live = (await mediator.Send(query)).Value!;

        // Guard: BeEquivalentTo on two empty payloads proves nothing, so pin that every part is populated
        // BEFORE comparing. These also pin the specific values the round trip must preserve.
        live.RatingScaleMax.Should().Be(5);
        live.ScoredEmployeeCount.Should().Be(2);
        live.AverageScore.Should().Be(3.0m);
        live.Scope.Should().Be("Organization");
        live.AvailableExportFormats.Should().NotBeEmpty();
        live.ScoreDistribution.Should().HaveCount(4);
        live.DepartmentAverages.Should().HaveCount(2);
        live.TopPerformers.Should().HaveCount(2);
        live.BottomPerformers.Should().HaveCount(2);
        live.Progress.TotalParticipants.Should().Be(2);
        live.Progress.ManagerReviewCompleted.Should().Be(2);
        live.Progress.CompletionRate.Should().Be(100m);
        live.Progress.CalibrationCompleted.Should().Be(1,
            "a nullable int carrying a real value is what makes the round trip meaningful — null would " +
            "survive a field that no longer serializes at all");

        var cached = (await mediator.Send(query)).Value!;

        cache.WrittenKeys.Should().ContainSingle(
            "the second request must be a HIT — otherwise this compares two live computations and asserts " +
            "nothing about serialization");
        cache.ReadKeys.Should().HaveCount(2);
        cached.Should().NotBeSameAs(live, "the cached value must be a fresh deserialization");

        cached.Should().BeEquivalentTo(live,
            "every field must survive the JSON round trip, not just the two the hit-path tests read");
    }

    [Fact]
    [Trait("TC", "TC-PRF-ISO-129")]
    public async Task A_cache_outage_degrades_to_a_correct_LIVE_read_Issue129()
    {
        // BUG-115 fail-open: the service swallows cache exceptions so an outage costs latency, never
        // availability. No double in this file ever threw, so both catch blocks were untested — and a
        // fail-open that returned an EMPTY or STALE payload instead of a live one would have looked
        // identical from outside.
        var cache = new ThrowingCache();
        var mediator = BuildPipeline(_tenantA, Hr(_tenantA), cache);
        var query = new PerformanceDashboardOverviewQuery(Filter(_cycleA));

        var first = await mediator.Send(query);

        first.IsSuccess.Should().BeTrue("a cache outage must not surface as a failed request");
        first.Value!.AverageScore.Should().Be(3.0m);
        first.Value.ScoredEmployeeCount.Should().Be(2);
        first.Value.CycleName.Should().Be("A-FY2026");
        first.Value.TopPerformers.Should().NotBeEmpty(
            "degrading to an EMPTY payload is a wrong answer, not a slow one");
        first.Value.ScoreDistribution.Should().NotBeEmpty();

        // With every read AND write throwing, nothing can be memoised — so a change under the service MUST
        // be visible on the next call. This is the inverse of the read-through tests, and it is what
        // separates "fell back to a live read" from "fell back to some other cached copy".
        using (var db = Db(_tenantA))
        {
            db.ManagerReviews.Add(NewReview(_tenantA, _cycleA, _mgr1EmpId, 1.0m, DateTime.UtcNow));
            db.SaveChanges();
        }

        var second = await mediator.Send(query);

        second.IsSuccess.Should().BeTrue();
        second.Value!.ScoredEmployeeCount.Should().Be(3, "the degraded path must recompute, not go stale");
        second.Value.AverageScore.Should().Be(2.33m);

        cache.GetAttempts.Should().Be(2, "fail-open must not silently STOP consulting the cache");
        cache.SetAttempts.Should().Be(2, "...nor stop trying to repopulate it once it recovers");

        // The trend path fails open through the same helpers; assert it rather than assuming.
        var trend = await mediator.Send(
            new PerformanceTrendQuery([_cycleA], Filter(_cycleA), IncludeDepartmentSeries: false));

        trend.IsSuccess.Should().BeTrue();
        trend.Value!.Points.Should().ContainSingle();
        trend.Value.Points[0].ScoredEmployeeCount.Should().Be(3);
        trend.Value.Points[0].AverageScore.Should().Be(2.33m);
        cache.GetAttempts.Should().Be(3);
        cache.SetAttempts.Should().Be(3);
    }
}
