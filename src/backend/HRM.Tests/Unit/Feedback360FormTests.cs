// ============================================================================
// BUG-244 Feedback360 #2: reviewer feedback FORM load + submit-by-assignment (RLS + round-trip).
//
// Drives Feedback360Service through the real EF Core InMemory provider (same pattern + same constructor as
// Feedback360ServiceTests — no fixture change). Covers the BUG-244 #2 additions:
//   10. The assigned reviewer GETs the form → 200, questions projected from the reviewee's goals,
//       submitted=false, ratings null.
//   11. A DIFFERENT employee (not the assignment's reviewer) GETs the form → 403 not_assigned (RLS).
//   12. Round-trip: reviewer loads the form, POSTs submit-by-assignment with answers → success; re-loading the
//       form shows submitted=true with the rating/comment hydrated back BY GOAL ID. A non-owner submit → 403
//       not_assigned.
//
// The not_assigned denials are genuine: the caller resolves to a real (but wrong) employee, so the 403 comes
// from the reviewer-identity RLS check, not from a missing employee record.
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

public sealed class Feedback360FormTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;

    private readonly Guid _cycleId = Guid.NewGuid();
    private readonly Guid _revieweeEmpId = Guid.NewGuid();
    private readonly Guid _reviewerEmpId = Guid.NewGuid();
    private readonly Guid _reviewerUserId = Guid.NewGuid();
    private readonly Guid _otherEmpId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();
    private readonly Guid _goal1Id = Guid.NewGuid();
    private readonly Guid _goal2Id = Guid.NewGuid();

    private Guid _assignmentId;

    public Feedback360FormTests()
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
        Substitute.For<IPerformanceNotificationService>(),
        Substitute.For<ILogger<Feedback360Service>>());

    private ICurrentUser ReviewerUser(Guid userId)
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(userId);
        u.IsAuthenticated.Returns(true);
        u.Permissions.Returns(new[] { PermissionCatalog.Performance.ViewOwn });
        return u;
    }

    private void Seed()
    {
        using var db = CreateDbContext();
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });

        Emp(db, _revieweeEmpId, null, "RVW", "Ada", "Lovelace");
        Emp(db, _reviewerEmpId, _reviewerUserId, "PR1", "Alan", "Turing");
        Emp(db, _otherEmpId, _otherUserId, "PR2", "Edsger", "Dijkstra");

        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleId, TenantId = _tenantId, Name = "FY2026",
            Status = AppraisalCycleStatus.Active,
            StartDate = DateTime.UtcNow.AddDays(-10),
            RatingScaleMax = 5,
            Is360Enabled = true,
            Min360PeerReviewers = 1,
            IsDeleted = false,
        });

        // The reviewee's goals for this cycle — the source of the form questions.
        db.Goals.AddRange(
            Goal(_goal1Id, "Ship the platform", "Deliver v1 by Q4"),
            Goal(_goal2Id, "Mentor the team", "Grow two juniors"));

        // A Pending Peer assignment: reviewer (peer1) reviews the reviewee.
        var assignment = new ReviewerAssignment
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, CycleId = _cycleId,
            RevieweeEmployeeId = _revieweeEmpId, ReviewerEmployeeId = _reviewerEmpId,
            Category = ReviewerCategory.Peer, Status = ReviewerAssignmentStatus.Pending, IsDeleted = false,
        };
        db.ReviewerAssignments.Add(assignment);

        db.SaveChanges();
        _assignmentId = assignment.Id;
    }

    private void Emp(AppDbContext db, Guid id, Guid? userId, string no, string first, string last)
        => db.Employees.Add(new Employee
        {
            Id = id, TenantId = _tenantId, UserId = userId, EmployeeNo = no,
            FirstName = first, LastName = last, Email = $"{first.ToLowerInvariant()}@acme.com",
            Status = EmployeeStatus.Active, IsDeleted = false,
        });

    private Goal Goal(Guid id, string title, string description)
        => new()
        {
            Id = id, TenantId = _tenantId, CycleId = _cycleId, EmployeeId = _revieweeEmpId,
            Title = title, Description = description, Category = GoalCategory.Kpi, Weight = 50,
            TargetValue = "100%", MeasurementUnit = "%", DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Status = GoalStatus.Acknowledged, IsDeleted = false,
        };

    // ── 10: the assigned reviewer loads the form → 200, questions from goals, unsubmitted ──
    [Fact]
    public async Task GetFeedbackForm_AssignedReviewer_ProjectsGoals_Unsubmitted()
    {
        Seed();

        var result = await CreateService(ReviewerUser(_reviewerUserId)).GetFeedbackFormAsync(_assignmentId);

        result.IsSuccess.Should().BeTrue(result.Error);
        var form = result.Value!;
        form.AssignmentId.Should().Be(_assignmentId);
        form.RevieweeId.Should().Be(_revieweeEmpId);
        form.Category.Should().Be(ReviewerCategory.Peer);
        form.Submitted.Should().BeFalse();

        // Questions projected from the reviewee's two goals, keyed by goal id, no ratings yet.
        form.Questions.Select(q => q.QuestionId).Should().BeEquivalentTo(new[] { _goal1Id, _goal2Id });
        form.Questions.Should().OnlyContain(q => q.Rating == null);
    }

    // ── 11: a different employee (not the reviewer) is denied — RLS not_assigned ──
    [Fact]
    public async Task GetFeedbackForm_NonReviewer_IsDenied_NotAssigned()
    {
        Seed();

        // The "other" employee resolves to a real employee record, but is NOT this assignment's reviewer.
        var result = await CreateService(ReviewerUser(_otherUserId)).GetFeedbackFormAsync(_assignmentId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("not_assigned");
    }

    // ── 12: round-trip — load, submit-by-assignment, reload shows hydrated + locked ──
    [Fact]
    public async Task SubmitByAssignment_RoundTrips_HydratesAnswersByGoalId_AndLocksForm()
    {
        Seed();

        // Reviewer loads the (empty) form first (#2 form projection).
        (await CreateService(ReviewerUser(_reviewerUserId)).GetFeedbackFormAsync(_assignmentId))
            .Value!.Submitted.Should().BeFalse();

        // Submit answers keyed by goal id.
        var answers = new[]
        {
            new FeedbackAnswerInput { QuestionId = _goal1Id, Rating = 4, Comment = "Strong delivery." },
            new FeedbackAnswerInput { QuestionId = _goal2Id, Rating = 5, Comment = "Excellent mentoring." },
        };

        var submit = await CreateService(ReviewerUser(_reviewerUserId))
            .SubmitFeedbackByAssignmentAsync(_assignmentId, answers);

        submit.IsSuccess.Should().BeTrue(submit.Error);
        submit.Value!.Submitted.Should().BeTrue("the returned form must be locked immediately after submit");

        // Re-load: submitted=true and each answer hydrated back BY GOAL ID.
        var reload = await CreateService(ReviewerUser(_reviewerUserId)).GetFeedbackFormAsync(_assignmentId);
        reload.IsSuccess.Should().BeTrue(reload.Error);
        var form = reload.Value!;
        form.Submitted.Should().BeTrue();

        var q1 = form.Questions.Single(q => q.QuestionId == _goal1Id);
        q1.Rating.Should().Be(4);
        q1.Comment.Should().Be("Strong delivery.");

        var q2 = form.Questions.Single(q => q.QuestionId == _goal2Id);
        q2.Rating.Should().Be(5);
        q2.Comment.Should().Be("Excellent mentoring.");

        // The assignment is now Completed in the DB.
        using var db = CreateDbContext();
        (await db.ReviewerAssignments.AsNoTracking().FirstAsync(a => a.Id == _assignmentId))
            .Status.Should().Be(ReviewerAssignmentStatus.Completed);
    }

    // ── 12 (negative): a non-owner submit is denied — RLS not_assigned ──────
    [Fact]
    public async Task SubmitByAssignment_NonOwner_IsDenied_NotAssigned()
    {
        Seed();

        var answers = new[] { new FeedbackAnswerInput { QuestionId = _goal1Id, Rating = 3, Comment = "x" } };

        var result = await CreateService(ReviewerUser(_otherUserId))
            .SubmitFeedbackByAssignmentAsync(_assignmentId, answers);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("not_assigned");

        // Nothing was written on behalf of the non-owner.
        using var db = CreateDbContext();
        (await db.Feedback360s.AsNoTracking().CountAsync()).Should().Be(0);
        (await db.ReviewerAssignments.AsNoTracking().FirstAsync(a => a.Id == _assignmentId))
            .Status.Should().Be(ReviewerAssignmentStatus.Pending);
    }
}
