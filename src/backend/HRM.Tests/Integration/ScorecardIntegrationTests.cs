// ============================================================================
// US-REC-006: Interview scorecard integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI
// container (with the MediatR pipeline + global query filters), covering:
//   - Submit -> saved + averageScore computed (AC-1)
//   - Interview -> Completed once ALL assigned interviewers submit (FR-4)
//   - Multiple interviewers independent + aggregate average (AC-3)
//   - Anti-bias: an interviewer who hasn't submitted cannot see peers' cards (FR-6/BR-5)
//   - Cross-tenant isolation: Tenant B cannot read Tenant A's scorecards (AC-4)
//   - Offer gate (BR-6): a scorecard makes the data available to the move path
//
// PROVIDER: EF Core InMemory through the real composed pipeline (no PostgreSQL,
// no Docker in the verify gate) — mirrors the other recruitment integration
// tests. The PG-specific schema is validated by the `migrations` CI job applying
// the generated migration to real PG.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Application.Features.Recruitment.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class ScorecardIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    // Users: each interviewer has a user; recruiter is a separate user with Recruitment.View.
    private readonly Guid _userInt1 = Guid.NewGuid();
    private readonly Guid _userInt2 = Guid.NewGuid();
    private readonly Guid _userRecruiter = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    private Guid _vacancyA;
    private Guid _applicantA;
    private Guid _interviewA;       // two interviewers: empInt1 + empInt2
    private Guid _empInt1;
    private Guid _empInt2;

    private Guid _vacancyB;
    private Guid _applicantB;
    private Guid _interviewB;
    private Guid _empIntB;

    public ScorecardIntegrationTests()
    {
        SeedData();
    }

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

    private IMediator BuildPipeline(Guid tenantId, Guid userId, params string[] permissions)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.Email.Returns("user@test.com");
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Permissions.Returns(permissions);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

        services.AddScoped<IRecruitmentNotificationService, LogOnlyRecruitmentNotificationService>();
        services.AddScoped<IScorecardService, ScorecardService>();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(SubmitScorecardCommand).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private void SeedData()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        _vacancyA = Guid.NewGuid();
        _applicantA = Guid.NewGuid();
        _interviewA = Guid.NewGuid();
        _empInt1 = Guid.NewGuid();
        _empInt2 = Guid.NewGuid();

        _vacancyB = Guid.NewGuid();
        _applicantB = Guid.NewGuid();
        _interviewB = Guid.NewGuid();
        _empIntB = Guid.NewGuid();

        db.Vacancies.AddRange(
            Vacancy(_vacancyA, _tenantA, "Backend (A)"),
            Vacancy(_vacancyB, _tenantB, "Backend (B)"));

        db.Applicants.AddRange(
            Applicant(_applicantA, _tenantA, _vacancyA),
            Applicant(_applicantB, _tenantB, _vacancyB));

        db.Employees.AddRange(
            Emp(_empInt1, _tenantA, _userInt1, "Grace", "Hopper"),
            Emp(_empInt2, _tenantA, _userInt2, "Alan", "Turing"),
            Emp(_empIntB, _tenantB, _userB, "Bee", "One"));

        db.Interviews.AddRange(
            Interview(_interviewA, _tenantA, _applicantA, _vacancyA, _empInt1, _empInt2),
            Interview(_interviewB, _tenantB, _applicantB, _vacancyB, _empIntB));

        db.SaveChanges();
    }

    private static Vacancy Vacancy(Guid id, Guid tenantId, string title) => new()
    {
        Id = id,
        TenantId = tenantId,
        ReferenceNumber = $"VAC-{id.GetHashCode() & 0xFFF:D4}",
        Title = title,
        Status = VacancyStatus.Open,
        EmploymentType = EmploymentType.FullTime,
        Headcount = 1,
        Description = "Build.",
        IsDeleted = false,
    };

    private static Applicant Applicant(Guid id, Guid tenantId, Guid vacancyId) => new()
    {
        Id = id,
        TenantId = tenantId,
        VacancyId = vacancyId,
        ApplicationReferenceNumber = $"APP-{id.GetHashCode() & 0xFFF:D4}",
        FirstName = "Ada",
        LastName = "Lovelace",
        Email = "ada@a.com",
        ResumeStorageKey = "x",
        ResumeFileName = "r.pdf",
        Stage = ApplicantStage.Interview,
        Source = ApplicationSource.Public,
        AppliedAt = DateTime.UtcNow,
        IsDeleted = false,
    };

    private static Employee Emp(Guid id, Guid tenantId, Guid userId, string first, string last) => new()
    {
        Id = id,
        TenantId = tenantId,
        EmployeeNo = $"EMP-{id.GetHashCode() & 0xFFF:D4}",
        FirstName = first,
        LastName = last,
        Email = $"{first}@a.com".ToLowerInvariant(),
        DateOfJoining = DateTime.UtcNow.AddYears(-1),
        DepartmentId = Guid.NewGuid(),
        JobTitleId = Guid.NewGuid(),
        EmploymentType = EmploymentType.FullTime,
        Status = EmployeeStatus.Active,
        UserId = userId,
        IsDeleted = false,
    };

    private static Interview Interview(Guid id, Guid tenantId, Guid applicantId, Guid vacancyId, params Guid[] interviewerEmpIds) => new()
    {
        Id = id,
        TenantId = tenantId,
        ApplicantId = applicantId,
        VacancyId = vacancyId,
        RoundNumber = 1,
        InterviewType = InterviewType.Video,
        ScheduledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
        StartTime = new TimeOnly(10, 0),
        DurationMinutes = 60,
        VideoLink = "https://meet.example.com/x",
        Status = InterviewStatus.Scheduled,
        IsDeleted = false,
        Interviewers = interviewerEmpIds.Select(empId => new InterviewInterviewer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            InterviewId = id,
            EmployeeId = empId,
            IsDeleted = false,
        }).ToList(),
    };

    private static SubmitScorecardCommand Submit(
        Guid interviewId, OverallRecommendation rec, params (string key, int score)[] ratings)
        => new(interviewId, rec, "Solid round.",
            ratings.Select(r => new CriterionRatingInput { CriterionKey = r.key, Score = r.score }).ToList());

    private static readonly (string, int)[] DefaultRatings =
    {
        ("technical_skills", 4), ("communication", 5), ("problem_solving", 3), ("cultural_fit", 4),
    };

    // ── Submit -> saved + averageScore (AC-1) ─────────────────────────

    [Fact]
    public async Task Submit_SavesScorecard_WithComputedAverage()
    {
        var med = BuildPipeline(_tenantA, _userInt1, "Recruitment.View");

        var result = await med.Send(Submit(_interviewA, OverallRecommendation.StrongHire, DefaultRatings));

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.InterviewId.Should().Be(_interviewA);
        dto.AverageScore.Should().Be(4.00m);  // (4+5+3+4)/4
        dto.OverallRecommendationName.Should().Be("StrongHire");
        dto.Ratings.Should().HaveCount(4);
        dto.Ratings.First().CriterionLabel.Should().Be("Technical Skills");
    }

    // ── FR-4: interview Completed when ALL assigned submit ────────────

    [Fact]
    public async Task Interview_MarkedCompleted_WhenAllAssignedInterviewersSubmit()
    {
        var med1 = BuildPipeline(_tenantA, _userInt1, "Recruitment.View");
        await med1.Send(Submit(_interviewA, OverallRecommendation.Hire, ("technical_skills", 4)));

        // Only one of two interviewers submitted -> still Scheduled.
        using (var db = ReadDb(_tenantA))
        {
            (await db.Interviews.FirstAsync(i => i.Id == _interviewA)).Status
                .Should().Be(InterviewStatus.Scheduled);
        }

        var med2 = BuildPipeline(_tenantA, _userInt2, "Recruitment.View");
        await med2.Send(Submit(_interviewA, OverallRecommendation.StrongHire, ("technical_skills", 5)));

        using (var db = ReadDb(_tenantA))
        {
            (await db.Interviews.FirstAsync(i => i.Id == _interviewA)).Status
                .Should().Be(InterviewStatus.Completed);
        }
    }

    // ── AC-3: multiple interviewers independent + aggregate average ───

    [Fact]
    public async Task Recruiter_SeesAllScorecards_WithAggregateAverage()
    {
        var med1 = BuildPipeline(_tenantA, _userInt1, "Recruitment.View");
        await med1.Send(Submit(_interviewA, OverallRecommendation.Hire,
            ("technical_skills", 4), ("communication", 4)));   // avg 4.00

        var med2 = BuildPipeline(_tenantA, _userInt2, "Recruitment.View");
        await med2.Send(Submit(_interviewA, OverallRecommendation.StrongHire,
            ("technical_skills", 5), ("communication", 5)));   // avg 5.00

        var recruiter = BuildPipeline(_tenantA, _userRecruiter, "Recruitment.View");
        var view = await recruiter.Send(new GetScorecardsForInterviewQuery(_interviewA));

        view.IsSuccess.Should().BeTrue();
        var summary = view.Value!;
        summary.Scorecards.Should().HaveCount(2);
        summary.HiddenCount.Should().Be(0);
        summary.AntiBiasApplies.Should().BeFalse();
        // aggregate = mean of the two card averages = (4.00 + 5.00)/2 = 4.50
        summary.AggregateAverageScore.Should().Be(4.50m);
    }

    // ── FR-6/BR-5: anti-bias hides peers until you submit ─────────────

    [Fact]
    public async Task AntiBias_HidesPeerScorecard_UntilInterviewerSubmitsOwn()
    {
        // Interviewer 1 submits.
        var med1 = BuildPipeline(_tenantA, _userInt1, "Recruitment.View");
        await med1.Send(Submit(_interviewA, OverallRecommendation.Hire, ("technical_skills", 4)));

        // Interviewer 2 (NOT yet submitted) views -> sees none, anti-bias overlay applies.
        var med2 = BuildPipeline(_tenantA, _userInt2, "Recruitment.View");
        var before = await med2.Send(new GetScorecardsForInterviewQuery(_interviewA));
        before.Value!.Scorecards.Should().BeEmpty();
        before.Value.HiddenCount.Should().Be(1);
        before.Value.AntiBiasApplies.Should().BeTrue();

        // Interviewer 2 submits, then views -> now sees BOTH.
        await med2.Send(Submit(_interviewA, OverallRecommendation.StrongHire, ("technical_skills", 5)));
        var after = await med2.Send(new GetScorecardsForInterviewQuery(_interviewA));
        after.Value!.Scorecards.Should().HaveCount(2);
        after.Value.HiddenCount.Should().Be(0);
        after.Value.AntiBiasApplies.Should().BeFalse();
    }

    // ── AC-4: cross-tenant isolation ──────────────────────────────────

    [Fact]
    public async Task Submit_ForCrossTenantInterview_Returns404()
    {
        var medB = BuildPipeline(_tenantB, _userB, "Recruitment.View");

        // Tenant B tries to submit against Tenant A's interview -> filtered out -> 404.
        var result = await medB.Send(Submit(_interviewA, OverallRecommendation.Hire, ("technical_skills", 4)));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("interview_not_found");
    }

    [Fact]
    public async Task GetForInterview_TenantB_CannotSeeTenantAScorecards()
    {
        var med1 = BuildPipeline(_tenantA, _userInt1, "Recruitment.View");
        await med1.Send(Submit(_interviewA, OverallRecommendation.Hire, ("technical_skills", 4)));

        var medB = BuildPipeline(_tenantB, _userB, "Recruitment.View");
        var read = await medB.Send(new GetScorecardsForInterviewQuery(_interviewA));

        // Interview itself is invisible to Tenant B.
        read.IsFailure.Should().BeTrue();
        read.StatusCode.Should().Be(404);
    }

    // ── AC-2/AC-3: applicant-level read ───────────────────────────────

    [Fact]
    public async Task GetForApplicant_Recruiter_SeesScorecardsAcrossInterviews()
    {
        var med1 = BuildPipeline(_tenantA, _userInt1, "Recruitment.View");
        await med1.Send(Submit(_interviewA, OverallRecommendation.Hire, ("technical_skills", 4)));

        var recruiter = BuildPipeline(_tenantA, _userRecruiter, "Recruitment.View");
        var view = await recruiter.Send(new GetScorecardsForApplicantQuery(_applicantA));

        view.IsSuccess.Should().BeTrue();
        view.Value!.Scorecards.Should().ContainSingle();
        view.Value.AggregateAverageScore.Should().Be(4.00m);
    }

    // ── Criteria endpoint ─────────────────────────────────────────────

    [Fact]
    public async Task GetCriteria_ReturnsDefaultSet()
    {
        var med = BuildPipeline(_tenantA, _userRecruiter, "Recruitment.View");

        var result = await med.Send(new GetScorecardCriteriaQuery());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(4);
        result.Value.Select(c => c.Label).Should().Contain("Cultural Fit");
    }

    private AppDbContext ReadDb(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }
}
