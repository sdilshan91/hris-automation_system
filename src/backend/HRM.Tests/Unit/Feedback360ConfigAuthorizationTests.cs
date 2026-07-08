// ============================================================================
// BUG-244 Feedback360: reviewer-CONFIG authorization gate + full-replace save + tracker + tenant flag.
//
// Drives ReviewerAssignmentService through the real EF Core InMemory provider (so reviewer_assignment
// rows are actually persisted) — the same pattern as ReviewerAssignmentServiceTests, with the SAME service
// constructor (no fixture change needed). Covers the BUG-244 additions:
//
//   A. AuthorizeConfigureAsync — the tenant-configurable, team-scoped manager write-authz gate (the security
//      core). Four outcomes exercised:
//        1. HR (Performance.Review.All) may configure ANY employee (even a non-report).
//        2. A manager (Performance.Review.Team) may configure their OWN direct report when the tenant toggle
//           is ON.
//        3. A manager configuring a NON-report (someone else's report), toggle ON → 403 not_team_manager.
//        4. A manager configuring their own report but with the tenant toggle OFF → 403 forbidden.
//        5. A caller with neither permission (Read.Self only) → 403 forbidden.
//        6. The gate is applied to EVERY config-surface method that changed (get/save/tracker/add/remove).
//   B. SaveReviewersAsync full-replace: adds missing, removes absent, leaves Self/Manager untouched; a
//      Completed row cannot be removed (409); BR-2 self-as-peer + BR-3 duplicate still fire.
//   D. GetTrackerAsync: per-category submitted/pending counts, overdue==0, Peer minimum==Min360PeerReviewers.
//   E. Tenant default: a tenant created without touching the flag has AllowManagerReviewerConfig == true, so
//      manager-of-report access works out of the box.
//
// The #3 (manager-of-non-report) and #4 (toggle-off) denials are proven NON-tautological by pairing each with
// the same-shaped ALLOWED case that differs ONLY in the relationship (#2 report vs #3 non-report) or ONLY in
// the tenant flag (#2 toggle-on vs #4 toggle-off).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class Feedback360ConfigAuthorizationTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly IPerformanceNotificationService _notifications;

    private readonly Guid _deptId = Guid.NewGuid();
    private readonly Guid _cycleId = Guid.NewGuid();

    // The manager persona + their org tree.
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _managerEmpId = Guid.NewGuid();
    private readonly Guid _reportEmpId = Guid.NewGuid();     // reports directly to the manager
    private readonly Guid _nonReportEmpId = Guid.NewGuid();  // reports to somebody else — NOT the manager's report
    private readonly Guid _peer1EmpId = Guid.NewGuid();      // a same-dept peer of the report (auto-seed source)
    private readonly Guid _peer2EmpId = Guid.NewGuid();
    private readonly Guid _outsiderEmpId = Guid.NewGuid();   // another dept — used as a manual peer add
    private readonly Guid _otherManagerEmpId = Guid.NewGuid();

    public Feedback360ConfigAuthorizationTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _notifications = Substitute.For<IPerformanceNotificationService>();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private ReviewerAssignmentService CreateService(ICurrentUser user) => new(
        CreateDbContext(),
        _tenantContext,
        user,
        _notifications,
        Substitute.For<ILogger<ReviewerAssignmentService>>());

    /// <summary>HR: holds Performance.Review.All → unrestricted config on any employee.</summary>
    private ICurrentUser HrUser()
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(Guid.NewGuid());
        u.IsAuthenticated.Returns(true);
        u.Email.Returns("hr@acme.com");
        u.Permissions.Returns(new[] { PermissionCatalog.Performance.ReviewAll });
        return u;
    }

    /// <summary>Manager: holds Performance.Review.Team, resolved to the manager employee via UserId.</summary>
    private ICurrentUser ManagerUser()
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(_managerUserId);
        u.IsAuthenticated.Returns(true);
        u.Email.Returns("manager@acme.com");
        u.Permissions.Returns(new[] { PermissionCatalog.Performance.ReviewTeam });
        return u;
    }

    /// <summary>A caller with neither Review.All nor Review.Team (Read.Self only).</summary>
    private ICurrentUser PlainUser()
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(_managerUserId); // even if resolved to the manager employee, no Review.Team ⇒ forbidden
        u.IsAuthenticated.Returns(true);
        u.Permissions.Returns(new[] { PermissionCatalog.Performance.ReadSelf });
        return u;
    }

    /// <param name="allowManagerConfig">
    /// When true (default) the tenant is created WITHOUT touching the flag — exercising the entity default
    /// (AllowManagerReviewerConfig == true). When false the flag is explicitly cleared.
    /// </param>
    private void Seed(bool allowManagerConfig = true, int minPeers = 2)
    {
        using var db = CreateDbContext();

        var tenant = new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" };
        if (!allowManagerConfig)
            tenant.AllowManagerReviewerConfig = false;
        db.Tenants.Add(tenant);

        Emp(db, _managerEmpId, "MGR", "Grace", "Hopper", _deptId, reportsTo: null, userId: _managerUserId);
        Emp(db, _otherManagerEmpId, "MG2", "Barbara", "Liskov", _deptId, reportsTo: null, userId: Guid.NewGuid());
        Emp(db, _reportEmpId, "RPT", "Ada", "Lovelace", _deptId, reportsTo: _managerEmpId, userId: Guid.NewGuid());
        Emp(db, _nonReportEmpId, "NRP", "Alan", "Turing", _deptId, reportsTo: _otherManagerEmpId, userId: Guid.NewGuid());
        Emp(db, _peer1EmpId, "PR1", "Edsger", "Dijkstra", _deptId, reportsTo: _managerEmpId, userId: Guid.NewGuid());
        Emp(db, _peer2EmpId, "PR2", "Donald", "Knuth", _deptId, reportsTo: _managerEmpId, userId: Guid.NewGuid());
        Emp(db, _outsiderEmpId, "OUT", "Linus", "Torvalds", Guid.NewGuid(), reportsTo: null, userId: Guid.NewGuid());

        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleId, TenantId = _tenantId, Name = "FY2026",
            Status = AppraisalCycleStatus.Active,
            StartDate = DateTime.UtcNow.AddDays(-10),
            RatingScaleMax = 5,
            Is360Enabled = true,
            Min360PeerReviewers = minPeers,
            IsDeleted = false,
        });

        db.SaveChanges();
    }

    private void Emp(AppDbContext db, Guid id, string no, string first, string last, Guid deptId, Guid? reportsTo, Guid userId)
        => db.Employees.Add(new Employee
        {
            Id = id, TenantId = _tenantId, UserId = userId, EmployeeNo = no, FirstName = first, LastName = last,
            Email = $"{first.ToLowerInvariant()}@acme.com", DepartmentId = deptId,
            Status = EmployeeStatus.Active, ReportsToEmployeeId = reportsTo, IsDeleted = false,
        });

    private Guid SeedAssignment(Guid revieweeId, Guid reviewerId, ReviewerCategory category,
        ReviewerAssignmentStatus status = ReviewerAssignmentStatus.Pending)
    {
        using var db = CreateDbContext();
        var a = new ReviewerAssignment
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, CycleId = _cycleId,
            RevieweeEmployeeId = revieweeId, ReviewerEmployeeId = reviewerId,
            Category = category, Status = status, IsDeleted = false,
        };
        db.ReviewerAssignments.Add(a);
        db.SaveChanges();
        return a.Id;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  A. AUTHZ MATRIX (the security core)
    // ═══════════════════════════════════════════════════════════════════════

    // ── A1: HR (Review.All) may configure ANY employee, incl. a non-report ──
    [Fact]
    public async Task Hr_WithReviewAll_MayConfigure_AnyEmployee_IncludingNonReport()
    {
        Seed();

        // The non-report is nobody's direct report of the caller; HR still succeeds (unrestricted).
        var result = await CreateService(HrUser())
            .SaveReviewersAsync(_nonReportEmpId, Array.Empty<ReviewerInputDto>());

        result.IsSuccess.Should().BeTrue(result.Error);
    }

    // ── A2: manager may configure their OWN direct report (toggle ON) ───────
    [Fact]
    public async Task Manager_WithReviewTeam_MayConfigure_OwnDirectReport_WhenToggleOn()
    {
        Seed(allowManagerConfig: true);

        // The reviewee reports directly to the manager → allowed; a new peer is actually added.
        var result = await CreateService(ManagerUser())
            .SaveReviewersAsync(_reportEmpId, new[] { new ReviewerInputDto(_outsiderEmpId, ReviewerCategory.Peer) });

        result.IsSuccess.Should().BeTrue(result.Error);

        using var db = CreateDbContext();
        (await db.ReviewerAssignments.AnyAsync(a => a.CycleId == _cycleId
            && a.RevieweeEmployeeId == _reportEmpId && a.ReviewerEmployeeId == _outsiderEmpId
            && a.Category == ReviewerCategory.Peer)).Should().BeTrue("the manager's save actually persisted");
    }

    // ── A3: manager configuring a NON-report → 403 not_team_manager (CRITICAL) ──
    [Fact]
    public async Task Manager_ConfiguringNonReport_IsDenied_NotTeamManager()
    {
        Seed(allowManagerConfig: true);

        var result = await CreateService(ManagerUser())
            .SaveReviewersAsync(_nonReportEmpId, new[] { new ReviewerInputDto(_outsiderEmpId, ReviewerCategory.Peer) });

        // The critical negative: a manager must NOT be able to touch an arbitrary employee's reviewer set.
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("not_team_manager");

        // And nothing leaked into the DB for the non-report.
        using var db = CreateDbContext();
        (await db.ReviewerAssignments.AnyAsync(a => a.RevieweeEmployeeId == _nonReportEmpId))
            .Should().BeFalse("the denied manager write must not have persisted anything");
    }

    // ── A4: manager configuring own report but toggle OFF → 403 forbidden ───
    [Fact]
    public async Task Manager_ConfiguringOwnReport_IsDenied_Forbidden_WhenToggleOff()
    {
        Seed(allowManagerConfig: false);

        // Same manager + same OWN report that succeeds when the toggle is ON (A2) — the ONLY difference is
        // the tenant flag, so this proves the toggle actually governs the outcome.
        var result = await CreateService(ManagerUser())
            .SaveReviewersAsync(_reportEmpId, new[] { new ReviewerInputDto(_outsiderEmpId, ReviewerCategory.Peer) });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("forbidden");

        using var db = CreateDbContext();
        (await db.ReviewerAssignments.AnyAsync(a => a.RevieweeEmployeeId == _reportEmpId))
            .Should().BeFalse("with the toggle off the manager write must not have persisted anything");
    }

    // ── A5: caller with neither permission → 403 forbidden ──────────────────
    [Fact]
    public async Task Caller_WithoutReviewPermission_IsDenied_Forbidden()
    {
        Seed(allowManagerConfig: true);

        // PlainUser resolves to the manager employee but holds only Read.Self — no Review.Team.
        var result = await CreateService(PlainUser())
            .SaveReviewersAsync(_reportEmpId, Array.Empty<ReviewerInputDto>());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("forbidden");
    }

    // ── A6: the gate is applied to EVERY changed config-surface method ───────
    [Fact]
    public async Task ConfigGate_DeniesManagerForNonReport_AcrossAllConfigMethods()
    {
        Seed(allowManagerConfig: true);

        // A pre-existing assignment on the non-report so RemoveReviewer has a real target to authorize against.
        var assignmentId = SeedAssignment(_nonReportEmpId, _outsiderEmpId, ReviewerCategory.Peer);

        var probes = new (string Name, Func<Task<(int? Status, string? Code)>> Invoke)[]
        {
            ("GetConfiguration(cycle-keyed)", async () =>
            {
                var r = await CreateService(ManagerUser()).GetConfigurationAsync(_nonReportEmpId, _cycleId);
                return (r.StatusCode, r.ErrorCode);
            }),
            ("GetConfigurationForActiveCycle", async () =>
            {
                var r = await CreateService(ManagerUser()).GetConfigurationForActiveCycleAsync(_nonReportEmpId);
                return (r.StatusCode, r.ErrorCode);
            }),
            ("SaveReviewers", async () =>
            {
                var r = await CreateService(ManagerUser()).SaveReviewersAsync(_nonReportEmpId, Array.Empty<ReviewerInputDto>());
                return (r.StatusCode, r.ErrorCode);
            }),
            ("GetTracker", async () =>
            {
                var r = await CreateService(ManagerUser()).GetTrackerAsync(_nonReportEmpId);
                return (r.StatusCode, r.ErrorCode);
            }),
            ("AddReviewer", async () =>
            {
                var r = await CreateService(ManagerUser())
                    .AddReviewerAsync(_nonReportEmpId, _cycleId, _peer1EmpId, ReviewerCategory.Peer);
                return (r.StatusCode, r.ErrorCode);
            }),
            ("RemoveReviewer", async () =>
            {
                var r = await CreateService(ManagerUser()).RemoveReviewerAsync(assignmentId);
                return (r.StatusCode, r.ErrorCode);
            }),
        };

        foreach (var (name, invoke) in probes)
        {
            var (status, code) = await invoke();
            status.Should().Be(403, $"{name} must deny a manager configuring a non-report");
            code.Should().Be("not_team_manager", $"{name} must surface the same manager-gate denial");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  B. SaveReviewers (full-replace) behaviour
    // ═══════════════════════════════════════════════════════════════════════

    // ── B7: full-replace adds missing + removes absent; Self/Manager untouched ──
    [Fact]
    public async Task SaveReviewers_FullReplace_AddsMissing_RemovesAbsent_LeavesSelfAndManagerUntouched()
    {
        Seed();

        // First auto-seed Self + Manager + suggested peers/reports so there is real state to replace.
        var seedConfig = await CreateService(HrUser()).GetConfigurationAsync(_reportEmpId, _cycleId);
        seedConfig.IsSuccess.Should().BeTrue(seedConfig.Error);
        var seededPeers = seedConfig.Value!.Assignments
            .Where(a => a.Category == ReviewerCategory.Peer).Select(a => a.ReviewerEmployeeId).ToHashSet();
        seededPeers.Should().Contain(_peer1EmpId).And.Contain(_peer2EmpId);

        // Desired = keep peer1, add the outsider as a NEW peer; peer2 + any auto-seeded direct reports are OMITTED.
        var desired = new[]
        {
            new ReviewerInputDto(_peer1EmpId, ReviewerCategory.Peer),
            new ReviewerInputDto(_outsiderEmpId, ReviewerCategory.Peer),
        };

        var result = await CreateService(HrUser()).SaveReviewersAsync(_reportEmpId, desired);
        result.IsSuccess.Should().BeTrue(result.Error);

        using var db = CreateDbContext();
        var live = await db.ReviewerAssignments.AsNoTracking()
            .Where(a => a.CycleId == _cycleId && a.RevieweeEmployeeId == _reportEmpId).ToListAsync();

        // Peer + DirectReport (client-managed) now reflect EXACTLY the desired set.
        var managedReviewerIds = live
            .Where(a => a.Category is ReviewerCategory.Peer or ReviewerCategory.DirectReport)
            .Select(a => a.ReviewerEmployeeId).ToHashSet();
        managedReviewerIds.Should().BeEquivalentTo(new[] { _peer1EmpId, _outsiderEmpId });

        // The new peer was ADDED, the omitted peer2 was REMOVED (soft-deleted, invisible).
        managedReviewerIds.Should().Contain(_outsiderEmpId);
        managedReviewerIds.Should().NotContain(_peer2EmpId);

        // Self + Manager are server-owned and MUST survive the full-replace.
        live.Should().Contain(a => a.Category == ReviewerCategory.Self && a.ReviewerEmployeeId == _reportEmpId);
        live.Should().Contain(a => a.Category == ReviewerCategory.Manager && a.ReviewerEmployeeId == _managerEmpId);
    }

    // ── B8: a Completed row cannot be removed via the replace (409, kept) ───
    [Fact]
    public async Task SaveReviewers_CannotRemove_CompletedAssignment_ViaReplace()
    {
        Seed();
        await CreateService(HrUser()).GetConfigurationAsync(_reportEmpId, _cycleId); // auto-seed peers

        // Mark peer2's assignment Completed (a reviewer who already submitted).
        Guid peer2AssignmentId;
        using (var db = CreateDbContext())
        {
            var a = await db.ReviewerAssignments.FirstAsync(x =>
                x.RevieweeEmployeeId == _reportEmpId && x.ReviewerEmployeeId == _peer2EmpId
                && x.Category == ReviewerCategory.Peer);
            a.Status = ReviewerAssignmentStatus.Completed;
            peer2AssignmentId = a.Id;
            await db.SaveChangesAsync();
        }

        // Replace OMITTING the completed peer2 → the guard must reject with 409 and keep peer2.
        var result = await CreateService(HrUser())
            .SaveReviewersAsync(_reportEmpId, new[] { new ReviewerInputDto(_peer1EmpId, ReviewerCategory.Peer) });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("already_completed");

        using var verify = CreateDbContext();
        var peer2 = await verify.ReviewerAssignments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == peer2AssignmentId);
        peer2.Should().NotBeNull("a completed assignment must be kept, not silently deleted");
        peer2!.Status.Should().Be(ReviewerAssignmentStatus.Completed);
    }

    // ── B9: BR-2 self-as-peer + BR-3 duplicate still fire through the replace ──
    [Fact]
    public async Task SaveReviewers_Rejects_RevieweeAsOwnPeer_BR2()
    {
        Seed();

        var result = await CreateService(HrUser())
            .SaveReviewersAsync(_reportEmpId, new[] { new ReviewerInputDto(_reportEmpId, ReviewerCategory.Peer) });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("self_as_peer");
    }

    [Fact]
    public async Task SaveReviewers_Rejects_DuplicateReviewerCategory_BR3()
    {
        Seed();

        var result = await CreateService(HrUser()).SaveReviewersAsync(_reportEmpId, new[]
        {
            new ReviewerInputDto(_peer1EmpId, ReviewerCategory.Peer),
            new ReviewerInputDto(_peer1EmpId, ReviewerCategory.Peer),
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("duplicate_reviewer");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  D. Completion tracker
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetTracker_ReturnsPerCategoryCounts_OverdueZero_PeerMinimumOnly()
    {
        Seed(minPeers: 2);

        // Assignments across categories: Self (completed), Manager (pending), 2 Peers (one completed),
        // 1 DirectReport (pending).
        SeedAssignment(_reportEmpId, _reportEmpId, ReviewerCategory.Self, ReviewerAssignmentStatus.Completed);
        SeedAssignment(_reportEmpId, _managerEmpId, ReviewerCategory.Manager);
        SeedAssignment(_reportEmpId, _peer1EmpId, ReviewerCategory.Peer, ReviewerAssignmentStatus.Completed);
        SeedAssignment(_reportEmpId, _peer2EmpId, ReviewerCategory.Peer);
        SeedAssignment(_reportEmpId, _outsiderEmpId, ReviewerCategory.DirectReport);

        var result = await CreateService(HrUser()).GetTrackerAsync(_reportEmpId);

        result.IsSuccess.Should().BeTrue(result.Error);
        var byCat = result.Value!.Categories.ToDictionary(c => c.Category);

        byCat[ReviewerCategory.Self].Submitted.Should().Be(1);
        byCat[ReviewerCategory.Self].Pending.Should().Be(0);

        byCat[ReviewerCategory.Manager].Submitted.Should().Be(0);
        byCat[ReviewerCategory.Manager].Pending.Should().Be(1);

        byCat[ReviewerCategory.Peer].Submitted.Should().Be(1);
        byCat[ReviewerCategory.Peer].Pending.Should().Be(1);

        byCat[ReviewerCategory.DirectReport].Submitted.Should().Be(0);
        byCat[ReviewerCategory.DirectReport].Pending.Should().Be(1);

        // Overdue is always 0 (no 360 window entity yet).
        result.Value!.Categories.Should().OnlyContain(c => c.Overdue == 0);

        // Only the Peer category carries the configured minimum; the rest are 0.
        byCat[ReviewerCategory.Peer].Minimum.Should().Be(2);
        byCat[ReviewerCategory.Self].Minimum.Should().Be(0);
        byCat[ReviewerCategory.Manager].Minimum.Should().Be(0);
        byCat[ReviewerCategory.DirectReport].Minimum.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  E. Tenant setting default
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Tenant_CreatedWithoutTouchingFlag_DefaultsToTrue_ManagerOfReportWorksOutOfBox()
    {
        // Seed() with allowManagerConfig:true does NOT set the property — exercising the entity default.
        Seed(allowManagerConfig: true);

        using (var db = CreateDbContext())
        {
            var persisted = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == _tenantId);
            persisted.AllowManagerReviewerConfig.Should().BeTrue(
                "a tenant created without touching the flag must default to allowing manager reviewer config");
        }

        // Functional proof: the manager can configure their own report out of the box (no admin toggle needed).
        var result = await CreateService(ManagerUser())
            .SaveReviewersAsync(_reportEmpId, Array.Empty<ReviewerInputDto>());

        result.IsSuccess.Should().BeTrue(result.Error);
    }
}
