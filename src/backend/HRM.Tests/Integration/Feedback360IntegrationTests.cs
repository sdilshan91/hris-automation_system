// ============================================================================
// US-PRF-005: 360-degree feedback — integration tests.
//
// Exercises ReviewerAssignmentService + Feedback360Service + the ManagerReviewService BR-6 incorporation
// over a real AppDbContext (InMemory) with the ITenantContext-driven global query filter, covering the
// cross-cutting concerns a service unit test cannot:
//   - NFR-2: tenant isolation — Tenant A's 360 feedback is invisible to Tenant B; a Tenant A reviewer
//     cannot submit against / read a Tenant B reviewee (cross-tenant read/write blocked).
//   - persistence: reviewer assignments + feedback + items survive a fresh DbContext round-trip.
//   - BR-3: duplicate submit blocked end-to-end (one persisted row).
//   - FR-5/NFR-3: anonymity enforced in the persisted-then-projected results (no reviewer id leaks).
//   - BR-6: the manager-review FINAL score incorporates the 360 peer/report feedback when 360 is enabled,
//     and is UNCHANGED (== the two-way self+manager blend) when 360 is disabled.
//
// PROVIDER: InMemory — same rationale as the other integration tests here (the verify gate runs
// `dotnet test` with no PostgreSQL / Docker). The genuine RLS / xmin behaviour is validated against real
// PG by the `migrations` CI job applying this feature's migration.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class Feedback360IntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

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

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private (AppDbContext Db, MutableTenantContext Ctx) Scoped(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return (new AppDbContext(options, ctx), ctx);
    }

    private Feedback360Service Feedback(Guid tenantId, Guid userId, params string[] permissions)
    {
        var (db, ctx) = Scoped(tenantId);
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.IsAuthenticated.Returns(true);
        user.Permissions.Returns(permissions);
        return new Feedback360Service(
            db, ctx, user, Substitute.For<IPerformanceNotificationService>(),
            NullLogger<Feedback360Service>.Instance);
    }

    /// <summary>
    /// Same as <see cref="Feedback"/> but returns the <see cref="IPerformanceNotificationService"/> mock so a
    /// test can assert on the release notification seam (<c>.Received</c> / <c>.DidNotReceive</c>). The inline
    /// <see cref="Feedback"/> factory throws its mock away, so it cannot verify BR-4/TC-PRF-005-04's notify step.
    /// </summary>
    private (Feedback360Service Service, IPerformanceNotificationService Notifications) FeedbackWithNotifications(
        Guid tenantId, Guid userId, params string[] permissions)
    {
        var (db, ctx) = Scoped(tenantId);
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.IsAuthenticated.Returns(true);
        user.Permissions.Returns(permissions);
        var notifications = Substitute.For<IPerformanceNotificationService>();
        return (new Feedback360Service(db, ctx, user, notifications,
            NullLogger<Feedback360Service>.Instance), notifications);
    }

    private ReviewerAssignmentService Assignments(Guid tenantId, Guid userId, params string[] permissions)
    {
        var (db, ctx) = Scoped(tenantId);
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.IsAuthenticated.Returns(true);
        user.Permissions.Returns(permissions);
        return new ReviewerAssignmentService(
            db, ctx, user, Substitute.For<IPerformanceNotificationService>(),
            NullLogger<ReviewerAssignmentService>.Instance);
    }

    private ManagerReviewService Manager(Guid tenantId, Guid userId, params string[] permissions)
    {
        var (db, ctx) = Scoped(tenantId);
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.IsAuthenticated.Returns(true);
        user.Permissions.Returns(permissions);
        return new ManagerReviewService(
            db, ctx, user, Substitute.For<IPerformanceNotificationService>(),
            NullLogger<ManagerReviewService>.Instance);
    }

    private sealed record Seeded(
        Guid ManagerUserId, Guid ManagerEmpId,
        Guid RevieweeUserId, Guid RevieweeEmpId,
        Guid Peer1UserId, Guid Peer1EmpId,
        Guid Peer2UserId, Guid Peer2EmpId,
        Guid CycleId, Guid Goal1, Guid Goal2);

    /// <summary>
    /// Seeds a manager, a reviewee (direct report of the manager) with a SUBMITTED self-assessment, two
    /// same-department peers, an active cycle (360 toggle + anonymity configurable), two goals (60/40), and
    /// Pending reviewer assignments for self/manager/both peers.
    /// </summary>
    private async Task<Seeded> SeedAsync(
        Guid tenantId, bool is360Enabled = true, bool isAnonymous = false,
        int selfWeightPercent = 30, decimal selfScore = 4.20m, int minPeers = 2)
    {
        using var db = Db(tenantId);
        var now = DateTime.UtcNow;
        var deptId = Guid.NewGuid();

        var s = new Seeded(
            Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        db.Employees.AddRange(
            Emp(tenantId, s.ManagerEmpId, s.ManagerUserId, "MGR", "Grace", "Hopper", deptId, null),
            Emp(tenantId, s.RevieweeEmpId, s.RevieweeUserId, "RVW", "Ada", "Lovelace", deptId, s.ManagerEmpId),
            Emp(tenantId, s.Peer1EmpId, s.Peer1UserId, "PR1", "Alan", "Turing", deptId, s.ManagerEmpId),
            Emp(tenantId, s.Peer2EmpId, s.Peer2UserId, "PR2", "Edsger", "Dijkstra", deptId, s.ManagerEmpId));

        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = s.CycleId, TenantId = tenantId, Name = "FY2026", Status = AppraisalCycleStatus.Active,
            GoalSettingStart = now.AddDays(-90), GoalSettingEnd = now.AddDays(-60),
            SelfAssessmentStart = now.AddDays(-30), SelfAssessmentEnd = now.AddDays(-10),
            ManagerReviewStart = now.AddDays(-5), ManagerReviewEnd = now.AddDays(5),
            RatingScaleMax = 5, SelfWeightPercent = selfWeightPercent,
            Is360Enabled = is360Enabled, IsAnonymousFeedback = isAnonymous, Min360PeerReviewers = minPeers,
            ThreeSixtySelfWeightPercent = 10, ThreeSixtyManagerWeightPercent = 40,
            ThreeSixtyPeerWeightPercent = 30, ThreeSixtyReportWeightPercent = 20,
        });

        db.Goals.AddRange(
            Goal(tenantId, s.Goal1, s.CycleId, s.RevieweeEmpId, "Ship", 60, now),
            Goal(tenantId, s.Goal2, s.CycleId, s.RevieweeEmpId, "Mentor", 40, now));

        db.SelfAssessments.Add(new SelfAssessment
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CycleId = s.CycleId, EmployeeId = s.RevieweeEmpId,
            Status = SelfAssessmentStatus.Submitted, WeightedSelfScore = selfScore, SubmittedAt = now.AddDays(-12),
            Items =
            [
                new SelfAssessmentItem { Id = Guid.NewGuid(), TenantId = tenantId, GoalId = s.Goal1, SelfRating = 5, AchievementPercentage = 100, Comment = "Consistent, strong delivery." },
                new SelfAssessmentItem { Id = Guid.NewGuid(), TenantId = tenantId, GoalId = s.Goal2, SelfRating = 3, AchievementPercentage = 70, Comment = "Consistent, strong delivery." },
            ],
        });

        // Pending reviewer assignments (the gate the submit path requires).
        db.ReviewerAssignments.AddRange(
            Assign(tenantId, s.CycleId, s.RevieweeEmpId, s.RevieweeEmpId, ReviewerCategory.Self),
            Assign(tenantId, s.CycleId, s.RevieweeEmpId, s.ManagerEmpId, ReviewerCategory.Manager),
            Assign(tenantId, s.CycleId, s.RevieweeEmpId, s.Peer1EmpId, ReviewerCategory.Peer),
            Assign(tenantId, s.CycleId, s.RevieweeEmpId, s.Peer2EmpId, ReviewerCategory.Peer));

        await db.SaveChangesAsync();
        return s;
    }

    private static Employee Emp(Guid t, Guid id, Guid userId, string no, string first, string last, Guid dept, Guid? reportsTo)
        => new()
        {
            Id = id, TenantId = t, UserId = userId, EmployeeNo = no, FirstName = first, LastName = last,
            Email = $"{first.ToLowerInvariant()}@t.com", DepartmentId = dept, Status = EmployeeStatus.Active,
            ReportsToEmployeeId = reportsTo,
        };

    private static Goal Goal(Guid t, Guid id, Guid cycleId, Guid empId, string title, int weight, DateTime now)
        => new()
        {
            Id = id, TenantId = t, CycleId = cycleId, EmployeeId = empId, Title = title,
            Category = GoalCategory.Kpi, Weight = weight, TargetValue = "100%", MeasurementUnit = "%",
            DueDate = DateOnly.FromDateTime(now.AddDays(30)), Status = GoalStatus.Acknowledged,
        };

    private static ReviewerAssignment Assign(Guid t, Guid cycleId, Guid reviewee, Guid reviewer, ReviewerCategory cat)
        => new()
        {
            Id = BaseEntity.NewUuidV7(), TenantId = t, CycleId = cycleId, RevieweeEmployeeId = reviewee,
            ReviewerEmployeeId = reviewer, Category = cat, Status = ReviewerAssignmentStatus.Pending,
        };

    private static SubmitFeedback360Input Fb(Guid cycleId, Guid revieweeEmpId, int rating)
        => new(cycleId, revieweeEmpId, "Good cycle.",
            new[] { new Feedback360ItemInput { CompetencyKey = "Communication", Rating = rating } });

    private static SaveManagerReviewInput ManagerSubmit(Guid cycleId, Guid empId, Guid g1, Guid g2, int r1, int r2)
        => new(cycleId, empId, "Overall solid.", ReviewFlag.None,
            new[]
            {
                new ManagerReviewItemInput(g1, r1, "Strong, consistent delivery against the goal."),
                new ManagerReviewItemInput(g2, r2, "Strong, consistent delivery against the goal."),
            });

    // ── persistence: assignments + feedback + items round-trip ──────────

    [Fact]
    public async Task Submit_Persists_Feedback_Items_AndMarksAssignmentCompleted()
    {
        var s = await SeedAsync(_tenantA);

        var result = await Feedback(_tenantA, s.Peer1UserId, PermissionCatalog.Performance.ViewOwn)
            .SubmitFeedbackAsync(Fb(s.CycleId, s.RevieweeEmpId, 4));

        result.IsSuccess.Should().BeTrue(result.Error);

        using var db = Db(_tenantA);
        var feedback = await db.Feedback360s.Include(f => f.Items).AsNoTracking()
            .SingleAsync(f => f.ReviewerEmployeeId == s.Peer1EmpId);
        feedback.CycleId.Should().Be(s.CycleId);
        feedback.RevieweeEmployeeId.Should().Be(s.RevieweeEmpId);
        feedback.Category.Should().Be(ReviewerCategory.Peer);
        feedback.Items.Should().ContainSingle(i => i.CompetencyKey == "Communication" && i.Rating == 4);

        (await db.ReviewerAssignments.AsNoTracking().FirstAsync(a => a.ReviewerEmployeeId == s.Peer1EmpId))
            .Status.Should().Be(ReviewerAssignmentStatus.Completed);
    }

    // ── BR-3: duplicate submit blocked end-to-end ───────────────────────

    [Fact]
    public async Task Submit_Twice_PersistsOnlyOneRow()
    {
        var s = await SeedAsync(_tenantA);

        (await Feedback(_tenantA, s.Peer1UserId, PermissionCatalog.Performance.ViewOwn)
            .SubmitFeedbackAsync(Fb(s.CycleId, s.RevieweeEmpId, 4))).IsSuccess.Should().BeTrue();
        (await Feedback(_tenantA, s.Peer1UserId, PermissionCatalog.Performance.ViewOwn)
            .SubmitFeedbackAsync(Fb(s.CycleId, s.RevieweeEmpId, 2))).IsFailure.Should().BeTrue();

        using var db = Db(_tenantA);
        (await db.Feedback360s.AsNoTracking().CountAsync(f => f.ReviewerEmployeeId == s.Peer1EmpId))
            .Should().Be(1);
    }

    // ── NFR-2: tenant isolation ─────────────────────────────────────────

    [Fact]
    public async Task ReviewerInTenantA_CannotSubmitAgainst_TenantBReviewee()
    {
        var a = await SeedAsync(_tenantA);
        var b = await SeedAsync(_tenantB);

        // Tenant A's peer1 tries to submit against Tenant B's reviewee + cycle. The global filter hides
        // both B's cycle and B's assignment, so the submit cannot find them (cross-tenant write blocked).
        var result = await Feedback(_tenantA, a.Peer1UserId, PermissionCatalog.Performance.ViewOwn)
            .SubmitFeedbackAsync(Fb(b.CycleId, b.RevieweeEmpId, 4));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().BeOneOf(403, 404);

        // Nothing was written into Tenant B.
        using var dbB = Db(_tenantB);
        (await dbB.Feedback360s.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Feedback_FromTenantA_Invisible_ToTenantB()
    {
        var a = await SeedAsync(_tenantA, minPeers: 1);
        (await Feedback(_tenantA, a.Peer1UserId, PermissionCatalog.Performance.ViewOwn)
            .SubmitFeedbackAsync(Fb(a.CycleId, a.RevieweeEmpId, 4))).IsSuccess.Should().BeTrue();

        // Tenant B sees no feedback rows at all.
        using var dbB = Db(_tenantB);
        (await dbB.Feedback360s.AsNoTracking().CountAsync()).Should().Be(0);

        // Tenant A sees exactly one.
        using var dbA = Db(_tenantA);
        (await dbA.Feedback360s.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TenantA_Hr_CannotRead_TenantBResults()
    {
        var b = await SeedAsync(_tenantB, minPeers: 1);
        (await Feedback(_tenantB, b.Peer1UserId, PermissionCatalog.Performance.ViewOwn)
            .SubmitFeedbackAsync(Fb(b.CycleId, b.RevieweeEmpId, 4))).IsSuccess.Should().BeTrue();

        // Tenant A's HR queries Tenant B's reviewee/cycle — the filter hides B's cycle & employee.
        var results = await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .GetResultsAsync(b.RevieweeEmpId, b.CycleId);

        results.IsFailure.Should().BeTrue();
        results.StatusCode.Should().BeOneOf(403, 404);
    }

    // ── FR-5/NFR-3: anonymity enforced in persisted-then-projected results ──

    [Fact]
    public async Task AnonymousResults_NeverLeak_ReviewerIdentity()
    {
        var s = await SeedAsync(_tenantA, isAnonymous: true, minPeers: 1);

        (await Feedback(_tenantA, s.Peer1UserId, PermissionCatalog.Performance.ViewOwn)
            .SubmitFeedbackAsync(Fb(s.CycleId, s.RevieweeEmpId, 4))).IsSuccess.Should().BeTrue();

        // The persisted row DOES record the reviewer id (needed for BR-3 de-dupe) ...
        using (var db = Db(_tenantA))
            (await db.Feedback360s.AsNoTracking().FirstAsync()).IsAnonymous.Should().BeTrue();

        // ... but the projected results MUST NOT surface it.
        var results = await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .GetResultsAsync(s.RevieweeEmpId, s.CycleId);

        results.IsSuccess.Should().BeTrue(results.Error);
        results.Value!.Entries.Should().OnlyContain(e =>
            e.IsAnonymous && e.ReviewerEmployeeId == null && e.ReviewerName == null);
        results.Value!.Entries.Select(e => e.ReviewerEmployeeId).Should().NotContain(s.Peer1EmpId);
    }

    // ── BR-6: manager-review FINAL score incorporates the 360 feedback ──

    [Fact]
    public async Task ManagerFinalScore_Incorporates360_WhenEnabled()
    {
        // 360 ENABLED. Peers submit 4 and 2 → peer avg 3. Self 4.20 (seeded self-assessment), manager
        // weighted score 5*0.6 + 3*0.4 = 4.20. No direct reports.
        // Final (BR-6) = composite over self/manager/peer with weights 10/40/30:
        //   (4.20*10 + 4.20*40 + 3*30)/(80) = (42 + 168 + 90)/80 = 300/80 = 3.75
        var s = await SeedAsync(_tenantA, is360Enabled: true, selfScore: 4.20m, minPeers: 2);

        (await Feedback(_tenantA, s.Peer1UserId, PermissionCatalog.Performance.ViewOwn)
            .SubmitFeedbackAsync(Fb(s.CycleId, s.RevieweeEmpId, 4))).IsSuccess.Should().BeTrue();
        (await Feedback(_tenantA, s.Peer2UserId, PermissionCatalog.Performance.ViewOwn)
            .SubmitFeedbackAsync(Fb(s.CycleId, s.RevieweeEmpId, 2))).IsSuccess.Should().BeTrue();

        var review = await Manager(_tenantA, s.ManagerUserId, PermissionCatalog.Performance.ReviewTeam)
            .SubmitAsync(ManagerSubmit(s.CycleId, s.RevieweeEmpId, s.Goal1, s.Goal2, r1: 5, r2: 3));

        review.IsSuccess.Should().BeTrue(review.Error);
        review.Value!.WeightedManagerScore.Should().Be(4.20m);
        review.Value!.FinalScore.Should().Be(3.75m, "BR-6: the 360 peer feedback shifts the final score");
    }

    [Fact]
    public async Task ManagerFinalScore_Unchanged_When360Disabled()
    {
        // 360 DISABLED — final score must be the plain US-PRF-003 two-way self+manager blend, NOT the 360
        // composite, even though peer feedback rows would exist. self 4.20 * 0.3 + manager 4.20 * 0.7 = 4.20.
        var s = await SeedAsync(_tenantA, is360Enabled: false, selfWeightPercent: 30, selfScore: 4.20m);

        var review = await Manager(_tenantA, s.ManagerUserId, PermissionCatalog.Performance.ReviewTeam)
            .SubmitAsync(ManagerSubmit(s.CycleId, s.RevieweeEmpId, s.Goal1, s.Goal2, r1: 5, r2: 3));

        review.IsSuccess.Should().BeTrue(review.Error);
        review.Value!.WeightedManagerScore.Should().Be(4.20m);
        review.Value!.FinalScore.Should().Be(4.20m, "with 360 disabled the final score is the two-way blend");
    }

    [Fact]
    public async Task ManagerFinalScore_360Enabled_DiffersFrom_TwoWayBlend_WhenPeersDisagree()
    {
        // Proves the two code paths actually diverge: with the SAME self+manager inputs, enabling 360 and
        // adding LOW peer ratings (both 1 → peer avg 1) pulls the final score BELOW the 4.20 two-way blend.
        //   composite = (4.20*10 + 4.20*40 + 1*30)/80 = (42 + 168 + 30)/80 = 240/80 = 3.00
        var s = await SeedAsync(_tenantA, is360Enabled: true, selfWeightPercent: 30, selfScore: 4.20m, minPeers: 2);

        (await Feedback(_tenantA, s.Peer1UserId, PermissionCatalog.Performance.ViewOwn)
            .SubmitFeedbackAsync(Fb(s.CycleId, s.RevieweeEmpId, 1))).IsSuccess.Should().BeTrue();
        (await Feedback(_tenantA, s.Peer2UserId, PermissionCatalog.Performance.ViewOwn)
            .SubmitFeedbackAsync(Fb(s.CycleId, s.RevieweeEmpId, 1))).IsSuccess.Should().BeTrue();

        var review = await Manager(_tenantA, s.ManagerUserId, PermissionCatalog.Performance.ReviewTeam)
            .SubmitAsync(ManagerSubmit(s.CycleId, s.RevieweeEmpId, s.Goal1, s.Goal2, r1: 5, r2: 3));

        review.Value!.FinalScore.Should().Be(3.00m).And.NotBe(4.20m);
    }

    // ── reviewer assignment persistence (auto-seed) ─────────────────────

    [Fact]
    public async Task GetConfiguration_AutoSeeds_AndPersistsAssignments()
    {
        var s = await SeedAsyncWithoutAssignments(_tenantA);

        var config = await Assignments(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .GetConfigurationAsync(s.RevieweeEmpId, s.CycleId);

        config.IsSuccess.Should().BeTrue(config.Error);

        using var db = Db(_tenantA);
        var persisted = await db.ReviewerAssignments.AsNoTracking()
            .Where(a => a.CycleId == s.CycleId && a.RevieweeEmployeeId == s.RevieweeEmpId)
            .ToListAsync();
        persisted.Should().Contain(a => a.Category == ReviewerCategory.Self && a.ReviewerEmployeeId == s.RevieweeEmpId);
        persisted.Should().Contain(a => a.Category == ReviewerCategory.Manager && a.ReviewerEmployeeId == s.ManagerEmpId);
        persisted.Count(a => a.Category == ReviewerCategory.Peer).Should().BeGreaterThanOrEqualTo(2);
    }

    /// <summary>Same as SeedAsync but WITHOUT the reviewer assignments (so auto-seed has work to do).</summary>
    private async Task<Seeded> SeedAsyncWithoutAssignments(Guid tenantId)
    {
        var s = await SeedAsync(tenantId);
        using var db = Db(tenantId);
        var rows = await db.ReviewerAssignments
            .Where(a => a.CycleId == s.CycleId && a.RevieweeEmployeeId == s.RevieweeEmpId).ToListAsync();
        db.ReviewerAssignments.RemoveRange(rows);
        await db.SaveChangesAsync();
        return s;
    }

    // ── PDF report export (FR-7, deferred-PDF work item) ────────────────

    [Fact]
    public async Task Export_report_pdf_returns_a_pdf_document()
    {
        var s = await SeedAsync(_tenantA, minPeers: 1);
        (await Feedback(_tenantA, s.Peer1UserId, PermissionCatalog.Performance.ViewOwn)
            .SubmitFeedbackAsync(Fb(s.CycleId, s.RevieweeEmpId, 4))).IsSuccess.Should().BeTrue();

        var export = await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .ExportReportAsync(s.RevieweeEmpId, s.CycleId, "pdf");

        export.IsSuccess.Should().BeTrue(export.Error);
        export.Value!.ContentType.Should().Be("application/pdf");
        export.Value!.FileName.Should().EndWith(".pdf");
        export.Value!.FileContent.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(export.Value!.FileContent, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task Export_report_pdf_is_tenant_isolated()
    {
        var b = await SeedAsync(_tenantB, minPeers: 1);

        // Tenant A's HR cannot render Tenant B's 360 report (global query filter hides B's cycle/employee).
        var crossExport = await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .ExportReportAsync(b.RevieweeEmpId, b.CycleId, "pdf");

        crossExport.IsFailure.Should().BeTrue();
        crossExport.StatusCode.Should().BeOneOf(403, 404);
    }

    [Fact]
    public async Task Export_report_pdf_refuses_a_caller_without_ReviewAll()
    {
        var s = await SeedAsync(_tenantA, minPeers: 1);

        // No Performance.Review.All → refused. The PDF is not less protected than the /results + /report JSON.
        var forbidden = await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReadSelf)
            .ExportReportAsync(s.RevieweeEmpId, s.CycleId, "pdf");

        forbidden.IsFailure.Should().BeTrue();
        forbidden.StatusCode.Should().Be(403);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  US-PRF-005 BR-4/FR-3: RELEASE state + reviewee-facing read
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Submits a peer's feedback (helper for the release-threshold arms).</summary>
    private async Task SubmitPeer(Guid tenantId, Guid cycleId, Guid revieweeEmpId, Guid peerUserId, int rating)
        => (await Feedback(tenantId, peerUserId, PermissionCatalog.Performance.ViewOwn)
            .SubmitFeedbackAsync(Fb(cycleId, revieweeEmpId, rating))).IsSuccess.Should().BeTrue();

    // ── BR-4: below the minimum peer threshold is hard-blocked (422), no row ──
    [Fact]
    public async Task Release_BelowPeerThreshold_Returns422_AndWritesNoRow()
    {
        var s = await SeedAsync(_tenantA, minPeers: 2);
        await SubmitPeer(_tenantA, s.CycleId, s.RevieweeEmpId, s.Peer1UserId, 4); // only 1 of 2 required peers

        var rel = await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .ReleaseResultsAsync(s.CycleId, s.RevieweeEmpId);

        rel.IsFailure.Should().BeTrue();
        rel.StatusCode.Should().Be(422);
        rel.ErrorCode.Should().Be("min_peer_threshold_not_met");

        using var db = Db(_tenantA);
        (await db.Feedback360Releases.AsNoTracking().CountAsync()).Should().Be(0,
            "a blocked release must not persist a release row");
    }

    // ── BR-4 boundary (TC-PRF-005-14): EXACTLY the minimum must SUCCEED (>=) ──
    // This is the arm the >= / > mutation flips: at 2 peers with a minimum of 2, `>=` releases and `>` blocks.
    [Fact]
    public async Task Release_ExactlyAtMinimumPeers_Succeeds()
    {
        var s = await SeedAsync(_tenantA, minPeers: 2);
        await SubmitPeer(_tenantA, s.CycleId, s.RevieweeEmpId, s.Peer1UserId, 4);
        await SubmitPeer(_tenantA, s.CycleId, s.RevieweeEmpId, s.Peer2UserId, 5); // exactly 2 == the minimum

        var rel = await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .ReleaseResultsAsync(s.CycleId, s.RevieweeEmpId);

        rel.IsSuccess.Should().BeTrue(rel.Error);
        rel.Value!.CycleId.Should().Be(s.CycleId);
        rel.Value!.RevieweeEmployeeId.Should().Be(s.RevieweeEmpId);

        using var db = Db(_tenantA);
        (await db.Feedback360Releases.AsNoTracking().CountAsync()).Should().Be(1);
    }

    // ── BR-4: above the minimum succeeds ──
    [Fact]
    public async Task Release_AbovePeerThreshold_Succeeds()
    {
        var s = await SeedAsync(_tenantA, minPeers: 1);
        await SubmitPeer(_tenantA, s.CycleId, s.RevieweeEmpId, s.Peer1UserId, 4);
        await SubmitPeer(_tenantA, s.CycleId, s.RevieweeEmpId, s.Peer2UserId, 5); // 2 > minimum of 1

        var rel = await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .ReleaseResultsAsync(s.CycleId, s.RevieweeEmpId);

        rel.IsSuccess.Should().BeTrue(rel.Error);
    }

    // ── BR-4: re-releasing is idempotent — 200, the existing row, no second insert ──
    [Fact]
    public async Task Release_Twice_IsIdempotent_OneRow()
    {
        var s = await SeedAsync(_tenantA, minPeers: 1);
        await SubmitPeer(_tenantA, s.CycleId, s.RevieweeEmpId, s.Peer1UserId, 4);

        var r1 = await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .ReleaseResultsAsync(s.CycleId, s.RevieweeEmpId);
        var r2 = await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .ReleaseResultsAsync(s.CycleId, s.RevieweeEmpId);

        r1.IsSuccess.Should().BeTrue(r1.Error);
        r2.IsSuccess.Should().BeTrue(r2.Error);
        r2.Value!.ReleasedAt.Should().Be(r1.Value!.ReleasedAt, "the re-release returns the SAME existing row");

        using var db = Db(_tenantA);
        (await db.Feedback360Releases.AsNoTracking().CountAsync()).Should().Be(1,
            "a retried release must not create a second row");
    }

    // ── Authorization: HR (Review.All), the reviewee's own manager (Review.Team), else denied ──
    [Fact]
    public async Task Release_ByHr_ReviewAll_Allowed()
    {
        var s = await SeedAsync(_tenantA, minPeers: 1);
        await SubmitPeer(_tenantA, s.CycleId, s.RevieweeEmpId, s.Peer1UserId, 4);

        var rel = await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .ReleaseResultsAsync(s.CycleId, s.RevieweeEmpId);

        rel.IsSuccess.Should().BeTrue(rel.Error);
    }

    [Fact]
    public async Task Release_ByRevieweesOwnManager_ReviewTeam_Allowed()
    {
        var s = await SeedAsync(_tenantA, minPeers: 1);
        await SubmitPeer(_tenantA, s.CycleId, s.RevieweeEmpId, s.Peer1UserId, 4);

        // The reviewee reports directly to the manager persona → the Review.Team gate allows the release.
        var rel = await Feedback(_tenantA, s.ManagerUserId, PermissionCatalog.Performance.ReviewTeam)
            .ReleaseResultsAsync(s.CycleId, s.RevieweeEmpId);

        rel.IsSuccess.Should().BeTrue(rel.Error);
        // Hole 3: the releaser is stamped with the ACTING manager's employee id — a mutation that stamps an
        // empty/wrong releaser (e.g. Guid.Empty) survives without this assertion.
        rel.Value!.ReleasedByEmployeeId.Should().Be(s.ManagerEmpId,
            "the release must record the employee id of the manager who performed it");
    }

    [Fact]
    public async Task Release_ByNonManagingManager_ReviewTeam_Denied_NotTeamManager()
    {
        var s = await SeedAsync(_tenantA, minPeers: 1);
        await SubmitPeer(_tenantA, s.CycleId, s.RevieweeEmpId, s.Peer1UserId, 4);

        // Peer1 holds Review.Team but is NOT the reviewee's reporting manager → 403 not_team_manager.
        var rel = await Feedback(_tenantA, s.Peer1UserId, PermissionCatalog.Performance.ReviewTeam)
            .ReleaseResultsAsync(s.CycleId, s.RevieweeEmpId);

        rel.IsFailure.Should().BeTrue();
        rel.StatusCode.Should().Be(403);
        rel.ErrorCode.Should().Be("not_team_manager");

        using var db = Db(_tenantA);
        (await db.Feedback360Releases.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Release_ByCaller_WithNeitherPermission_Denied()
    {
        var s = await SeedAsync(_tenantA, minPeers: 1);
        await SubmitPeer(_tenantA, s.CycleId, s.RevieweeEmpId, s.Peer1UserId, 4);

        var rel = await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ViewOwn)
            .ReleaseResultsAsync(s.CycleId, s.RevieweeEmpId);

        rel.IsFailure.Should().BeTrue();
        rel.StatusCode.Should().Be(403);
        rel.ErrorCode.Should().Be("forbidden");
    }

    // ── Reviewee-facing read: 404 not_released before release, 200 after ──
    [Fact]
    public async Task RevieweeReadsOwnResults_BeforeRelease_Returns404_NotReleased()
    {
        var s = await SeedAsync(_tenantA, minPeers: 1);
        await SubmitPeer(_tenantA, s.CycleId, s.RevieweeEmpId, s.Peer1UserId, 4);

        // Not yet released — 404 (never 403), so we do not disclose whether unreleased results exist.
        var mine = await Feedback(_tenantA, s.RevieweeUserId, PermissionCatalog.Performance.ReadSelf)
            .GetMyResultsAsync(s.CycleId);

        mine.IsFailure.Should().BeTrue();
        mine.StatusCode.Should().Be(404);
        mine.ErrorCode.Should().Be("not_released");
    }

    [Fact]
    public async Task RevieweeReadsOwnResults_AfterRelease_Succeeds()
    {
        var s = await SeedAsync(_tenantA, minPeers: 1);
        await SubmitPeer(_tenantA, s.CycleId, s.RevieweeEmpId, s.Peer1UserId, 4);
        (await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .ReleaseResultsAsync(s.CycleId, s.RevieweeEmpId)).IsSuccess.Should().BeTrue();

        var mine = await Feedback(_tenantA, s.RevieweeUserId, PermissionCatalog.Performance.ReadSelf)
            .GetMyResultsAsync(s.CycleId);

        mine.IsSuccess.Should().BeTrue(mine.Error);
        mine.Value!.Released.Should().BeTrue();
        mine.Value!.ReleasedAt.Should().NotBeNull();
        mine.Value!.ExportAvailable.Should().BeFalse("the reviewee cannot call the HR-gated PDF report route");
    }

    // ── FR-5 (THE arm that matters): the reviewee's own view NEVER carries a reviewer
    //    identity, even when the cycle's anonymity flag is OFF; HR's view on the same data DOES. ──
    [Fact]
    public async Task RevieweeResults_NeverLeak_ReviewerIdentity_EvenWhenAnonymityIsOff()
    {
        var s = await SeedAsync(_tenantA, isAnonymous: false, minPeers: 1);
        await SubmitPeer(_tenantA, s.CycleId, s.RevieweeEmpId, s.Peer1UserId, 4);
        (await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .ReleaseResultsAsync(s.CycleId, s.RevieweeEmpId)).IsSuccess.Should().BeTrue();

        // The reviewee's own payload: NO reviewer id or name on any entry (FR-5, unconditional).
        var mine = await Feedback(_tenantA, s.RevieweeUserId, PermissionCatalog.Performance.ReadSelf)
            .GetMyResultsAsync(s.CycleId);
        mine.IsSuccess.Should().BeTrue(mine.Error);
        mine.Value!.Entries.Should().NotBeEmpty();
        mine.Value!.Entries.Should().OnlyContain(e => e.ReviewerEmployeeId == null && e.ReviewerName == null);
        mine.Value!.Entries.Select(e => e.ReviewerEmployeeId).Should().NotContain(s.Peer1EmpId);

        // HR's payload on the SAME (non-anonymous) data DOES carry the reviewer identity — proving the
        // reviewee-path suppression is a real, path-specific override rather than the data simply being anonymous.
        var hr = await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .GetResultsAsync(s.RevieweeEmpId, s.CycleId);
        hr.IsSuccess.Should().BeTrue(hr.Error);
        hr.Value!.Entries.Should().Contain(e => e.ReviewerEmployeeId == s.Peer1EmpId && e.ReviewerName != null);
    }

    // ── NFR-2: tenant isolation on release ──
    [Fact]
    public async Task TenantA_Hr_CannotRelease_TenantBReviewee()
    {
        var b = await SeedAsync(_tenantB, minPeers: 1);
        await SubmitPeer(_tenantB, b.CycleId, b.RevieweeEmpId, b.Peer1UserId, 4);

        // Tenant A's HR tries to release Tenant B's reviewee/cycle — the filter hides B's cycle & employee.
        var rel = await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .ReleaseResultsAsync(b.CycleId, b.RevieweeEmpId);

        rel.IsFailure.Should().BeTrue();
        rel.StatusCode.Should().BeOneOf(403, 404);

        using var dbB = Db(_tenantB);
        (await dbB.Feedback360Releases.AsNoTracking().CountAsync()).Should().Be(0,
            "a cross-tenant release attempt must not write into Tenant B");
    }

    // ── HR path: Released / ReleasedAt / ExportAvailable populate correctly ──
    [Fact]
    public async Task HrResults_Populate_ReleaseFields_AndExportAvailable()
    {
        var s = await SeedAsync(_tenantA, minPeers: 1);
        await SubmitPeer(_tenantA, s.CycleId, s.RevieweeEmpId, s.Peer1UserId, 4);

        var before = await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .GetResultsAsync(s.RevieweeEmpId, s.CycleId);
        before.IsSuccess.Should().BeTrue(before.Error);
        before.Value!.Released.Should().BeFalse();
        before.Value!.ReleasedAt.Should().BeNull();
        before.Value!.ExportAvailable.Should().BeTrue("HR may always call the PDF report route");

        (await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .ReleaseResultsAsync(s.CycleId, s.RevieweeEmpId)).IsSuccess.Should().BeTrue();

        var after = await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .GetResultsAsync(s.RevieweeEmpId, s.CycleId);
        after.IsSuccess.Should().BeTrue(after.Error);
        after.Value!.Released.Should().BeTrue();
        after.Value!.ReleasedAt.Should().NotBeNull();
        after.Value!.ExportAvailable.Should().BeTrue();
    }

    // ── BR-4 / TC-PRF-005-04: a SUCCESSFUL release notifies the reviewee AND their reporting manager ──
    // Hole 1: the notification seam (Feedback360Service.cs:396-402) was asserted nowhere — the entire
    // NotifyCycleEventAsync("results-released", …) block could be deleted with the suite still green.
    [Fact]
    public async Task Release_OnSuccess_Notifies_Reviewee_And_ReportingManager()
    {
        var s = await SeedAsync(_tenantA, minPeers: 1);
        await SubmitPeer(_tenantA, s.CycleId, s.RevieweeEmpId, s.Peer1UserId, 4);

        var (service, notifications) = FeedbackWithNotifications(
            _tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll);
        var rel = await service.ReleaseResultsAsync(s.CycleId, s.RevieweeEmpId);

        rel.IsSuccess.Should().BeTrue(rel.Error);

        // The REVIEWEE is notified with the "results-released" event on their own cycle.
        await notifications.Received(1).NotifyCycleEventAsync(
            "results-released", s.CycleId, s.RevieweeEmpId, Arg.Any<string?>(), Arg.Any<CancellationToken>());

        // The reviewee's REPORTING MANAGER (a SEPARATE if-branch) is also notified — the seeded reviewee
        // reports to s.ManagerEmpId.
        await notifications.Received(1).NotifyCycleEventAsync(
            "results-released", s.CycleId, s.ManagerEmpId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // ── BR-4: a FAILED release (below the peer threshold) sends NO notification ──
    // Guards the manager branch too: without a real success there is nothing to notify.
    [Fact]
    public async Task Release_BelowPeerThreshold_SendsNoNotification()
    {
        var s = await SeedAsync(_tenantA, minPeers: 2);
        await SubmitPeer(_tenantA, s.CycleId, s.RevieweeEmpId, s.Peer1UserId, 4); // only 1 of 2 required peers

        var (service, notifications) = FeedbackWithNotifications(
            _tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll);
        var rel = await service.ReleaseResultsAsync(s.CycleId, s.RevieweeEmpId);

        rel.IsFailure.Should().BeTrue();
        rel.StatusCode.Should().Be(422);

        await notifications.DidNotReceive().NotifyCycleEventAsync(
            "results-released", Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // ── BR-4: releasing on a cycle where 360 is DISABLED is a 409 not_360_enabled ──
    // Hole 2: every other fixture seeds Is360Enabled=true, so the early-return branch (Feedback360Service.cs:330-332)
    // was untested — deleting it changed nothing in the suite.
    [Fact]
    public async Task Release_On_Non360Cycle_Returns409()
    {
        var s = await SeedAsync(_tenantA, is360Enabled: false, minPeers: 1);

        var rel = await Feedback(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .ReleaseResultsAsync(s.CycleId, s.RevieweeEmpId);

        rel.IsFailure.Should().BeTrue();
        rel.StatusCode.Should().Be(409);
        rel.ErrorCode.Should().Be("not_360_enabled");

        using var db = Db(_tenantA);
        (await db.Feedback360Releases.AsNoTracking().CountAsync()).Should().Be(0,
            "a release blocked because 360 is disabled must not persist a release row");
    }
}
