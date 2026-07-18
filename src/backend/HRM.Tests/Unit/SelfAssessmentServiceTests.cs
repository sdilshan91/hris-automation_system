// ============================================================================
// US-PRF-002: Employee self-assessment — service unit tests.
//
// Drives SelfAssessmentService through the real EF Core InMemory provider (so the
// self_assessment + item rows are actually persisted). Covers the core rules:
//   - AC-1: get-my materializes one item per assigned goal (merging saved draft)
//   - AC-3/FR-6: save-as-draft persists partial progress without submitting
//   - AC-2/BR-2: submit rejected when a goal is unrated (incomplete_ratings)
//   - FR-3: submit rejected when a comment is < 20 chars (comment_too_short)
//   - FR-2/BR-4: rating outside the cycle scale is rejected (rating_out_of_range)
//   - FR-4: weighted self-score = Σ(rating*weight)/Σ(weight)
//   - BR-1/AC-4: edits/submits rejected when the window is closed
//   - BR-3: a submitted assessment is locked (already_submitted)
//   - NFR-2: the caller only ever sees/edits their OWN assessment
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Performance.DTOs;
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

public sealed class SelfAssessmentServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();        // the acting employee's user
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _cycleId = Guid.NewGuid();
    private readonly Guid _goal1 = Guid.NewGuid();         // weight 60
    private readonly Guid _goal2 = Guid.NewGuid();         // weight 40

    public SelfAssessmentServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.Permissions.Returns(new[] { "Performance.Read.Self" });
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private SelfAssessmentService CreateService() => new(
        CreateDbContext(),
        _tenantContext,
        _currentUser,
        Substitute.For<IPerformanceNotificationService>(),
        Substitute.For<ILogger<SelfAssessmentService>>());

    /// <param name="windowOpen">when false the self-assessment window is in the past (AC-4 closed).</param>
    /// <param name="windowFuture">when true the self-assessment window has NOT yet opened (ISSUE-107).</param>
    private void Seed(bool windowOpen = true, bool windowFuture = false)
    {
        using var db = CreateDbContext();

        var now = DateTime.UtcNow;
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });

        db.Employees.Add(new Employee
        {
            Id = _employeeId,
            TenantId = _tenantId,
            UserId = _userId,
            EmployeeNo = "EMP-0001",
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada@acme.com",
            Status = EmployeeStatus.Active,
            IsDeleted = false,
        });

        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleId,
            TenantId = _tenantId,
            Name = "FY2026",
            Status = AppraisalCycleStatus.Active,
            GoalSettingStart = now.AddDays(-60),
            GoalSettingEnd = now.AddDays(-30),
            SelfAssessmentStart = windowFuture ? now.AddDays(10) : windowOpen ? now.AddDays(-5) : now.AddDays(-20),
            SelfAssessmentEnd = windowFuture ? now.AddDays(20) : windowOpen ? now.AddDays(5) : now.AddDays(-10),
            RatingScaleMax = 5,
            SelfWeightPercent = 30,
            IsDeleted = false,
        });

        db.Goals.AddRange(
            Goal(_goal1, "Ship the API", 60),
            Goal(_goal2, "Mentor juniors", 40));

        db.SaveChanges();
    }

    private Goal Goal(Guid id, string title, int weight) => new()
    {
        Id = id,
        TenantId = _tenantId,
        CycleId = _cycleId,
        EmployeeId = _employeeId,
        Title = title,
        Category = GoalCategory.Kpi,
        Weight = weight,
        TargetValue = "100%",
        MeasurementUnit = "%",
        DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        Status = GoalStatus.Acknowledged,
        IsDeleted = false,
    };

    private static SelfAssessmentItemInput Rated(Guid goalId, int rating, int pct, string comment)
        => new(goalId, rating, pct, comment);

    private const string ValidComment = "Delivered everything on time and to spec.";

    // ── AC-1: get materializes one item per goal ──────────────────────

    [Fact]
    public async Task GetMyAssessment_ReturnsOneItemPerGoal_WithCycleConfig()
    {
        Seed();

        var result = await CreateService().GetMyAssessmentAsync(_cycleId);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.Items.Should().HaveCount(2);
        dto.RatingScaleMax.Should().Be(5);
        dto.SelfWeightPercent.Should().Be(30);
        dto.IsSelfAssessmentOpen.Should().BeTrue();
        dto.Status.Should().Be(SelfAssessmentStatus.Draft);
        dto.Items.Should().OnlyContain(i => i.SelfRating == null);
    }

    // ── AC-3/FR-6: save-as-draft ──────────────────────────────────────

    [Fact]
    public async Task SaveDraft_PersistsPartialProgress_WithoutSubmitting()
    {
        Seed();
        var input = new SaveSelfAssessmentInput(_cycleId, new[] { Rated(_goal1, 4, 90, "partial") });

        var result = await CreateService().SaveDraftAsync(input);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(SelfAssessmentStatus.Draft);

        // re-read shows the saved rating merged onto goal 1, goal 2 still blank.
        var read = await CreateService().GetMyAssessmentAsync(_cycleId);
        var g1 = read.Value!.Items.Single(i => i.GoalId == _goal1);
        g1.SelfRating.Should().Be(4);
        read.Value!.Items.Single(i => i.GoalId == _goal2).SelfRating.Should().BeNull();
    }

    // ── AC-2/BR-2: submit requires ALL goals rated ────────────────────

    [Fact]
    public async Task Submit_Rejected_WhenAGoalIsUnrated()
    {
        Seed();
        var input = new SaveSelfAssessmentInput(_cycleId, new[] { Rated(_goal1, 4, 90, ValidComment) });

        var result = await CreateService().SubmitAsync(input);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("incomplete_ratings");
    }

    // ── FR-3: comment min length on submit ────────────────────────────

    [Fact]
    public async Task Submit_Rejected_WhenCommentTooShort()
    {
        Seed();
        var input = new SaveSelfAssessmentInput(_cycleId, new[]
        {
            Rated(_goal1, 4, 90, ValidComment),
            Rated(_goal2, 3, 70, "too short"), // < 20 chars
        });

        var result = await CreateService().SubmitAsync(input);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("comment_too_short");
    }

    // ── FR-2/BR-4: rating within the configured scale ─────────────────

    [Fact]
    public async Task Submit_Rejected_WhenRatingExceedsScale()
    {
        Seed();
        var input = new SaveSelfAssessmentInput(_cycleId, new[]
        {
            Rated(_goal1, 9, 90, ValidComment), // scale max is 5
            Rated(_goal2, 3, 70, ValidComment),
        });

        var result = await CreateService().SubmitAsync(input);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("rating_out_of_range");
    }

    // ── AC-2 + FR-4: successful submit computes the weighted score ────

    [Fact]
    public async Task Submit_Succeeds_ComputesWeightedScore_AndLocks()
    {
        Seed();
        // rating 5 (weight 60) + rating 3 (weight 40) => (5*60 + 3*40)/100 = 4.20
        var input = new SaveSelfAssessmentInput(_cycleId, new[]
        {
            Rated(_goal1, 5, 100, ValidComment),
            Rated(_goal2, 3, 70, ValidComment),
        });

        var result = await CreateService().SubmitAsync(input);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.Status.Should().Be(SelfAssessmentStatus.Submitted);
        dto.WeightedSelfScore.Should().Be(4.20m);
        dto.IsLocked.Should().BeTrue();
        dto.SubmittedAt.Should().NotBeNull();
    }

    // ── BR-3: a submitted assessment is locked ────────────────────────

    [Fact]
    public async Task SaveDraft_Rejected_AfterSubmit()
    {
        Seed();
        var submit = new SaveSelfAssessmentInput(_cycleId, new[]
        {
            Rated(_goal1, 5, 100, ValidComment),
            Rated(_goal2, 3, 70, ValidComment),
        });
        (await CreateService().SubmitAsync(submit)).IsSuccess.Should().BeTrue();

        var draft = new SaveSelfAssessmentInput(_cycleId, new[] { Rated(_goal1, 2, 50, ValidComment) });
        var result = await CreateService().SaveDraftAsync(draft);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("already_submitted");
    }

    // ── BR-1/AC-4: closed-window guard ────────────────────────────────

    [Fact]
    public async Task Submit_Rejected_WhenWindowClosed()
    {
        Seed(windowOpen: false);
        var input = new SaveSelfAssessmentInput(_cycleId, new[]
        {
            Rated(_goal1, 5, 100, ValidComment),
            Rated(_goal2, 3, 70, ValidComment),
        });

        var result = await CreateService().SubmitAsync(input);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("self_assessment_closed");
    }

    // ── ISSUE-107: a FUTURE (not-yet-open) self-assessment window must return a "not yet open"
    //    message + code, NOT the "has ended" wording used for a genuinely past window. ──
    [Fact]
    public async Task Submit_FutureWindow_Returns_NotYetOpen_NotEnded()
    {
        Seed(windowFuture: true);
        var input = new SaveSelfAssessmentInput(_cycleId, new[]
        {
            Rated(_goal1, 5, 100, ValidComment),
            Rated(_goal2, 3, 70, ValidComment),
        });

        var result = await CreateService().SubmitAsync(input);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("self_assessment_not_open");
        result.Error.Should().Be("The self-assessment period for this cycle has not opened yet.");
        result.Error.Should().NotContain("ended");
    }

    [Fact]
    public async Task Get_ReflectsClosedWindow()
    {
        Seed(windowOpen: false);

        var dto = (await CreateService().GetMyAssessmentAsync(_cycleId)).Value!;

        dto.IsSelfAssessmentOpen.Should().BeFalse();
    }

    // ── NFR-2: another user (no employee record) cannot read ──────────

    [Fact]
    public async Task GetMyAssessment_Fails_WhenCallerHasNoEmployeeRecord()
    {
        Seed();
        var stranger = Substitute.For<ICurrentUser>();
        stranger.UserId.Returns(Guid.NewGuid()); // not linked to any employee
        stranger.IsAuthenticated.Returns(true);

        var service = new SelfAssessmentService(
            CreateDbContext(), _tenantContext, stranger,
            Substitute.For<IPerformanceNotificationService>(),
            Substitute.For<ILogger<SelfAssessmentService>>());

        var result = await service.GetMyAssessmentAsync(_cycleId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("no_employee_record");
    }

    // ── FR-4 (pure): weighted-score helper ────────────────────────────

    [Fact]
    public void ComputeWeightedScore_WeightsByGoalWeight()
    {
        var goals = new List<Goal> { Goal(_goal1, "a", 60), Goal(_goal2, "b", 40) };
        var items = new List<SelfAssessmentItem>
        {
            new() { GoalId = _goal1, SelfRating = 5 },
            new() { GoalId = _goal2, SelfRating = 3 },
        };

        SelfAssessmentService.ComputeWeightedScore(goals, items).Should().Be(4.20m);
    }
}
