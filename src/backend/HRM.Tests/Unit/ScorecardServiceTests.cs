// ============================================================================
// US-REC-006: Interview scorecard — service unit tests.
//
// Drives ScorecardService through the real EF Core InMemory provider (so the
// scorecard + criterion-rating + audit-log rows are actually persisted, like
// ApplicantStageMoveServiceTests). Covers the core business rules:
//   - FR-3: averageScore is the mean of the criterion ratings (rounded)
//   - BR-3: a valid overall recommendation is required (invalid enum rejected)
//   - BR-1: only the assigned interviewer may submit (403 otherwise)
//   - FR-2: a second submit by the same interviewer EDITS the one scorecard
//   - BR-4: editing after the lock period is rejected (409 scorecard_locked)
//   - FR-1/BR-2: an unknown criterion key / out-of-range score is rejected
//   - GetCriteria returns the default set
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class ScorecardServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();          // the assigned interviewer's user
    private readonly Guid _otherUserId = Guid.NewGuid();     // a non-assigned user
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    private readonly Guid _vacancyId = Guid.NewGuid();
    private readonly Guid _applicantId = Guid.NewGuid();
    private readonly Guid _interviewId = Guid.NewGuid();
    private readonly Guid _interviewerEmpId = Guid.NewGuid();   // assigned, linked to _userId

    public ScorecardServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.Permissions.Returns(new[] { "Recruitment.View" });

        SeedInterview();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private ScorecardService CreateService(IConfiguration? config = null)
    {
        return new ScorecardService(
            CreateDbContext(),
            _tenantContext,
            _currentUser,
            Substitute.For<IRecruitmentNotificationService>(),
            config ?? new ConfigurationBuilder().Build(),
            Substitute.For<ILogger<ScorecardService>>());
    }

    private void SeedInterview(int interviewDaysFromNow = 1)
    {
        using var db = CreateDbContext();

        db.Vacancies.Add(new Vacancy
        {
            Id = _vacancyId,
            TenantId = _tenantId,
            ReferenceNumber = "VAC-2026-0001",
            Title = "Backend Engineer",
            Status = VacancyStatus.Open,
            EmploymentType = EmploymentType.FullTime,
            Headcount = 1,
            Description = "Build things.",
            IsDeleted = false,
        });

        db.Applicants.Add(new Applicant
        {
            Id = _applicantId,
            TenantId = _tenantId,
            VacancyId = _vacancyId,
            ApplicationReferenceNumber = "APP-2026-0001",
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada@a.com",
            ResumeStorageKey = "x",
            ResumeFileName = "r.pdf",
            Stage = ApplicantStage.Interview,
            Source = ApplicationSource.Public,
            AppliedAt = DateTime.UtcNow,
            IsDeleted = false,
        });

        // The interviewer employee, linked to the current user.
        db.Employees.Add(new Employee
        {
            Id = _interviewerEmpId,
            TenantId = _tenantId,
            EmployeeNo = "EMP-0001",
            FirstName = "Grace",
            LastName = "Hopper",
            Email = "grace@a.com",
            DateOfJoining = DateTime.UtcNow.AddYears(-1),
            DepartmentId = Guid.NewGuid(),
            JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            UserId = _userId,
            IsDeleted = false,
        });

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(interviewDaysFromNow));
        db.Interviews.Add(new Interview
        {
            Id = _interviewId,
            TenantId = _tenantId,
            ApplicantId = _applicantId,
            VacancyId = _vacancyId,
            RoundNumber = 1,
            InterviewType = InterviewType.Video,
            ScheduledDate = startDate,
            StartTime = new TimeOnly(10, 0),
            DurationMinutes = 60,
            VideoLink = "https://meet.example.com/x",
            Status = InterviewStatus.Scheduled,
            IsDeleted = false,
            Interviewers = new List<InterviewInterviewer>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantId,
                    InterviewId = _interviewId,
                    EmployeeId = _interviewerEmpId,
                    IsDeleted = false,
                },
            },
        });

        db.SaveChanges();
    }

    private static SubmitScorecardInput Input(
        OverallRecommendation rec = OverallRecommendation.Hire,
        params (string key, int score)[] ratings)
    {
        var set = ratings.Length > 0
            ? ratings
            : new (string key, int score)[] { ("technical_skills", 4), ("communication", 5) };

        return new SubmitScorecardInput
        {
            InterviewId = default, // set by caller
            OverallRecommendation = rec,
            Ratings = set.Select(r => new CriterionRatingInput { CriterionKey = r.key, Score = r.score }).ToList(),
        };
    }

    private SubmitScorecardInput InputFor(
        OverallRecommendation rec = OverallRecommendation.Hire,
        params (string key, int score)[] ratings)
        => Input(rec, ratings) with { InterviewId = _interviewId };

    // ── FR-3: average is the mean of the ratings ──────────────────────

    [Fact]
    public async Task Submit_ComputesAverageScore_AsMeanOfRatings()
    {
        var service = CreateService();

        var result = await service.SubmitAsync(InputFor(
            OverallRecommendation.StrongHire,
            ("technical_skills", 4), ("communication", 5), ("problem_solving", 3), ("cultural_fit", 4)));

        result.IsSuccess.Should().BeTrue();
        // (4 + 5 + 3 + 4) / 4 = 4.00
        result.Value!.AverageScore.Should().Be(4.00m);
        result.Value.OverallRecommendation.Should().Be(OverallRecommendation.StrongHire);
        result.Value.OverallRecommendationName.Should().Be("StrongHire");
        result.Value.Ratings.Should().HaveCount(4);
    }

    [Fact]
    public async Task Submit_AverageScore_IsRoundedToTwoDecimals()
    {
        var service = CreateService();

        // (5 + 4 + 4) / 3 = 4.333... -> 4.33
        var result = await service.SubmitAsync(InputFor(
            OverallRecommendation.Hire,
            ("technical_skills", 5), ("communication", 4), ("problem_solving", 4)));

        result.Value!.AverageScore.Should().Be(4.33m);
    }

    // ── BR-3: recommendation required (invalid enum value) ────────────

    [Fact]
    public async Task Submit_InvalidRecommendation_IsRejected()
    {
        var service = CreateService();

        var result = await service.SubmitAsync(InputFor((OverallRecommendation)999,
            ("technical_skills", 4)));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("recommendation_required");
    }

    // ── BR-1: only the assigned interviewer can submit ────────────────

    [Fact]
    public async Task Submit_ByNonAssignedUser_IsForbidden()
    {
        _currentUser.UserId.Returns(_otherUserId);  // not linked to any assigned interviewer
        var service = CreateService();

        var result = await service.SubmitAsync(InputFor());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("not_assigned_interviewer");
    }

    // ── FR-2: a second submit edits the one scorecard ─────────────────

    [Fact]
    public async Task Submit_Twice_EditsExistingScorecard_NotDuplicate()
    {
        var service = CreateService();

        var first = await service.SubmitAsync(InputFor(
            OverallRecommendation.NoHire, ("technical_skills", 2), ("communication", 2)));
        first.IsSuccess.Should().BeTrue();
        first.Value!.AverageScore.Should().Be(2.00m);

        var second = await service.SubmitAsync(InputFor(
            OverallRecommendation.Hire, ("technical_skills", 4), ("communication", 4)));
        second.IsSuccess.Should().BeTrue();
        second.Value!.Id.Should().Be(first.Value.Id);   // same scorecard edited
        second.Value.AverageScore.Should().Be(4.00m);
        second.Value.OverallRecommendation.Should().Be(OverallRecommendation.Hire);

        using var db = CreateDbContext();
        var count = await db.InterviewScorecards.CountAsync(s => s.InterviewId == _interviewId);
        count.Should().Be(1);
    }

    // ── BR-4: edit after the lock period is rejected ──────────────────

    [Fact]
    public async Task Submit_EditAfterLockPeriod_IsRejected()
    {
        // Lock period 0h => locked_at = interview start; the interview is in the past so it is already
        // locked the moment the first scorecard exists.
        var pastConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recruitment:ScorecardLockPeriodHours"] = "0",
            }).Build();

        // Re-seed with an interview in the past so locked_at is already behind us.
        // (the constructor seeded one in the future; use a fresh db scoped to a past interview)
        using (var db = CreateDbContext())
        {
            var interview = await db.Interviews.FirstAsync(i => i.Id == _interviewId);
            interview.ScheduledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
            await db.SaveChangesAsync();
        }

        var service = CreateService(pastConfig);

        // First submit succeeds (no existing card; lock check only applies to edits).
        var first = await service.SubmitAsync(InputFor(OverallRecommendation.Hire, ("technical_skills", 4)));
        first.IsSuccess.Should().BeTrue();

        // Edit is now rejected — locked_at (interview start + 0h) is already in the past.
        var edit = await service.SubmitAsync(InputFor(OverallRecommendation.StrongHire, ("technical_skills", 5)));
        edit.IsFailure.Should().BeTrue();
        edit.StatusCode.Should().Be(409);
        edit.ErrorCode.Should().Be("scorecard_locked");
    }

    // ── FR-1/BR-2: unknown criterion / out-of-range score ─────────────

    [Fact]
    public async Task Submit_UnknownCriterion_IsRejected()
    {
        var service = CreateService();

        var result = await service.SubmitAsync(InputFor(OverallRecommendation.Hire, ("leadership", 4)));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("unknown_criterion");
    }

    [Fact]
    public async Task Submit_ScoreOutOfRange_IsRejected()
    {
        var service = CreateService();

        var result = await service.SubmitAsync(InputFor(OverallRecommendation.Hire, ("technical_skills", 6)));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("score_out_of_range");
    }

    // ── AC-K1: edit history — an edit must not silently rewrite what a scorecard MEANT ──────────
    //
    // Editing replaced the rating set wholesale (RemoveRange + re-insert) on an IAuditExempt entity, so the
    // prior judgement vanished with nothing recording it anywhere. A hiring decision could be reviewed months
    // later against scores that were never the ones it was made on.

    [Fact]
    public async Task Editing_a_scorecard_SNAPSHOTS_the_superseded_version_ACK1()
    {
        var service = CreateService();

        await service.SubmitAsync(InputFor(
            OverallRecommendation.NoHire, ("technical_skills", 2), ("communication", 2)));
        await service.SubmitAsync(InputFor(
            OverallRecommendation.Hire, ("technical_skills", 4), ("communication", 4)));

        using var db = CreateDbContext();
        var revisions = await db.InterviewScorecardRevisions.AsNoTracking().ToListAsync();

        revisions.Should().ContainSingle("one edit supersedes exactly one version");
        var snapshot = revisions[0];
        snapshot.Version.Should().Be(1, "the snapshot captures the version it REPLACED, not the new one");
        snapshot.OverallRecommendation.Should().Be(OverallRecommendation.NoHire,
            "the ORIGINAL judgement must survive — this is the whole point of AC-K1");
        snapshot.AverageScore.Should().Be(2.00m);
        snapshot.RatingsJson.Should().Contain("technical_skills")
            .And.Contain("2", "the superseded SCORES must be recoverable, not just the recommendation");
    }

    [Fact]
    public async Task A_scorecard_that_was_never_edited_has_NO_revisions_ACK1()
    {
        // Guards against snapshotting on first submission, which would make every scorecard look edited.
        var service = CreateService();

        await service.SubmitAsync(InputFor(OverallRecommendation.Hire, ("technical_skills", 4)));

        using var db = CreateDbContext();
        (await db.InterviewScorecardRevisions.CountAsync()).Should().Be(0);
        (await db.InterviewScorecards.AsNoTracking().SingleAsync()).Version.Should().Be(1);
    }

    [Fact]
    public async Task Each_edit_adds_one_revision_and_advances_the_version_ACK1()
    {
        var service = CreateService();

        await service.SubmitAsync(InputFor(OverallRecommendation.NoHire, ("technical_skills", 1)));
        await service.SubmitAsync(InputFor(OverallRecommendation.Hire, ("technical_skills", 3)));
        await service.SubmitAsync(InputFor(OverallRecommendation.StrongHire, ("technical_skills", 5)));

        using var db = CreateDbContext();
        var versions = await db.InterviewScorecardRevisions.AsNoTracking()
            .OrderBy(r => r.Version).Select(r => r.Version).ToListAsync();

        versions.Should().Equal([1, 2], "two edits supersede versions 1 and 2");
        (await db.InterviewScorecards.AsNoTracking().SingleAsync()).Version.Should().Be(3);
    }

    // ── AC-K1: GET by id — the route did not exist, blocking TC-REC-006-05/-08 and ISO-015 ──────

    [Fact]
    public async Task GetById_returns_the_scorecard_with_its_history_oldest_first_ACK1()
    {
        var service = CreateService();

        var created = await service.SubmitAsync(InputFor(
            OverallRecommendation.NoHire, ("technical_skills", 2)));
        await service.SubmitAsync(InputFor(OverallRecommendation.Hire, ("technical_skills", 4)));

        var detail = await service.GetByIdAsync(created.Value!.Id);

        detail.IsSuccess.Should().BeTrue(detail.Error);
        detail.Value!.Scorecard.OverallRecommendation.Should().Be(OverallRecommendation.Hire,
            "the scorecard itself reports the CURRENT state");
        detail.Value.Scorecard.Version.Should().Be(2);

        detail.Value.Revisions.Should().ContainSingle();
        detail.Value.Revisions[0].Version.Should().Be(1);
        detail.Value.Revisions[0].OverallRecommendation.Should().Be(OverallRecommendation.NoHire);
        detail.Value.Revisions[0].Ratings.Should().Contain(r => r.CriterionKey == "technical_skills" && r.Score == 2,
            "the superseded ratings must round-trip out of the JSON snapshot, not just be stored");
    }

    [Fact]
    public async Task GetById_for_an_unknown_scorecard_is_404_ACK1()
    {
        var result = await CreateService().GetByIdAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("scorecard_not_found");
    }

    // ── GetCriteria returns the default set ───────────────────────────

    [Fact]
    public void GetCriteria_ReturnsDefaultSet()
    {
        var service = CreateService();

        var result = service.GetCriteria();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Select(c => c.Key).Should().BeEquivalentTo(
            new[] { "technical_skills", "communication", "problem_solving", "cultural_fit" });
    }
}
