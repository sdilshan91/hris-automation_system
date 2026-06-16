// ============================================================================
// US-PRF-005: 360-degree reviewer assignment — service unit tests.
//
// Drives ReviewerAssignmentService through the real EF Core InMemory provider (so the
// reviewer_assignment rows are actually persisted). Covers the configuration rules:
//   - AC-1/FR-2: auto-assignment seeds Self + Manager + suggested peers (same dept) + direct reports.
//   - FR-2: suggested peers/reports already assigned are not re-offered as suggestions.
//   - AC-1/FR-2: manual add reviewer + manual remove reviewer.
//   - BR-2: an employee cannot be their own Peer (auto-seed skips it AND manual add is rejected).
//   - FR-3: a minimum number of reviewers per category can be assigned (multiple peers).
//   - BR-1: configuration/add rejected when the cycle's 360 toggle is off.
//   - permission gate: a caller without Performance.Review.All is forbidden.
//   - AC-2: notifying reviewers stamps NotifiedAt and dispatches once per pending reviewer.
//   - a completed reviewer cannot be removed.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
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

public sealed class ReviewerAssignmentServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _hrUserId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _hrUser;
    private readonly IPerformanceNotificationService _notifications;

    private readonly Guid _deptId = Guid.NewGuid();
    private readonly Guid _otherDeptId = Guid.NewGuid();
    private readonly Guid _managerEmpId = Guid.NewGuid();
    private readonly Guid _revieweeEmpId = Guid.NewGuid();
    private readonly Guid _peer1EmpId = Guid.NewGuid();
    private readonly Guid _peer2EmpId = Guid.NewGuid();
    private readonly Guid _reportEmpId = Guid.NewGuid();   // a direct report of the reviewee
    private readonly Guid _outsiderEmpId = Guid.NewGuid();  // another department — never suggested
    private readonly Guid _cycleId = Guid.NewGuid();

    public ReviewerAssignmentServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _notifications = Substitute.For<IPerformanceNotificationService>();

        _hrUser = Substitute.For<ICurrentUser>();
        _hrUser.UserId.Returns(_hrUserId);
        _hrUser.IsAuthenticated.Returns(true);
        _hrUser.Email.Returns("hr@acme.com");
        _hrUser.Permissions.Returns(new[] { PermissionCatalog.Performance.ReviewAll });
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private ReviewerAssignmentService CreateService(ICurrentUser? user = null) => new(
        CreateDbContext(),
        _tenantContext,
        user ?? _hrUser,
        _notifications,
        Substitute.For<ILogger<ReviewerAssignmentService>>());

    private ICurrentUser PlainUser()
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(Guid.NewGuid());
        u.IsAuthenticated.Returns(true);
        u.Permissions.Returns(new[] { PermissionCatalog.Performance.ViewOwn });
        return u;
    }

    /// <param name="is360Enabled">when false the cycle's 360 toggle is off (BR-1).</param>
    private void Seed(bool is360Enabled = true, bool isAnonymous = false, int minPeers = 2)
    {
        using var db = CreateDbContext();
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });

        Emp(db, _managerEmpId, "MGR", "Grace", "Hopper", _deptId, reportsTo: null);
        Emp(db, _revieweeEmpId, "RVW", "Ada", "Lovelace", _deptId, reportsTo: _managerEmpId);
        Emp(db, _peer1EmpId, "PR1", "Alan", "Turing", _deptId, reportsTo: _managerEmpId);
        Emp(db, _peer2EmpId, "PR2", "Edsger", "Dijkstra", _deptId, reportsTo: _managerEmpId);
        Emp(db, _reportEmpId, "RPT", "Donald", "Knuth", _deptId, reportsTo: _revieweeEmpId);
        Emp(db, _outsiderEmpId, "OUT", "Linus", "Torvalds", _otherDeptId, reportsTo: null);

        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleId, TenantId = _tenantId, Name = "FY2026",
            Status = AppraisalCycleStatus.Active,
            RatingScaleMax = 5,
            Is360Enabled = is360Enabled,
            IsAnonymousFeedback = isAnonymous,
            Min360PeerReviewers = minPeers,
            IsDeleted = false,
        });

        db.SaveChanges();
    }

    private void Emp(AppDbContext db, Guid id, string no, string first, string last, Guid deptId, Guid? reportsTo)
        => db.Employees.Add(new Employee
        {
            Id = id, TenantId = _tenantId, EmployeeNo = no, FirstName = first, LastName = last,
            Email = $"{first.ToLowerInvariant()}@acme.com", DepartmentId = deptId,
            Status = EmployeeStatus.Active, ReportsToEmployeeId = reportsTo, IsDeleted = false,
        });

    // ── AC-1/FR-2: auto-assignment seeds Self + Manager + peers + reports ──

    [Fact]
    public async Task GetConfiguration_AutoSeeds_Self_Manager_Peers_AndDirectReports()
    {
        Seed();

        var result = await CreateService().GetConfigurationAsync(_revieweeEmpId, _cycleId);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;

        // Self auto-assigned (the reviewee reviewing themselves).
        dto.Assignments.Should().ContainSingle(a =>
            a.Category == ReviewerCategory.Self && a.ReviewerEmployeeId == _revieweeEmpId);

        // Manager auto-assigned from the org tree.
        dto.Assignments.Should().ContainSingle(a =>
            a.Category == ReviewerCategory.Manager && a.ReviewerEmployeeId == _managerEmpId);

        // Both same-department peers auto-suggested as assignments (not the reviewee — BR-2).
        dto.Assignments.Where(a => a.Category == ReviewerCategory.Peer)
            .Select(a => a.ReviewerEmployeeId)
            .Should().Contain(new[] { _peer1EmpId, _peer2EmpId, _managerEmpId })
            .And.NotContain(_revieweeEmpId);

        // The direct report auto-suggested.
        dto.Assignments.Should().Contain(a =>
            a.Category == ReviewerCategory.DirectReport && a.ReviewerEmployeeId == _reportEmpId);

        // An employee in another department is never auto-assigned.
        dto.Assignments.Should().NotContain(a => a.ReviewerEmployeeId == _outsiderEmpId);
    }

    [Fact]
    public async Task GetConfiguration_IsIdempotent_DoesNotDuplicateAutoAssignments()
    {
        Seed();
        var svc = CreateService();

        var first = await svc.GetConfigurationAsync(_revieweeEmpId, _cycleId);
        var second = await CreateService().GetConfigurationAsync(_revieweeEmpId, _cycleId);

        first.Value!.Assignments.Count.Should().Be(second.Value!.Assignments.Count);

        using var db = CreateDbContext();
        var selfCount = await db.ReviewerAssignments
            .CountAsync(a => a.CycleId == _cycleId && a.RevieweeEmployeeId == _revieweeEmpId
                && a.Category == ReviewerCategory.Self);
        selfCount.Should().Be(1, "auto-seed must not duplicate the Self assignment on re-open");
    }

    // ── FR-3: minimum reviewers per category — multiple peers can co-exist ──

    [Fact]
    public async Task AutoSeed_AssignsAllPeerCandidates_MeetingMinimumPeerCategory()
    {
        Seed(minPeers: 2);

        var result = await CreateService().GetConfigurationAsync(_revieweeEmpId, _cycleId);

        var peerCount = result.Value!.Assignments.Count(a => a.Category == ReviewerCategory.Peer);
        peerCount.Should().BeGreaterThanOrEqualTo(2, "two same-department peers exist (FR-3 minimum)");
    }

    // ── FR-2: already-assigned candidates are not offered as suggestions ──

    [Fact]
    public async Task GetConfiguration_DoesNotSuggest_AlreadyAssignedPeers()
    {
        Seed();

        // First access auto-seeds peers as assignments; so they must not also appear as suggestions.
        var result = await CreateService().GetConfigurationAsync(_revieweeEmpId, _cycleId);

        var assignedPeerIds = result.Value!.Assignments
            .Where(a => a.Category == ReviewerCategory.Peer).Select(a => a.ReviewerEmployeeId).ToHashSet();
        result.Value!.SuggestedPeers.Select(s => s.EmployeeId)
            .Should().NotIntersectWith(assignedPeerIds);
    }

    // ── AC-1/FR-2: manual add reviewer ──────────────────────────────────

    [Fact]
    public async Task AddReviewer_Peer_Persists()
    {
        Seed();

        // Use the outsider (different department, never auto-seeded) as a manual peer add.
        var result = await CreateService().AddReviewerAsync(
            _revieweeEmpId, _cycleId, _outsiderEmpId, ReviewerCategory.Peer);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Category.Should().Be(ReviewerCategory.Peer);
        result.Value!.ReviewerEmployeeId.Should().Be(_outsiderEmpId);

        using var verify = CreateDbContext();
        (await verify.ReviewerAssignments.AnyAsync(a => a.CycleId == _cycleId
            && a.ReviewerEmployeeId == _outsiderEmpId && a.Category == ReviewerCategory.Peer))
            .Should().BeTrue();
    }

    [Fact]
    public async Task AddReviewer_Duplicate_InSameCategory_Rejected()
    {
        Seed();
        (await CreateService().AddReviewerAsync(_revieweeEmpId, _cycleId, _outsiderEmpId, ReviewerCategory.Peer))
            .IsSuccess.Should().BeTrue();

        var dup = await CreateService().AddReviewerAsync(
            _revieweeEmpId, _cycleId, _outsiderEmpId, ReviewerCategory.Peer);

        dup.IsFailure.Should().BeTrue();
        dup.StatusCode.Should().Be(409);
        dup.ErrorCode.Should().Be("duplicate_reviewer");
    }

    // ── BR-2: an employee cannot be their own Peer ──────────────────────

    [Fact]
    public async Task AddReviewer_RejectsRevieweeAsTheirOwnPeer()
    {
        Seed();

        var result = await CreateService().AddReviewerAsync(
            _revieweeEmpId, _cycleId, _revieweeEmpId, ReviewerCategory.Peer);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("self_as_peer");
    }

    [Fact]
    public async Task AutoSeed_NeverAssigns_RevieweeAsTheirOwnPeer()
    {
        Seed();

        var result = await CreateService().GetConfigurationAsync(_revieweeEmpId, _cycleId);

        result.Value!.Assignments.Should().NotContain(a =>
            a.Category == ReviewerCategory.Peer && a.ReviewerEmployeeId == _revieweeEmpId);
    }

    // ── AC-1/FR-2: manual remove reviewer ───────────────────────────────

    [Fact]
    public async Task RemoveReviewer_SoftDeletesAssignment()
    {
        Seed();
        var config = await CreateService().GetConfigurationAsync(_revieweeEmpId, _cycleId);
        var peer = config.Value!.Assignments.First(a => a.Category == ReviewerCategory.Peer);

        var result = await CreateService().RemoveReviewerAsync(peer.Id);

        result.IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        // The global query filter + soft-delete filter hides it from normal queries.
        (await db.ReviewerAssignments.AnyAsync(a => a.Id == peer.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveReviewer_Rejected_WhenReviewerAlreadyCompleted()
    {
        Seed();
        var config = await CreateService().GetConfigurationAsync(_revieweeEmpId, _cycleId);
        var peer = config.Value!.Assignments.First(a => a.Category == ReviewerCategory.Peer);

        // Mark the assignment Completed directly (a reviewer who already submitted).
        using (var db = CreateDbContext())
        {
            var a = await db.ReviewerAssignments.FirstAsync(x => x.Id == peer.Id);
            a.Status = ReviewerAssignmentStatus.Completed;
            await db.SaveChangesAsync();
        }

        var result = await CreateService().RemoveReviewerAsync(peer.Id);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("already_completed");
    }

    // ── BR-1: 360 toggle gates configuration + add ──────────────────────

    [Fact]
    public async Task GetConfiguration_Rejected_When360Disabled()
    {
        Seed(is360Enabled: false);

        var result = await CreateService().GetConfigurationAsync(_revieweeEmpId, _cycleId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("not_360_enabled");
    }

    [Fact]
    public async Task AddReviewer_Rejected_When360Disabled()
    {
        Seed(is360Enabled: false);

        var result = await CreateService().AddReviewerAsync(
            _revieweeEmpId, _cycleId, _outsiderEmpId, ReviewerCategory.Peer);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("not_360_enabled");
    }

    // ── permission gate ─────────────────────────────────────────────────

    [Fact]
    public async Task GetConfiguration_Forbidden_WithoutReviewAll()
    {
        Seed();

        var result = await CreateService(PlainUser()).GetConfigurationAsync(_revieweeEmpId, _cycleId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("forbidden");
    }

    [Fact]
    public async Task AddReviewer_Forbidden_WithoutReviewAll()
    {
        Seed();

        var result = await CreateService(PlainUser()).AddReviewerAsync(
            _revieweeEmpId, _cycleId, _outsiderEmpId, ReviewerCategory.Peer);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
    }

    // ── AC-2: notify reviewers stamps NotifiedAt + dispatches once each ──

    [Fact]
    public async Task NotifyReviewers_StampsNotifiedAt_AndDispatchesPerPendingReviewer()
    {
        Seed();
        var config = await CreateService().GetConfigurationAsync(_revieweeEmpId, _cycleId);
        var pendingCount = config.Value!.Assignments.Count(a => a.Status == ReviewerAssignmentStatus.Pending);
        pendingCount.Should().BeGreaterThan(0);

        var result = await CreateService().NotifyReviewersAsync(_revieweeEmpId, _cycleId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(pendingCount);

        await _notifications.Received(pendingCount).NotifyReviewerAssignedAsync(
            Arg.Any<Guid>(), _revieweeEmpId, _cycleId, Arg.Any<CancellationToken>());

        using var db = CreateDbContext();
        (await db.ReviewerAssignments
            .Where(a => a.CycleId == _cycleId && a.RevieweeEmployeeId == _revieweeEmpId)
            .AllAsync(a => a.NotifiedAt != null))
            .Should().BeTrue();
    }
}
