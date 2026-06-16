// ============================================================================
// US-PRF-005: 360-degree feedback submission + aggregation — service unit tests.
//
// Drives Feedback360Service through the real EF Core InMemory provider (so feedback_360 +
// feedback_360_item rows are actually persisted). Covers the core rules:
//   - AC-3: submit persists the feedback, marks the reviewer assignment Completed, captures anonymity.
//   - BR-3: one feedback per reviewer per reviewee per cycle (the second submit is rejected).
//   - AC-3 gate: a reviewer with no Pending assignment may not submit (not_assigned).
//   - BR-1: submit rejected when the cycle's 360 toggle is off.
//   - FR-4: a rating outside the cycle scale is rejected (rating_out_of_range).
//   - AC-4/FR-6: results carry per-competency + per-category averages and the weighted composite score.
//   - FR-5/NFR-3 (ANONYMITY): when anonymity is on, the results entries carry NO reviewer id / name.
//   - FR-5/NFR-3 (NON-anon): when anonymity is off, the reviewer id IS present.
//   - BR-4: results warn (do not throw) when fewer than the minimum peers have submitted.
//   - permission gate: results require Performance.Review.All.
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
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class Feedback360ServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;

    private readonly Guid _revieweeEmpId = Guid.NewGuid();
    private readonly Guid _managerEmpId = Guid.NewGuid();
    private readonly Guid _peer1EmpId = Guid.NewGuid();
    private readonly Guid _peer2EmpId = Guid.NewGuid();
    private readonly Guid _peer1UserId = Guid.NewGuid();
    private readonly Guid _peer2UserId = Guid.NewGuid();
    private readonly Guid _hrUserId = Guid.NewGuid();
    private readonly Guid _cycleId = Guid.NewGuid();

    private const string Communication = "Communication";
    private const string Leadership = "Leadership";

    public Feedback360ServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private Feedback360Service CreateService(ICurrentUser user) => new(
        CreateDbContext(),
        _tenantContext,
        user,
        Substitute.For<ILogger<Feedback360Service>>());

    /// <summary>A reviewer caller resolved to a peer employee via UserId (the submit path).</summary>
    private ICurrentUser ReviewerUser(Guid userId)
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(userId);
        u.IsAuthenticated.Returns(true);
        u.Permissions.Returns(new[] { PermissionCatalog.Performance.ViewOwn });
        return u;
    }

    /// <summary>An HR caller with Review.All — the only role permitted to read aggregated results.</summary>
    private ICurrentUser HrUser()
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(_hrUserId);
        u.IsAuthenticated.Returns(true);
        u.Permissions.Returns(new[] { PermissionCatalog.Performance.ReviewAll });
        return u;
    }

    /// <param name="is360Enabled">when false the cycle's 360 toggle is off (BR-1).</param>
    /// <param name="isAnonymous">when true feedback is captured anonymously (BR-5/FR-5).</param>
    /// <param name="minPeers">BR-4 minimum-peer release gate.</param>
    private void Seed(bool is360Enabled = true, bool isAnonymous = false, int minPeers = 2)
    {
        using var db = CreateDbContext();
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });

        Emp(db, _revieweeEmpId, null, "RVW", "Ada", "Lovelace");
        Emp(db, _managerEmpId, null, "MGR", "Grace", "Hopper");
        Emp(db, _peer1EmpId, _peer1UserId, "PR1", "Alan", "Turing");
        Emp(db, _peer2EmpId, _peer2UserId, "PR2", "Edsger", "Dijkstra");

        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleId, TenantId = _tenantId, Name = "FY2026",
            Status = AppraisalCycleStatus.Active,
            RatingScaleMax = 5,
            Is360Enabled = is360Enabled,
            IsAnonymousFeedback = isAnonymous,
            Min360PeerReviewers = minPeers,
            // Default 360 weights: self 10 / manager 40 / peer 30 / report 20.
            ThreeSixtySelfWeightPercent = 10,
            ThreeSixtyManagerWeightPercent = 40,
            ThreeSixtyPeerWeightPercent = 30,
            ThreeSixtyReportWeightPercent = 20,
            IsDeleted = false,
        });

        db.SaveChanges();
    }

    private void Emp(AppDbContext db, Guid id, Guid? userId, string no, string first, string last)
        => db.Employees.Add(new Employee
        {
            Id = id, TenantId = _tenantId, UserId = userId, EmployeeNo = no,
            FirstName = first, LastName = last, Email = $"{first.ToLowerInvariant()}@acme.com",
            Status = EmployeeStatus.Active, IsDeleted = false,
        });

    /// <summary>Seeds a Pending reviewer assignment so the reviewer is allowed to submit (AC-3).</summary>
    private void Assign(Guid reviewerEmpId, ReviewerCategory category)
    {
        using var db = CreateDbContext();
        db.ReviewerAssignments.Add(new ReviewerAssignment
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, CycleId = _cycleId,
            RevieweeEmployeeId = _revieweeEmpId, ReviewerEmployeeId = reviewerEmpId,
            Category = category, Status = ReviewerAssignmentStatus.Pending, IsDeleted = false,
        });
        db.SaveChanges();
    }

    private SubmitFeedback360Input Submit(params (string Key, int Rating)[] items)
        => new(_cycleId, _revieweeEmpId, "Solid work this cycle.",
            items.Select(i => new Feedback360ItemInput { CompetencyKey = i.Key, Rating = i.Rating }).ToList());

    // ── AC-3: submit persists + marks the assignment Completed ──────────

    [Fact]
    public async Task Submit_PersistsFeedback_AndMarksAssignmentCompleted()
    {
        Seed();
        Assign(_peer1EmpId, ReviewerCategory.Peer);

        var result = await CreateService(ReviewerUser(_peer1UserId))
            .SubmitFeedbackAsync(Submit((Communication, 4), (Leadership, 5)));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Category.Should().Be(ReviewerCategory.Peer);
        result.Value!.Items.Should().HaveCount(2);

        using var db = CreateDbContext();
        (await db.Feedback360s.CountAsync(f => f.CycleId == _cycleId
            && f.RevieweeEmployeeId == _revieweeEmpId && f.ReviewerEmployeeId == _peer1EmpId))
            .Should().Be(1);
        (await db.ReviewerAssignments.FirstAsync(a => a.ReviewerEmployeeId == _peer1EmpId))
            .Status.Should().Be(ReviewerAssignmentStatus.Completed);
    }

    // ── BR-3: one feedback per reviewer per reviewee per cycle ──────────

    [Fact]
    public async Task Submit_Duplicate_BySameReviewer_Rejected()
    {
        Seed();
        Assign(_peer1EmpId, ReviewerCategory.Peer);

        (await CreateService(ReviewerUser(_peer1UserId)).SubmitFeedbackAsync(Submit((Communication, 4))))
            .IsSuccess.Should().BeTrue();

        var second = await CreateService(ReviewerUser(_peer1UserId))
            .SubmitFeedbackAsync(Submit((Communication, 3)));

        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("already_submitted");

        using var db = CreateDbContext();
        (await db.Feedback360s.CountAsync(f => f.ReviewerEmployeeId == _peer1EmpId))
            .Should().Be(1, "BR-3: only one feedback per reviewer per reviewee per cycle");
    }

    // ── AC-3 gate: an unassigned reviewer cannot submit ─────────────────

    [Fact]
    public async Task Submit_Rejected_WhenReviewerNotAssigned()
    {
        Seed();
        // No assignment seeded for peer1.

        var result = await CreateService(ReviewerUser(_peer1UserId))
            .SubmitFeedbackAsync(Submit((Communication, 4)));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("not_assigned");
    }

    // ── BR-1: 360 toggle gates submit ───────────────────────────────────

    [Fact]
    public async Task Submit_Rejected_When360Disabled()
    {
        Seed(is360Enabled: false);
        Assign(_peer1EmpId, ReviewerCategory.Peer);

        var result = await CreateService(ReviewerUser(_peer1UserId))
            .SubmitFeedbackAsync(Submit((Communication, 4)));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("not_360_enabled");
    }

    // ── FR-4: rating within the cycle scale ─────────────────────────────

    [Fact]
    public async Task Submit_Rejected_WhenRatingExceedsScale()
    {
        Seed();
        Assign(_peer1EmpId, ReviewerCategory.Peer);

        var result = await CreateService(ReviewerUser(_peer1UserId))
            .SubmitFeedbackAsync(Submit((Communication, 9)));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("rating_out_of_range");
    }

    // ── AC-4/FR-6: aggregated results — averages + composite score ──────

    [Fact]
    public async Task Results_AggregatesPerCompetency_PerCategory_AndComposite()
    {
        Seed(minPeers: 1);

        // Self rates 4, Manager rates 5, two peers rate 4 and 2 (peer avg 3).
        Assign(_revieweeEmpId, ReviewerCategory.Self);
        Assign(_managerEmpId, ReviewerCategory.Manager);
        Assign(_peer1EmpId, ReviewerCategory.Peer);
        Assign(_peer2EmpId, ReviewerCategory.Peer);

        // Reviewee submits Self via a reviewee user.
        var revieweeUser = ReviewerUser(Guid.NewGuid());
        using (var db = CreateDbContext())
        {
            var rvw = await db.Employees.FirstAsync(e => e.Id == _revieweeEmpId);
            rvw.UserId = revieweeUser.UserId;
            var mgr = await db.Employees.FirstAsync(e => e.Id == _managerEmpId);
            mgr.UserId = Guid.NewGuid();
            await db.SaveChangesAsync();
        }

        await CreateService(revieweeUser).SubmitFeedbackAsync(Submit((Communication, 4)));

        // Manager submits 5.
        var mgrUserId = await GetUserIdAsync(_managerEmpId);
        await CreateService(ReviewerUser(mgrUserId)).SubmitFeedbackAsync(Submit((Communication, 5)));

        // Peers submit 4 and 2 → peer average 3.
        await CreateService(ReviewerUser(_peer1UserId)).SubmitFeedbackAsync(Submit((Communication, 4)));
        await CreateService(ReviewerUser(_peer2UserId)).SubmitFeedbackAsync(Submit((Communication, 2)));

        var results = await CreateService(HrUser()).GetResultsAsync(_revieweeEmpId, _cycleId);

        results.IsSuccess.Should().BeTrue(results.Error);
        var dto = results.Value!;

        // Per-category averages.
        Cat(dto, ReviewerCategory.Self).AverageRating.Should().Be(4m);
        Cat(dto, ReviewerCategory.Manager).AverageRating.Should().Be(5m);
        Cat(dto, ReviewerCategory.Peer).AverageRating.Should().Be(3m);
        Cat(dto, ReviewerCategory.DirectReport).AverageRating.Should().BeNull("no direct reports submitted");

        // Composite = (4*10 + 5*40 + 3*30) / (10+40+30) = (40+200+90)/80 = 330/80 = 4.125 → 4.13 (away-from-zero).
        dto.CompositeScore.Should().Be(4.13m);

        // Per-competency average for "Communication": (4+5+4+2)/4 = 3.75.
        dto.CompetencyAverages.Should().ContainSingle(c => c.CompetencyKey == Communication)
            .Which.AverageRating.Should().Be(3.75m);
    }

    // ── FR-5/NFR-3: ANONYMITY — reviewer id absent from results ─────────

    [Fact]
    public async Task Results_WhenAnonymous_OmitReviewerIdentity()
    {
        Seed(isAnonymous: true, minPeers: 1);
        Assign(_peer1EmpId, ReviewerCategory.Peer);

        await CreateService(ReviewerUser(_peer1UserId)).SubmitFeedbackAsync(Submit((Communication, 4)));

        var results = await CreateService(HrUser()).GetResultsAsync(_revieweeEmpId, _cycleId);

        results.IsSuccess.Should().BeTrue(results.Error);
        results.Value!.IsAnonymousFeedback.Should().BeTrue();
        results.Value!.Entries.Should().NotBeEmpty();
        // NFR-3: NEITHER the reviewer id NOR the name may appear in the payload.
        results.Value!.Entries.Should().OnlyContain(e =>
            e.IsAnonymous && e.ReviewerEmployeeId == null && e.ReviewerName == null);

        // And the actual reviewer's id must not leak anywhere in any entry.
        results.Value!.Entries.Select(e => e.ReviewerEmployeeId)
            .Should().NotContain(_peer1EmpId);
    }

    [Fact]
    public async Task Results_WhenNotAnonymous_RevealReviewerIdentity()
    {
        Seed(isAnonymous: false, minPeers: 1);
        Assign(_peer1EmpId, ReviewerCategory.Peer);

        await CreateService(ReviewerUser(_peer1UserId)).SubmitFeedbackAsync(Submit((Communication, 4)));

        var results = await CreateService(HrUser()).GetResultsAsync(_revieweeEmpId, _cycleId);

        results.IsSuccess.Should().BeTrue(results.Error);
        var entry = results.Value!.Entries.Single();
        entry.IsAnonymous.Should().BeFalse();
        entry.ReviewerEmployeeId.Should().Be(_peer1EmpId);
        entry.ReviewerName.Should().Be("Alan Turing");
    }

    // ── BR-4: minimum-peer release gate warns (does not throw) ──────────

    [Fact]
    public async Task Results_Warn_WhenFewerThanMinimumPeersSubmitted()
    {
        Seed(minPeers: 2);
        Assign(_peer1EmpId, ReviewerCategory.Peer);

        // Only ONE peer submits — below the minimum of 2.
        await CreateService(ReviewerUser(_peer1UserId)).SubmitFeedbackAsync(Submit((Communication, 4)));

        var results = await CreateService(HrUser()).GetResultsAsync(_revieweeEmpId, _cycleId);

        results.IsSuccess.Should().BeTrue();
        var dto = results.Value!;
        dto.MinPeerReviewers.Should().Be(2);
        dto.PeerResponseCount.Should().Be(1);
        dto.MinPeerThresholdMet.Should().BeFalse();
        dto.ReleaseWarning.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Results_NoWarning_WhenMinimumPeersMet()
    {
        Seed(minPeers: 2);
        Assign(_peer1EmpId, ReviewerCategory.Peer);
        Assign(_peer2EmpId, ReviewerCategory.Peer);

        await CreateService(ReviewerUser(_peer1UserId)).SubmitFeedbackAsync(Submit((Communication, 4)));
        await CreateService(ReviewerUser(_peer2UserId)).SubmitFeedbackAsync(Submit((Communication, 3)));

        var results = await CreateService(HrUser()).GetResultsAsync(_revieweeEmpId, _cycleId);

        results.Value!.MinPeerThresholdMet.Should().BeTrue();
        results.Value!.ReleaseWarning.Should().BeNull();
    }

    // ── permission gate: results require Review.All ─────────────────────

    [Fact]
    public async Task Results_Forbidden_WithoutReviewAll()
    {
        Seed(minPeers: 1);
        Assign(_peer1EmpId, ReviewerCategory.Peer);
        await CreateService(ReviewerUser(_peer1UserId)).SubmitFeedbackAsync(Submit((Communication, 4)));

        var results = await CreateService(ReviewerUser(_peer1UserId)).GetResultsAsync(_revieweeEmpId, _cycleId);

        results.IsFailure.Should().BeTrue();
        results.StatusCode.Should().Be(403);
        results.ErrorCode.Should().Be("forbidden");
    }

    // ── completion tracker reflects submitted vs assigned ───────────────

    [Fact]
    public async Task Results_CompletionTracker_CountsAssignedAndCompleted()
    {
        Seed(minPeers: 1);
        Assign(_peer1EmpId, ReviewerCategory.Peer);
        Assign(_peer2EmpId, ReviewerCategory.Peer);

        // Only peer1 submits.
        await CreateService(ReviewerUser(_peer1UserId)).SubmitFeedbackAsync(Submit((Communication, 4)));

        var results = await CreateService(HrUser()).GetResultsAsync(_revieweeEmpId, _cycleId);

        var peerCompletion = results.Value!.Completion.Single(c => c.Category == ReviewerCategory.Peer);
        peerCompletion.Assigned.Should().Be(2);
        peerCompletion.Completed.Should().Be(1);
    }

    private static CategoryAverageDto Cat(Feedback360ResultsDto dto, ReviewerCategory cat)
        => dto.CategoryAverages.Single(c => c.Category == cat);

    private async Task<Guid> GetUserIdAsync(Guid empId)
    {
        using var db = CreateDbContext();
        var e = await db.Employees.FirstAsync(x => x.Id == empId);
        return e.UserId!.Value;
    }
}
