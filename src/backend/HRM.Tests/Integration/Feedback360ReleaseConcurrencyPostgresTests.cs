// ============================================================================
// US-PRF-005 BR-4 — CONCURRENT double-release idempotency for Feedback360Service.ReleaseResultsAsync.
//
// ReleaseResultsAsync is read-then-insert: it looks for an existing feedback_360_release row and, finding
// none, inserts one. Two SIMULTANEOUS callers therefore both see no row and both insert. The filtered
// unique index (tenant_id, cycle_id, reviewee_employee_id) WHERE is_deleted = false is the real guard and
// rejects the loser with a unique violation. The fix catches DbUpdateException, detaches the failed entity,
// re-resolves the winner's row and returns it — so idempotency holds concurrently, not just sequentially.
//
// HARNESS = real Postgres via Testcontainers (NOT InMemory). This is deliberate and load-bearing: the EF
// InMemory provider does NOT enforce unique indexes at all, so BOTH inserts would succeed there and the
// test would report two rows while claiming to prove idempotency — it could not tell pre-fix from post-fix,
// and would in fact go green over the bug. The sibling Feedback360IntegrationTests file is InMemory, which
// is exactly why this arm lives in its own Postgres file:
//   * pre-fix  — the loser's SaveChangesAsync throws DbUpdateException, which escapes as an unhandled
//                fault (500 to the caller). A double-clicked "Release results" button hits this. FAILS.
//   * post-fix — the loser resolves to the winner's row and returns 200 with the same ReleasedAt. PASSES.
//
// Schema is built with EnsureCreatedAsync() (mirrors ApplicantConcurrencyPostgresTests — MigrateAsync is
// intentionally avoided so an unrelated uncommitted model change cannot trip PendingModelChangesWarning).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Authorization;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class Feedback360ReleaseConcurrencyPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _hrUserId = Guid.NewGuid();
    private readonly Guid _cycleId = Guid.NewGuid();
    private readonly Guid _revieweeId = Guid.NewGuid();
    private readonly Guid _peer1Id = Guid.NewGuid();
    private readonly Guid _peer2Id = Guid.NewGuid();
    private readonly MutableTenantContext _tc = new();
    private ICurrentUser _cu = null!;

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
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _tc.TenantId = _tenantId;
        _cu = Substitute.For<ICurrentUser>();
        _cu.IsAuthenticated.Returns(true);
        _cu.UserId.Returns(_hrUserId);
        _cu.Email.Returns("hr@acme.com");
        // HR: Review.All clears the authorization gate outright, so the ONLY thing under test here is the
        // insert race — not the permission branch (that is covered by the sibling authorization arms).
        _cu.Permissions.Returns(new[] { PermissionCatalog.Performance.ReviewAll });

        await using var db = Db();
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var deptId = Guid.NewGuid();
        var jobTitleId = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
        // Department and job title must be REAL rows here. The sibling InMemory suite gets away with bare
        // Guid.NewGuid()s because InMemory enforces no foreign keys; Postgres does. Employee.JobTitleId is
        // non-nullable, so it is the same REQUIRED navigation behind the documented Include trap.
        db.Departments.Add(new Department
        {
            Id = deptId, TenantId = _tenantId, Name = "Engineering", Code = "ENG", IsActive = true,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = jobTitleId, TenantId = _tenantId, TitleName = "Engineer", IsActive = true,
        });
        db.Employees.AddRange(
            Emp(_revieweeId, "RVW", "Ada", "Lovelace", deptId, jobTitleId),
            Emp(_peer1Id, "PR1", "Alan", "Turing", deptId, jobTitleId),
            Emp(_peer2Id, "PR2", "Edsger", "Dijkstra", deptId, jobTitleId));

        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleId, TenantId = _tenantId, Name = "FY2026", Status = AppraisalCycleStatus.Active,
            GoalSettingStart = now.AddDays(-90), GoalSettingEnd = now.AddDays(-60),
            SelfAssessmentStart = now.AddDays(-30), SelfAssessmentEnd = now.AddDays(-10),
            ManagerReviewStart = now.AddDays(-5), ManagerReviewEnd = now.AddDays(5),
            RatingScaleMax = 5, SelfWeightPercent = 30,
            // Threshold MET (2 peers seeded below, minimum 2) so the BR-4 gate is not what decides this test.
            Is360Enabled = true, IsAnonymousFeedback = false, Min360PeerReviewers = 2,
            ThreeSixtySelfWeightPercent = 10, ThreeSixtyManagerWeightPercent = 40,
            ThreeSixtyPeerWeightPercent = 30, ThreeSixtyReportWeightPercent = 20,
        });

        db.Feedback360s.AddRange(
            PeerFeedback(_peer1Id, 4),
            PeerFeedback(_peer2Id, 5));

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(_tc), new AuditInterceptor(_cu))
            .Options, _tc);

    private Feedback360Service Service(AppDbContext db) => new(
        db, _tc, _cu,
        Substitute.For<IPerformanceNotificationService>(),
        NullLogger<Feedback360Service>.Instance);

    private Employee Emp(Guid id, string no, string first, string last, Guid dept, Guid jobTitleId)
        => new()
        {
            // UserId stays null: no employee here needs a login for this test, and a bare Guid would
            // violate fk_employees_users_user_id on real Postgres (InMemory would have accepted it).
            Id = id, TenantId = _tenantId, UserId = null, EmployeeNo = no,
            FirstName = first, LastName = last, Email = $"{first.ToLowerInvariant()}@acme.com",
            DepartmentId = dept, JobTitleId = jobTitleId, Status = EmployeeStatus.Active,
        };

    private Feedback360 PeerFeedback(Guid reviewerId, int rating)
        => new()
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, CycleId = _cycleId,
            RevieweeEmployeeId = _revieweeId, ReviewerEmployeeId = reviewerId,
            Category = ReviewerCategory.Peer, IsAnonymous = false,
            OverallComment = "Strong collaborator.", SubmittedAt = DateTime.UtcNow,
            Items =
            [
                new Feedback360Item
                {
                    Id = BaseEntity.NewUuidV7(), TenantId = _tenantId,
                    CompetencyKey = "Communication", Rating = rating, Comment = "Clear and consistent.",
                },
            ],
        };

    /// <summary>
    /// Blocks until Postgres reports at least one session waiting on another session's lock — i.e. B has
    /// genuinely reached its INSERT and is queued behind A's uncommitted row. Uses its OWN connection so it
    /// cannot participate in the contention it is observing. Throws rather than proceeding on timeout: a
    /// silent fall-through would reintroduce exactly the false-green this replaced.
    /// </summary>
    private async Task WaitUntilASessionIsBlockedAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var probe = Db();
            var blocked = await probe.Database
                .SqlQueryRaw<int>(
                    "SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE cardinality(pg_blocking_pids(pid)) > 0")
                .FirstAsync();
            if (blocked > 0)
                return;

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException(
            "No session ever became lock-blocked, so the double-release race was never actually forced. "
            + "Failing loudly instead of passing on the sequential path.");
    }

    private async Task<int> ReleaseRowCountAsync()
    {
        await using var db = Db();
        return await db.Feedback360Releases.AsNoTracking()
            .CountAsync(r => r.CycleId == _cycleId && r.RevieweeEmployeeId == _revieweeId);
    }

    // ── The race: two simultaneous releases must both return 200, and write exactly ONE row ──

    [Fact]
    public async Task ConcurrentDoubleRelease_LoserResolvesToWinnersRow_AndWritesExactlyOneRow_BR4()
    {
        // -- Why this is not simply two Task.WhenAll'd calls -------------------------------------------
        // A naive Task.WhenAll DOES NOT reliably race: the first call finishes its INSERT before the
        // second even reads, so both return via the early-exit "existing row" branch and the recovery is
        // never entered. Verified by mutation -- deleting the recovery entirely left such a test GREEN,
        // i.e. it proved nothing. The interleaving has to be forced.
        //
        // Forcing it with an uncommitted transaction, which is deterministic under READ COMMITTED:
        //   1. A inserts inside an OPEN transaction       -> row exists but is invisible to B
        //   2. B reads (sees nothing), inserts            -> B BLOCKS on the unique index, waiting on A
        //   3. A commits                                  -> B's insert is rejected: unique violation
        //   4. B's catch re-resolves A's now-visible row  -> B returns 200 with the SAME release
        // Without the recovery, step 4 is an unhandled DbUpdateException -> 500 to the caller.

        await using var dbA = Db();
        await using var txA = await dbA.Database.BeginTransactionAsync();

        var winner = await Service(dbA).ReleaseResultsAsync(_cycleId, _revieweeId);
        winner.IsSuccess.Should().BeTrue(winner.Error);

        // B runs on its own connection and blocks inside SaveChangesAsync until A commits.
        await using var dbB = Db();
        var loserTask = Task.Run(() => Service(dbB).ReleaseResultsAsync(_cycleId, _revieweeId));

        // Wait until B is ACTUALLY blocked on the unique index before committing A.
        //
        // A fixed Task.Delay was the first version of this and it is not sound: B must run authz, cycle,
        // reviewee, existing-check and peer-count queries before it ever reaches the blocking INSERT, so on
        // a cold or loaded box those reads can outlast any delay. A would then commit first, B would observe
        // the committed row, take the early-exit branch, and the arm would PASS while exercising nothing --
        // silently degrading to the sequential case. That failure mode is invisible (it can only produce a
        // false GREEN), and it would also make the mutation-verification of this arm timing-dependent.
        // Polling pg_blocking_pids() removes the guesswork: we proceed only once Postgres itself reports a
        // session waiting on another's lock.
        await WaitUntilASessionIsBlockedAsync(TimeSpan.FromSeconds(30));
        await txA.CommitAsync();

        var loser = await loserTask;

        loser.IsSuccess.Should().BeTrue(
            $"the LOSER of the unique-index race must resolve to the winner's row, not fault with a 500: {loser.Error}");
        // BeCloseTo, not Be: the loser re-read the row from Postgres, whose `timestamp with time zone`
        // holds microseconds, while the winner still holds the full-tick .NET DateTime it constructed.
        loser.Value!.ReleasedAt.Should().BeCloseTo(winner.Value!.ReleasedAt, TimeSpan.FromMilliseconds(1));
        loser.Value!.CycleId.Should().Be(_cycleId);
        loser.Value!.RevieweeEmployeeId.Should().Be(_revieweeId);

        (await ReleaseRowCountAsync()).Should().Be(1, "the filtered unique index permits exactly one release");
    }

    // ── Guard the index itself: a hand-rolled duplicate insert must be rejected by Postgres ──

    [Fact]
    public async Task DuplicateReleaseRow_IsRejectedByTheUniqueIndex_BR4()
    {
        var first = await Service(Db()).ReleaseResultsAsync(_cycleId, _revieweeId);
        first.IsSuccess.Should().BeTrue(first.Error);

        // Bypass the service entirely and insert a second row directly. This asserts the DATABASE constraint
        // exists, not just that the service checks first — the service's read-then-insert is only safe
        // BECAUSE this index backs it. If the index were dropped, this test goes red while every
        // service-level idempotency arm would stay green.
        await using var db = Db();
        db.Feedback360Releases.Add(new Feedback360Release
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, CycleId = _cycleId,
            RevieweeEmployeeId = _revieweeId, ReleasedAt = DateTime.UtcNow,
            ReleasedByEmployeeId = Guid.Empty, IsDeleted = false,
        });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "the filtered unique index (tenant_id, cycle_id, reviewee_employee_id) must reject a duplicate");

        (await ReleaseRowCountAsync()).Should().Be(1);
    }

    // ── Sequential re-release still resolves to the same row (the common retried-click case) ──

    [Fact]
    public async Task SequentialRerelease_ReturnsTheSameRow_BR4()
    {
        var first = await Service(Db()).ReleaseResultsAsync(_cycleId, _revieweeId);
        first.IsSuccess.Should().BeTrue(first.Error);

        var second = await Service(Db()).ReleaseResultsAsync(_cycleId, _revieweeId);
        second.IsSuccess.Should().BeTrue(second.Error);

        // Same sub-microsecond round-trip caveat as the concurrent arm: `first` holds the .NET DateTime it
        // constructed, `second` re-read it from Postgres at microsecond precision.
        second.Value!.ReleasedAt.Should().BeCloseTo(first.Value!.ReleasedAt, TimeSpan.FromMilliseconds(1));
        (await ReleaseRowCountAsync()).Should().Be(1);
    }
}
