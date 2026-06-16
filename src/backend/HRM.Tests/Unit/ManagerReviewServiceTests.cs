// ============================================================================
// US-PRF-003: Manager performance review — service unit tests.
//
// Drives ManagerReviewService through the real EF Core InMemory provider (so the
// manager_review + item rows are actually persisted). Covers the core rules:
//   - AC-1: workspace materializes one item per goal WITH the employee self-rating alongside.
//   - AC-2/AC-3: submit rejected when a goal is unrated (incomplete_ratings, lists the goal).
//   - FR-3: submit rejected when a manager comment < 20 chars (comment_too_short).
//   - FR-2: rating outside the cycle scale is rejected (rating_out_of_range).
//   - BR-4: final score = (self*self_w) + (manager*manager_w) across ratios (50:50, 30:70, 0:100).
//   - BR-1/AC-5: edits/submits rejected when the manager-review window is closed.
//   - BR-2: a manager cannot review a non-direct-report (not_direct_report → 403).
//   - BR-3/AC-5: HR (Review.All) can review anyone + reopen a submitted review.
//   - AC-5: a submitted review is locked (already_submitted).
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
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class ManagerReviewServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _managerUser;

    private readonly Guid _managerEmployeeId = Guid.NewGuid();
    private readonly Guid _reportEmployeeId = Guid.NewGuid();
    private readonly Guid _otherEmployeeId = Guid.NewGuid(); // NOT a report of the manager
    private readonly Guid _cycleId = Guid.NewGuid();
    private readonly Guid _goal1 = Guid.NewGuid(); // weight 60
    private readonly Guid _goal2 = Guid.NewGuid(); // weight 40

    private const string ValidComment = "Strong delivery; consistently exceeded the agreed targets.";

    public ManagerReviewServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _managerUser = Substitute.For<ICurrentUser>();
        _managerUser.UserId.Returns(_managerUserId);
        _managerUser.IsAuthenticated.Returns(true);
        _managerUser.Permissions.Returns(new[] { PermissionCatalog.Performance.ReviewTeam });
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private ManagerReviewService CreateService(ICurrentUser? user = null) => new(
        CreateDbContext(),
        _tenantContext,
        user ?? _managerUser,
        Substitute.For<IPerformanceNotificationService>(),
        Substitute.For<ILogger<ManagerReviewService>>());

    private ICurrentUser HrUser()
    {
        var hr = Substitute.For<ICurrentUser>();
        hr.UserId.Returns(Guid.NewGuid());
        hr.IsAuthenticated.Returns(true);
        hr.Permissions.Returns(new[] { PermissionCatalog.Performance.ReviewAll });
        return hr;
    }

    /// <param name="windowOpen">when false the manager-review window is in the past (AC-5 closed).</param>
    /// <param name="selfScore">if not null, seeds a SUBMITTED self-assessment with this weighted self-score.</param>
    /// <param name="selfWeightPercent">tenant self:manager ratio (BR-4).</param>
    private void Seed(bool windowOpen = true, decimal? selfScore = 4.20m, int selfWeightPercent = 30)
    {
        using var db = CreateDbContext();
        var now = DateTime.UtcNow;

        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });

        db.Employees.Add(new Employee
        {
            Id = _managerEmployeeId, TenantId = _tenantId, UserId = _managerUserId,
            EmployeeNo = "EMP-MGR", FirstName = "Grace", LastName = "Hopper",
            Email = "grace@acme.com", Status = EmployeeStatus.Active, IsDeleted = false,
        });
        db.Employees.Add(new Employee
        {
            Id = _reportEmployeeId, TenantId = _tenantId,
            EmployeeNo = "EMP-RPT", FirstName = "Ada", LastName = "Lovelace",
            Email = "ada@acme.com", Status = EmployeeStatus.Active,
            ReportsToEmployeeId = _managerEmployeeId, IsDeleted = false,
        });
        db.Employees.Add(new Employee
        {
            Id = _otherEmployeeId, TenantId = _tenantId,
            EmployeeNo = "EMP-OTH", FirstName = "Alan", LastName = "Turing",
            Email = "alan@acme.com", Status = EmployeeStatus.Active,
            ReportsToEmployeeId = Guid.NewGuid(), IsDeleted = false, // reports to someone else
        });

        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleId, TenantId = _tenantId, Name = "FY2026",
            Status = AppraisalCycleStatus.Active,
            GoalSettingStart = now.AddDays(-90), GoalSettingEnd = now.AddDays(-60),
            SelfAssessmentStart = now.AddDays(-30), SelfAssessmentEnd = now.AddDays(-10),
            ManagerReviewStart = windowOpen ? now.AddDays(-5) : now.AddDays(-20),
            ManagerReviewEnd = windowOpen ? now.AddDays(5) : now.AddDays(-10),
            RatingScaleMax = 5, SelfWeightPercent = selfWeightPercent, IsDeleted = false,
        });

        db.Goals.AddRange(
            Goal(_goal1, "Ship the API", 60),
            Goal(_goal2, "Mentor juniors", 40));

        if (selfScore is not null)
        {
            db.SelfAssessments.Add(new SelfAssessment
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, CycleId = _cycleId, EmployeeId = _reportEmployeeId,
                Status = SelfAssessmentStatus.Submitted, WeightedSelfScore = selfScore, SubmittedAt = now.AddDays(-12),
                IsDeleted = false,
                Items =
                [
                    new SelfAssessmentItem { Id = Guid.NewGuid(), TenantId = _tenantId, GoalId = _goal1, SelfRating = 5, AchievementPercentage = 100, Comment = ValidComment, IsDeleted = false },
                    new SelfAssessmentItem { Id = Guid.NewGuid(), TenantId = _tenantId, GoalId = _goal2, SelfRating = 3, AchievementPercentage = 70, Comment = ValidComment, IsDeleted = false },
                ],
            });
        }

        db.SaveChanges();
    }

    private Goal Goal(Guid id, string title, int weight) => new()
    {
        Id = id, TenantId = _tenantId, CycleId = _cycleId, EmployeeId = _reportEmployeeId,
        Title = title, Category = GoalCategory.Kpi, Weight = weight, TargetValue = "100%",
        MeasurementUnit = "%", DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        Status = GoalStatus.Acknowledged, IsDeleted = false,
    };

    private static ManagerReviewItemInput Rated(Guid goalId, int rating, string comment)
        => new(goalId, rating, comment);

    private SaveManagerReviewInput Input(params ManagerReviewItemInput[] items)
        => new(_cycleId, _reportEmployeeId, null, ReviewFlag.None, items);

    // ── AC-1: workspace shows self-rating alongside empty manager fields ──

    [Fact]
    public async Task GetWorkspace_ReturnsGoalsWithSelfRating_AndEmptyManagerFields()
    {
        Seed();

        var result = await CreateService().GetReviewWorkspaceAsync(_reportEmployeeId, _cycleId);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.Items.Should().HaveCount(2);
        dto.RatingScaleMax.Should().Be(5);
        dto.SelfWeightPercent.Should().Be(30);
        dto.ManagerWeightPercent.Should().Be(70);
        dto.SelfAssessmentSubmitted.Should().BeTrue();
        dto.WeightedSelfScore.Should().Be(4.20m);
        dto.IsReviewWindowOpen.Should().BeTrue();
        dto.Items.Single(i => i.GoalId == _goal1).SelfRating.Should().Be(5);
        dto.Items.Should().OnlyContain(i => i.ManagerRating == null);
    }

    // ── BR-2: manager cannot review a non-direct-report ──────────────────

    [Fact]
    public async Task GetWorkspace_Forbidden_WhenEmployeeIsNotADirectReport()
    {
        Seed();

        var result = await CreateService().GetReviewWorkspaceAsync(_otherEmployeeId, _cycleId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("not_direct_report");
    }

    [Fact]
    public async Task Submit_Forbidden_WhenEmployeeIsNotADirectReport()
    {
        Seed();
        var input = new SaveManagerReviewInput(_cycleId, _otherEmployeeId, null, ReviewFlag.None,
            new[] { Rated(_goal1, 4, ValidComment), Rated(_goal2, 3, ValidComment) });

        var result = await CreateService().SubmitAsync(input);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("not_direct_report");
    }

    // ── AC-3: submit requires ALL goals rated, lists the unrated goal ────

    [Fact]
    public async Task Submit_Rejected_WhenAGoalIsUnrated_ListsTheGoal()
    {
        Seed();
        var input = Input(Rated(_goal1, 4, ValidComment)); // goal2 missing

        var result = await CreateService().SubmitAsync(input);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("incomplete_ratings");
        result.Error.Should().Contain(_goal2.ToString());
    }

    // ── FR-3: manager comment ≥20 chars on submit ───────────────────────

    [Fact]
    public async Task Submit_Rejected_WhenCommentTooShort()
    {
        Seed();
        var input = Input(Rated(_goal1, 4, ValidComment), Rated(_goal2, 3, "too short"));

        var result = await CreateService().SubmitAsync(input);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("comment_too_short");
    }

    // ── FR-2: rating within the configured scale ────────────────────────

    [Fact]
    public async Task Submit_Rejected_WhenRatingExceedsScale()
    {
        Seed();
        var input = Input(Rated(_goal1, 9, ValidComment), Rated(_goal2, 3, ValidComment));

        var result = await CreateService().SubmitAsync(input);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("rating_out_of_range");
    }

    // ── BR-4: final score across ratios (manager 5*0.6 + 3*0.4 = 4.20) ──

    [Theory]
    [InlineData(50, 4.20)]  // 4.20*0.5 + 4.20*0.5 = 4.20
    [InlineData(30, 4.20)]  // self 4.20*0.3 + manager 4.20*0.7 = 4.20
    [InlineData(0, 4.20)]   // 0% self => pure manager 4.20
    public async Task Submit_ComputesFinalScore_AcrossRatios(int selfWeightPercent, decimal expectedFinal)
    {
        // self-score and manager-score are both 4.20 here, so all ratios yield 4.20 — proves the blend math
        // and that weights are applied. The asymmetric case is covered by the pure helper test below.
        Seed(selfScore: 4.20m, selfWeightPercent: selfWeightPercent);
        var input = Input(Rated(_goal1, 5, ValidComment), Rated(_goal2, 3, ValidComment));

        var result = await CreateService().SubmitAsync(input);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.Status.Should().Be(ManagerReviewStatus.Submitted);
        dto.WeightedManagerScore.Should().Be(4.20m);
        dto.FinalScore.Should().Be(expectedFinal);
        dto.IsLocked.Should().BeTrue();
        dto.SubmittedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Submit_FinalScore_BlendsAsymmetricSelfAndManager()
    {
        // self = 2.00, manager = 4.20, ratio 30:70 => 2.00*0.3 + 4.20*0.7 = 0.60 + 2.94 = 3.54
        Seed(selfScore: 2.00m, selfWeightPercent: 30);
        var input = Input(Rated(_goal1, 5, ValidComment), Rated(_goal2, 3, ValidComment));

        var result = await CreateService().SubmitAsync(input);

        result.IsSuccess.Should().BeTrue();
        result.Value!.FinalScore.Should().Be(3.54m);
    }

    // ── BR-1/AC-5: closed-window guard ──────────────────────────────────

    [Fact]
    public async Task Submit_Rejected_WhenWindowClosed()
    {
        Seed(windowOpen: false);
        var input = Input(Rated(_goal1, 5, ValidComment), Rated(_goal2, 3, ValidComment));

        var result = await CreateService().SubmitAsync(input);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("manager_review_closed");
    }

    // ── AC-5: a submitted review is locked ──────────────────────────────

    [Fact]
    public async Task SaveDraft_Rejected_AfterSubmit()
    {
        Seed();
        var submit = Input(Rated(_goal1, 5, ValidComment), Rated(_goal2, 3, ValidComment));
        (await CreateService().SubmitAsync(submit)).IsSuccess.Should().BeTrue();

        var draft = Input(Rated(_goal1, 2, ValidComment));
        var result = await CreateService().SaveDraftAsync(draft);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("already_submitted");
    }

    // ── BR-3/AC-5: HR can review anyone + reopen a submitted review ─────

    [Fact]
    public async Task Hr_CanReview_AnyEmployee_EvenNonReport()
    {
        Seed();
        var hr = HrUser();
        var input = new SaveManagerReviewInput(_cycleId, _otherEmployeeId, null, ReviewFlag.None,
            new[] { Rated(_goal1, 4, ValidComment), Rated(_goal2, 3, ValidComment) });

        // goals were seeded for the report, not _otherEmployeeId — so HR submit fails on no_goals, but the
        // AUTHORIZATION must pass (no 403). Assert it is not a forbidden failure.
        var result = await CreateService(hr).SubmitAsync(input);

        result.StatusCode.Should().NotBe(403);
    }

    [Fact]
    public async Task Hr_CanReopen_SubmittedReview_ManagerCannot()
    {
        Seed();
        var submit = Input(Rated(_goal1, 5, ValidComment), Rated(_goal2, 3, ValidComment));
        (await CreateService().SubmitAsync(submit)).IsSuccess.Should().BeTrue();

        // Manager reopen → forbidden.
        var mgrReopen = await CreateService().ReopenAsync(_reportEmployeeId, _cycleId);
        mgrReopen.IsFailure.Should().BeTrue();
        mgrReopen.ErrorCode.Should().Be("reopen_forbidden");

        // HR reopen → success, status back to Draft, editable again.
        var hrReopen = await CreateService(HrUser()).ReopenAsync(_reportEmployeeId, _cycleId);
        hrReopen.IsSuccess.Should().BeTrue();
        hrReopen.Value!.Status.Should().Be(ManagerReviewStatus.Draft);

        var draft = Input(Rated(_goal1, 2, ValidComment));
        (await CreateService().SaveDraftAsync(draft)).IsSuccess.Should().BeTrue();
    }

    // ── AC-4: team dashboard reflects review status ─────────────────────

    [Fact]
    public async Task TeamReviews_ReflectsStatus_SelfSubmitted_ThenCompleted()
    {
        Seed(); // report has a SUBMITTED self-assessment, no manager review yet
        var before = await CreateService().GetTeamReviewsAsync(_cycleId);
        before.IsSuccess.Should().BeTrue();
        var member = before.Value!.Members.Single(m => m.EmployeeId == _reportEmployeeId);
        member.Status.Should().Be("SelfAssessmentSubmitted");
        member.WeightedSelfScore.Should().Be(4.20m);

        (await CreateService().SubmitAsync(Input(Rated(_goal1, 5, ValidComment), Rated(_goal2, 3, ValidComment))))
            .IsSuccess.Should().BeTrue();

        var after = await CreateService().GetTeamReviewsAsync(_cycleId);
        after.Value!.Members.Single(m => m.EmployeeId == _reportEmployeeId).Status.Should().Be("Completed");
    }

    [Fact]
    public async Task TeamReviews_PendingSelfAssessment_WhenNoSelfSubmitted()
    {
        Seed(selfScore: null); // no submitted self-assessment

        var dashboard = await CreateService().GetTeamReviewsAsync(_cycleId);

        dashboard.Value!.Members.Single(m => m.EmployeeId == _reportEmployeeId)
            .Status.Should().Be("PendingSelfAssessment");
    }

    // ── BR-4 (pure): final-score helper across ratios ───────────────────

    [Theory]
    [InlineData(4.00, 5.00, 50, 50, 4.50)] // 4*0.5 + 5*0.5
    [InlineData(2.00, 4.20, 30, 70, 3.54)] // 2*0.3 + 4.2*0.7
    [InlineData(3.00, 4.50, 0, 100, 4.50)] // pure manager
    [InlineData(3.00, 4.50, 100, 0, 3.00)] // pure self
    public void ComputeFinalScore_BlendsByRatio(
        double self, double manager, int selfW, int mgrW, decimal expected)
    {
        ManagerReviewService.ComputeFinalScore((decimal)self, (decimal)manager, selfW, mgrW)
            .Should().Be(expected);
    }
}
