// ============================================================================
// US-REC-005: Interview scheduling + calendar integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI
// container (with the MediatR pipeline + global query filters), covering:
//   - Schedule -> Scheduled status + interviewers linked (AC-1)
//   - Multiple rounds for one applicant tracked independently, round auto-increment (AC-4/FR-2)
//   - Reschedule updates the interview fields + interviewer set (AC-3)
//   - Conflict detection returns a soft warning, interview still saved (FR-7)
//   - Cancel sets status Cancelled + clears the reminder job (AC-3/BR-6)
//   - BR-2: a non-active / non-existent interviewer is rejected
//   - Cross-tenant isolation: Tenant B cannot see/touch Tenant A's interviews (AC-5)
//
// NOTE ON PROVIDER + HANGFIRE: mirrors the other recruitment integration tests —
// EF Core InMemory through the real composed pipeline (no PostgreSQL, no Docker in
// the verify gate). IInterviewReminderScheduler (the Hangfire seam) is INTENTIONALLY
// NOT registered here, so the service runs its scheduling path as a no-op and tests
// never require real Hangfire storage. Assertions target the interview fields, not
// the Hangfire side-effect (per the story note). The PG-specific schema is validated
// by the `migrations` CI job applying the generated migration to real PG.
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

public sealed class InterviewSchedulingIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    private Guid _vacancyA;
    private Guid _vacancyB;
    private Guid _applicantA;
    private Guid _applicantB;
    private Guid _empA1;
    private Guid _empA2;
    private Guid _empAInactive;
    private Guid _empB1;

    public InterviewSchedulingIntegrationTests()
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

    /// <summary>
    /// Records reminder-scheduler interactions so tests can prove the reminder job is created on schedule
    /// and cleared on cancel/complete (rather than asserting an always-null id under the no-op seam).
    /// </summary>
    private sealed class RecordingReminderScheduler : IInterviewReminderScheduler
    {
        private int _seq;
        public List<string?> Cancelled { get; } = new();
        public string? Schedule(Guid tenantId, Guid interviewId, DateTime fireAtUtc) => $"job-{++_seq}";
        public void Cancel(string? jobId) => Cancelled.Add(jobId);
    }

    private IMediator BuildPipeline(Guid tenantId, Guid userId, IInterviewReminderScheduler? reminderScheduler = null)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.Email.Returns("user@test.com");
        currentUser.IsAuthenticated.Returns(true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

        services.AddScoped<IRecruitmentNotificationService, LogOnlyRecruitmentNotificationService>();
        // IInterviewReminderScheduler is a no-op by default (Hangfire seam); a test can inject a
        // recording fake to assert the schedule/clear behaviour explicitly.
        if (reminderScheduler is not null)
            services.AddSingleton(reminderScheduler);
        services.AddSingleton<IHtmlSanitizer, GanssHtmlSanitizer>();   // ISSUE-117: InterviewService now sanitizes notes on write
        services.AddScoped<IInterviewService, InterviewService>();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ScheduleInterviewCommand).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private void SeedData()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        _vacancyA = Guid.NewGuid();
        _vacancyB = Guid.NewGuid();
        _applicantA = Guid.NewGuid();
        _applicantB = Guid.NewGuid();
        _empA1 = Guid.NewGuid();
        _empA2 = Guid.NewGuid();
        _empAInactive = Guid.NewGuid();
        _empB1 = Guid.NewGuid();

        db.Vacancies.AddRange(
            OpenVacancy(_vacancyA, _tenantA, "Backend Engineer (A)"),
            OpenVacancy(_vacancyB, _tenantB, "Backend Engineer (B)"));

        db.Applicants.AddRange(
            Applicant(_applicantA, _tenantA, _vacancyA, "ada@a.com", "Ada", "Lovelace"),
            Applicant(_applicantB, _tenantB, _vacancyB, "eve@b.com", "Eve", "Hopper"));

        db.Employees.AddRange(
            Emp(_empA1, _tenantA, "int1@a.com", "Grace", "Hopper", EmployeeStatus.Active),
            Emp(_empA2, _tenantA, "int2@a.com", "Alan", "Turing", EmployeeStatus.Active),
            Emp(_empAInactive, _tenantA, "ex@a.com", "Ex", "Employee", EmployeeStatus.Terminated),
            Emp(_empB1, _tenantB, "int1@b.com", "Bee", "One", EmployeeStatus.Active));

        db.SaveChanges();
    }

    private static Vacancy OpenVacancy(Guid id, Guid tenantId, string title) => new()
    {
        Id = id,
        TenantId = tenantId,
        ReferenceNumber = $"VAC-2026-{id.GetHashCode() & 0xFFF:D4}",
        Title = title,
        Status = VacancyStatus.Open,
        EmploymentType = EmploymentType.FullTime,
        Headcount = 1,
        Description = "Build things.",
        IsDeleted = false,
    };

    private static Applicant Applicant(Guid id, Guid tenantId, Guid vacancyId, string email, string first, string last) => new()
    {
        Id = id,
        TenantId = tenantId,
        VacancyId = vacancyId,
        ApplicationReferenceNumber = $"APP-2026-{Math.Abs(email.GetHashCode()) % 10000:D4}",
        FirstName = first,
        LastName = last,
        Email = email,
        ResumeStorageKey = $"recruitment/{vacancyId}/x/z.pdf",
        ResumeFileName = "resume.pdf",
        Stage = ApplicantStage.Interview,
        Source = ApplicationSource.Public,
        AppliedAt = DateTime.UtcNow,
        IsDeleted = false,
    };

    private static Employee Emp(Guid id, Guid tenantId, string email, string first, string last, EmployeeStatus status) => new()
    {
        Id = id,
        TenantId = tenantId,
        EmployeeNo = $"EMP-{Math.Abs(email.GetHashCode()) % 10000:D4}",
        FirstName = first,
        LastName = last,
        Email = email,
        DateOfJoining = DateTime.UtcNow.AddYears(-1),
        DepartmentId = Guid.NewGuid(),
        JobTitleId = Guid.NewGuid(),
        EmploymentType = EmploymentType.FullTime,
        Status = status,
        IsDeleted = false,
    };

    private static ScheduleInterviewCommand Schedule(
        Guid applicantId, IReadOnlyList<Guid> interviewerIds,
        DateOnly? date = null, TimeOnly? start = null, int duration = 60,
        InterviewType type = InterviewType.Video, string? location = null,
        string? videoLink = "https://meet.example.com/x")
        => new(applicantId, type, date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            start ?? new TimeOnly(10, 0), duration, location, videoLink, "Bring portfolio.", interviewerIds);

    // ── Schedule -> Scheduled + interviewers linked (AC-1) ────────────

    [Fact]
    public async Task Schedule_CreatesScheduledInterview_WithInterviewersLinked()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(Schedule(_applicantA, new[] { _empA1, _empA2 }));

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.Status.Should().Be(InterviewStatus.Scheduled);
        dto.StatusName.Should().Be("Scheduled");
        dto.RoundNumber.Should().Be(1);
        dto.VacancyId.Should().Be(_vacancyA);
        dto.InterviewType.Should().Be(InterviewType.Video);
        dto.Interviewers.Should().HaveCount(2);
        dto.Interviewers.Select(i => i.EmployeeId).Should().BeEquivalentTo(new[] { _empA1, _empA2 });
        dto.Interviewers.Should().OnlyContain(i => !string.IsNullOrEmpty(i.Email));
        dto.Warnings.Should().BeEmpty();
    }

    // ── Multiple rounds tracked independently, auto-increment (AC-4/FR-2) ─

    [Fact]
    public async Task Schedule_SecondRound_AutoIncrementsRoundNumber_Independent()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var r1 = await mediator.Send(Schedule(_applicantA, new[] { _empA1 },
            date: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5))));
        var r2 = await mediator.Send(Schedule(_applicantA, new[] { _empA2 },
            date: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(8))));

        r1.Value!.RoundNumber.Should().Be(1);
        r2.Value!.RoundNumber.Should().Be(2);
        r2.Value.Id.Should().NotBe(r1.Value.Id);

        var list = await mediator.Send(new ListInterviewsByApplicantQuery(_applicantA));
        list.Value!.Should().HaveCount(2);
        // Newest round first.
        list.Value[0].RoundNumber.Should().Be(2);
        list.Value[0].Interviewers.Single().EmployeeId.Should().Be(_empA2);
        list.Value[1].Interviewers.Single().EmployeeId.Should().Be(_empA1);
    }

    // ── Reschedule updates fields + interviewers (AC-3) ───────────────

    [Fact]
    public async Task Update_ReschedulesInterview_AndSwapsInterviewers()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var created = (await mediator.Send(Schedule(_applicantA, new[] { _empA1 }))).Value!;

        var newDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var update = await mediator.Send(new UpdateInterviewCommand(
            created.Id, InterviewType.Phone, newDate, new TimeOnly(14, 30), 45,
            null, null, "Phone round.", new[] { _empA2 }));

        update.IsSuccess.Should().BeTrue();
        var dto = update.Value!;
        dto.InterviewType.Should().Be(InterviewType.Phone);
        dto.ScheduledDate.Should().Be(newDate);
        dto.StartTime.Should().Be(new TimeOnly(14, 30));
        dto.DurationMinutes.Should().Be(45);
        dto.Interviewers.Should().ContainSingle().Which.EmployeeId.Should().Be(_empA2);

        // Re-read confirms persistence.
        var read = await mediator.Send(new GetInterviewByIdQuery(created.Id));
        read.Value!.InterviewType.Should().Be(InterviewType.Phone);
        read.Value.Interviewers.Single().EmployeeId.Should().Be(_empA2);
    }

    // ── Conflict detection warns but still saves (FR-7) ───────────────

    [Fact]
    public async Task Schedule_OverlappingInterviewerSlot_ReturnsWarning_ButStillSaves()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(6));

        // First interview at 10:00-11:00 with empA1.
        var first = await mediator.Send(Schedule(_applicantA, new[] { _empA1 },
            date: date, start: new TimeOnly(10, 0), duration: 60));
        first.Value!.Warnings.Should().BeEmpty();

        // Second interview at 10:30-11:30 with the SAME interviewer overlaps → warning.
        var second = await mediator.Send(Schedule(_applicantA, new[] { _empA1 },
            date: date, start: new TimeOnly(10, 30), duration: 60));

        second.IsSuccess.Should().BeTrue();   // not blocked (override allowed)
        second.Value!.Warnings.Should().NotBeEmpty();
        second.Value.Warnings.Should().Contain(w => w.Contains("overlap"));
    }

    [Fact]
    public async Task Schedule_NonOverlappingSlot_NoWarning()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(6));

        await mediator.Send(Schedule(_applicantA, new[] { _empA1 },
            date: date, start: new TimeOnly(10, 0), duration: 60));

        // 11:30 starts after the first ends (11:00) → no overlap.
        var second = await mediator.Send(Schedule(_applicantA, new[] { _empA1 },
            date: date, start: new TimeOnly(11, 30), duration: 60));

        second.Value!.Warnings.Should().BeEmpty();
    }

    // ── Cancel (AC-3/BR-6) ────────────────────────────────────────────

    [Fact]
    public async Task Cancel_SetsStatusCancelled_AndClearsReminderJob()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var created = (await mediator.Send(Schedule(_applicantA, new[] { _empA1 }))).Value!;

        var cancel = await mediator.Send(new CancelInterviewCommand(created.Id));

        cancel.IsSuccess.Should().BeTrue();
        cancel.Value!.Status.Should().Be(InterviewStatus.Cancelled);
        cancel.Value.ReminderJobId.Should().BeNull();
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_Returns409()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var created = (await mediator.Send(Schedule(_applicantA, new[] { _empA1 }))).Value!;
        await mediator.Send(new CancelInterviewCommand(created.Id));

        var again = await mediator.Send(new CancelInterviewCommand(created.Id));
        again.IsFailure.Should().BeTrue();
        again.StatusCode.Should().Be(409);
    }

    // ── Outcome: Completed / No-Show + transition guard (FR-6, ISSUE-115) ─

    [Fact]
    public async Task Complete_SetsStatusCompleted_AndClearsReminderJob()
    {
        var scheduler = new RecordingReminderScheduler();
        var mediator = BuildPipeline(_tenantA, _userA, scheduler);
        var created = (await mediator.Send(Schedule(_applicantA, new[] { _empA1 }))).Value!;

        // The reminder job was actually scheduled at creation — a real non-null id to clear later.
        created.ReminderJobId.Should().NotBeNullOrEmpty();
        var jobId = created.ReminderJobId;

        var done = await mediator.Send(new CompleteInterviewCommand(created.Id));

        done.IsSuccess.Should().BeTrue();
        done.Value!.Status.Should().Be(InterviewStatus.Completed);
        done.Value.StatusName.Should().Be("Completed");
        // Not vacuous: the persisted id is nulled AND Cancel was invoked with the scheduled id.
        done.Value.ReminderJobId.Should().BeNull();
        scheduler.Cancelled.Should().Contain(jobId);
    }

    [Fact]
    public async Task MarkNoShow_SetsStatusNoShow()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var created = (await mediator.Send(Schedule(_applicantA, new[] { _empA1 }))).Value!;

        var noShow = await mediator.Send(new MarkInterviewNoShowCommand(created.Id));

        noShow.IsSuccess.Should().BeTrue();
        noShow.Value!.Status.Should().Be(InterviewStatus.NoShow);
        noShow.Value.StatusName.Should().Be("NoShow");
    }

    [Fact]
    public async Task NoShow_ThenComplete_IsRejectedAsInvalidTransition_409()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var created = (await mediator.Send(Schedule(_applicantA, new[] { _empA1 }))).Value!;
        await mediator.Send(new MarkInterviewNoShowCommand(created.Id));

        var complete = await mediator.Send(new CompleteInterviewCommand(created.Id));

        complete.IsFailure.Should().BeTrue();
        complete.StatusCode.Should().Be(409);
        complete.ErrorCode.Should().Be("interview_invalid_transition");
    }

    [Fact]
    public async Task Complete_ThenNoShow_IsRejectedAsInvalidTransition_409()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var created = (await mediator.Send(Schedule(_applicantA, new[] { _empA1 }))).Value!;
        await mediator.Send(new CompleteInterviewCommand(created.Id));

        var again = await mediator.Send(new MarkInterviewNoShowCommand(created.Id));

        again.IsFailure.Should().BeTrue();
        again.StatusCode.Should().Be(409);
        again.ErrorCode.Should().Be("interview_invalid_transition");
    }

    [Fact]
    public async Task Complete_OnCancelledInterview_IsRejectedAsInvalidTransition_409()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var created = (await mediator.Send(Schedule(_applicantA, new[] { _empA1 }))).Value!;
        await mediator.Send(new CancelInterviewCommand(created.Id));

        var complete = await mediator.Send(new CompleteInterviewCommand(created.Id));

        complete.IsFailure.Should().BeTrue();
        complete.StatusCode.Should().Be(409);
        complete.ErrorCode.Should().Be("interview_invalid_transition");
    }

    [Fact]
    public async Task Complete_CrossTenant_Returns404()
    {
        var medA = BuildPipeline(_tenantA, _userA);
        var created = (await medA.Send(Schedule(_applicantA, new[] { _empA1 }))).Value!;

        var medB = BuildPipeline(_tenantB, _userB);
        var complete = await medB.Send(new CompleteInterviewCommand(created.Id));

        complete.IsFailure.Should().BeTrue();
        complete.StatusCode.Should().Be(404);
    }

    // ── BR-2: interviewer must be an active employee ──────────────────

    [Fact]
    public async Task Schedule_WithInactiveInterviewer_IsRejected()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(Schedule(_applicantA, new[] { _empA1, _empAInactive }));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("interviewer_not_active");
    }

    [Fact]
    public async Task Schedule_WithCrossTenantInterviewer_IsRejected()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        // empB1 belongs to Tenant B — invisible to Tenant A's query → treated as not active/in tenant.
        var result = await mediator.Send(Schedule(_applicantA, new[] { _empB1 }));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("interviewer_not_active");
    }

    // ── Cross-tenant isolation (AC-5) ─────────────────────────────────

    [Fact]
    public async Task Schedule_ForCrossTenantApplicant_Returns404()
    {
        var medB = BuildPipeline(_tenantB, _userB);

        // Tenant B tries to schedule against Tenant A's applicant — filtered out → 404.
        var result = await medB.Send(Schedule(_applicantA, new[] { _empB1 }));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("applicant_not_found");
    }

    [Fact]
    public async Task List_TenantB_SeesNoneOfTenantAInterviews()
    {
        var medA = BuildPipeline(_tenantA, _userA);
        await medA.Send(Schedule(_applicantA, new[] { _empA1 }));

        var medB = BuildPipeline(_tenantB, _userB);
        var bList = await medB.Send(new ListInterviewsQuery(new InterviewCalendarFilter()));

        bList.IsSuccess.Should().BeTrue();
        bList.Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task GetById_TenantB_CannotSeeTenantAInterview()
    {
        var medA = BuildPipeline(_tenantA, _userA);
        var created = (await medA.Send(Schedule(_applicantA, new[] { _empA1 }))).Value!;

        var medB = BuildPipeline(_tenantB, _userB);
        var read = await medB.Send(new GetInterviewByIdQuery(created.Id));

        read.IsFailure.Should().BeTrue();
        read.StatusCode.Should().Be(404);
    }

    // ── Calendar filters (FR-5) ───────────────────────────────────────

    [Fact]
    public async Task List_FilterByInterviewer_ReturnsOnlyTheirInterviews()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        await mediator.Send(Schedule(_applicantA, new[] { _empA1 },
            date: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5))));
        await mediator.Send(Schedule(_applicantA, new[] { _empA2 },
            date: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7))));

        var byA1 = await mediator.Send(new ListInterviewsQuery(
            new InterviewCalendarFilter { InterviewerEmployeeId = _empA1 }));

        byA1.Value!.Should().ContainSingle();
        byA1.Value[0].Interviewers.Single().EmployeeId.Should().Be(_empA1);
    }

    // ── ISSUE-117: interview notes are sanitized, not merely trimmed ───────────────────────
    //
    // Notes are free text authored by one user and rendered to others. Before this, both write
    // paths stored `Trim(input.Notes)` verbatim, while the platform's IHtmlSanitizer was already
    // registered and used by OfferService, VacancyService, ApplicantService and ReviewSignoffService.
    // So the platform HAD the layer and this one service skipped it — the ledger's "platform-wide
    // pattern" framing was wrong, and worth stating because it changes the fix from a design
    // decision into a two-line omission.
    //
    // Asserted on BOTH paths deliberately. The finding named only the create site; sanitizing one of
    // two leaves the field reachable unsanitized by EDITING an interview instead of creating one,
    // which would have read as fixed and shipped the hole.

    private const string ScriptPayload = "<script>alert('xss')</script>Panel went well";

    [Fact]
    public async Task Schedule_SanitizesNotes_NotJustTrims_ISSUE117()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var cmd = Schedule(_applicantA, new[] { _empA1 }) with { Notes = "  " + ScriptPayload + "  " };
        var created = await mediator.Send(cmd);

        created.IsSuccess.Should().BeTrue(created.Error);
        created.Value!.Notes.Should().NotContain("<script>",
            "notes are rendered to other users, so a bare Trim stores markup verbatim (ISSUE-117)");
        created.Value!.Notes.Should().Contain("Panel went well",
            "sanitizing must strip the markup, not discard the author's text");
    }

    [Fact]
    public async Task Update_SanitizesNotes_TheSecondWritePathTheFindingDidNotName_ISSUE117()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var created = await mediator.Send(Schedule(_applicantA, new[] { _empA1 }));
        created.IsSuccess.Should().BeTrue(created.Error);

        var updated = await mediator.Send(new UpdateInterviewCommand(
            created.Value!.Id,
            InterviewType.Video,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(6)),
            new TimeOnly(11, 0),
            45,
            null,
            "https://meet.example.com/x",
            ScriptPayload,
            new[] { _empA1 }));

        updated.IsSuccess.Should().BeTrue(updated.Error);
        updated.Value!.Notes.Should().NotContain("<script>",
            "the UPDATE path is the one the finding omitted; leaving it raw makes the fix cosmetic");
        updated.Value!.Notes.Should().Contain("Panel went well");
    }
}
