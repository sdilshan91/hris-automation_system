// ============================================================================
// US-PRF-004: HR appraisal cycle management — service unit tests.
//
// Drives AppraisalCycleService through the real EF Core InMemory provider (so the
// appraisal_cycle + cycle_phase + cycle_participant rows are actually persisted).
// Covers the domain rules the FluentValidation layer cannot check from a single
// command's fields (those phase-set rules are covered by CycleValidatorTests):
//   - AC-1/AC-2: create persists a Draft cycle, its phases (sequenced), resolved participants.
//   - FR-3: AllEmployees scope resolves all active employees; CustomList rejects a foreign id.
//   - FR-7: valid status transitions (Draft→Active→Paused→Active→Completed, Draft→Cancelled);
//           illegal transitions rejected (invalid_status_transition).
//   - BR-5: rating scale / weights lock once the cycle is Active (rating_scale_locked).
//   - BR-6: cancellation requires a reason (cancellation_reason_required) + notifies participants.
//   - BR-2: a cycle with submitted review activity cannot be deleted (cycle_has_reviews); only a
//           Draft with no reviews can be deleted; non-Draft → cancel only (cycle_not_draft).
//   - FR-8: clone copies settings + phases under a new name with shifted dates, as a fresh Draft.
//   - The GoalSetting/SelfAssessment/ManagerReview phase windows drive the existing IsXOpen gates.
//
// PROVIDER: InMemory — same rationale as the other service unit tests here (the verify gate runs
// `dotnet test` with no PostgreSQL / Docker). The scheduler seam is absent (null ctor param).
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

public sealed class AppraisalCycleServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _hrUser;
    private readonly IPerformanceNotificationService _notifications;

    private readonly Guid _empA = Guid.NewGuid();
    private readonly Guid _empB = Guid.NewGuid();
    private readonly Guid _deptHr = Guid.NewGuid();
    private readonly Guid _deptEng = Guid.NewGuid();

    public AppraisalCycleServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _hrUser = Substitute.For<ICurrentUser>();
        _hrUser.UserId.Returns(Guid.NewGuid());
        _hrUser.IsAuthenticated.Returns(true);
        _hrUser.Email.Returns("hr@acme.com");

        _notifications = Substitute.For<IPerformanceNotificationService>();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private AppraisalCycleService CreateService(ICyclePhaseScheduler? scheduler = null) => new(
        CreateDbContext(),
        _tenantContext,
        _hrUser,
        _notifications,
        Substitute.For<ILogger<AppraisalCycleService>>(),
        scheduler);

    /// <summary>Seeds two active employees (one in HR, one in Eng) so scoping can be exercised.</summary>
    private void SeedEmployees()
    {
        using var db = CreateDbContext();
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
        db.Employees.Add(new Employee
        {
            Id = _empA, TenantId = _tenantId, EmployeeNo = "EMP-A", FirstName = "Ada", LastName = "Lovelace",
            Email = "ada@acme.com", Status = EmployeeStatus.Active, DepartmentId = _deptHr, IsDeleted = false,
        });
        db.Employees.Add(new Employee
        {
            Id = _empB, TenantId = _tenantId, EmployeeNo = "EMP-B", FirstName = "Alan", LastName = "Turing",
            Email = "alan@acme.com", Status = EmployeeStatus.Active, DepartmentId = _deptEng, IsDeleted = false,
        });
        db.SaveChanges();
    }

    // Default phase windows: goal-setting → self-assessment → manager-review, sequential + non-overlapping,
    // all within [start, end]. Anchored relative to "now" so the active-window gates can be asserted.
    private static IReadOnlyList<CyclePhaseInput> DefaultPhases(DateTime start) =>
    [
        new(CyclePhaseType.GoalSetting, start, start.AddDays(10)),
        new(CyclePhaseType.SelfAssessment, start.AddDays(11), start.AddDays(20)),
        new(CyclePhaseType.ManagerReview, start.AddDays(21), start.AddDays(30)),
    ];

    private static CreateCycleInput CreateInput(
        DateTime start,
        DateTime end,
        IReadOnlyList<CyclePhaseInput> phases,
        ParticipantScopeInput scope,
        CycleType type = CycleType.Annual,
        string name = "FY2026 Annual",
        int ratingScaleMax = 5,
        int selfWeightPercent = 30)
        => new(name, type, start, end, phases, scope, ratingScaleMax, selfWeightPercent,
            Is360Enabled: false, IsCalibrationEnabled: false, IsAnonymousFeedback: false);

    private static ParticipantScopeInput AllScope() => new(ParticipantScopeType.AllEmployees);

    // ── AC-1/AC-2: create persists cycle, phases, participants ───────────

    [Fact]
    public async Task Create_PersistsDraftCycleWithPhasesAndParticipants()
    {
        SeedEmployees();
        var start = DateTime.UtcNow.Date.AddDays(1);
        var input = CreateInput(start, start.AddDays(40), DefaultPhases(start), AllScope());

        var result = await CreateService().CreateAsync(input);

        result.IsSuccess.Should().BeTrue(result.Error);
        var dto = result.Value!;
        dto.Status.Should().Be(AppraisalCycleStatus.Draft);
        dto.Type.Should().Be(CycleType.Annual);
        dto.ParticipantScope.Should().Be(ParticipantScopeType.AllEmployees);
        dto.ParticipantCount.Should().Be(2);            // both active employees
        dto.Phases.Should().HaveCount(3);
        dto.Phases.Select(p => p.Sequence).Should().Equal(0, 1, 2);
        dto.Phases.First().PhaseType.Should().Be(CyclePhaseType.GoalSetting);

        // Persistence check.
        using var db = CreateDbContext();
        (await db.AppraisalCycles.AsNoTracking().CountAsync()).Should().Be(1);
        (await db.CyclePhases.AsNoTracking().CountAsync()).Should().Be(3);
        (await db.CycleParticipants.AsNoTracking().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Create_SyncsLegacyWindowColumns_FromPhases()
    {
        SeedEmployees();
        var start = DateTime.UtcNow.Date.AddDays(1);
        var phases = DefaultPhases(start);
        var input = CreateInput(start, start.AddDays(40), phases, AllScope());

        (await CreateService().CreateAsync(input)).IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var cycle = await db.AppraisalCycles.AsNoTracking().FirstAsync();
        cycle.GoalSettingStart.Should().Be(phases[0].StartDate);
        cycle.GoalSettingEnd.Should().Be(phases[0].EndDate);
        cycle.SelfAssessmentStart.Should().Be(phases[1].StartDate);
        cycle.ManagerReviewEnd.Should().Be(phases[2].EndDate);
    }

    [Fact]
    public async Task Create_SchedulesHangfireJobs_WhenSchedulerPresent()
    {
        SeedEmployees();
        var scheduler = Substitute.For<ICyclePhaseScheduler>();
        var start = DateTime.UtcNow.Date.AddDays(1);
        var input = CreateInput(start, start.AddDays(40), DefaultPhases(start), AllScope());

        var result = await CreateService(scheduler).CreateAsync(input);

        result.IsSuccess.Should().BeTrue();
        scheduler.Received(1).ScheduleCycleJobs(_tenantId, Arg.Any<Guid>());
    }

    [Fact]
    public async Task Create_Fails_WhenTenantNotResolved()
    {
        var unresolved = Substitute.For<ITenantContext>();
        unresolved.IsResolved.Returns(false);
        var svc = new AppraisalCycleService(
            TestDbContextFactory.Create(unresolved, _dbName), unresolved, _hrUser, _notifications,
            Substitute.For<ILogger<AppraisalCycleService>>());

        var start = DateTime.UtcNow.Date.AddDays(1);
        var input = CreateInput(start, start.AddDays(40), DefaultPhases(start), AllScope());

        var result = await svc.CreateAsync(input);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    // ── FR-3: participant scoping ────────────────────────────────────────

    [Fact]
    public async Task Create_CustomListScope_ResolvesOnlyListedEmployees()
    {
        SeedEmployees();
        var start = DateTime.UtcNow.Date.AddDays(1);
        var scope = new ParticipantScopeInput(ParticipantScopeType.CustomList, EmployeeIds: new[] { _empA });
        var input = CreateInput(start, start.AddDays(40), DefaultPhases(start), scope);

        var result = await CreateService().CreateAsync(input);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.ParticipantCount.Should().Be(1);
    }

    [Fact]
    public async Task Create_CustomListScope_RejectsUnknownEmployee()
    {
        SeedEmployees();
        var start = DateTime.UtcNow.Date.AddDays(1);
        var scope = new ParticipantScopeInput(ParticipantScopeType.CustomList, EmployeeIds: new[] { Guid.NewGuid() });
        var input = CreateInput(start, start.AddDays(40), DefaultPhases(start), scope);

        var result = await CreateService().CreateAsync(input);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_participant");
    }

    [Fact]
    public async Task Create_DepartmentScope_ResolvesOnlyThatDepartment()
    {
        SeedEmployees();
        var start = DateTime.UtcNow.Date.AddDays(1);
        var scope = new ParticipantScopeInput(ParticipantScopeType.Departments, DepartmentIds: new[] { _deptEng });
        var input = CreateInput(start, start.AddDays(40), DefaultPhases(start), scope);

        var result = await CreateService().CreateAsync(input);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.ParticipantCount.Should().Be(1); // only _empB is in Eng

        using var db = CreateDbContext();
        var cycleId = result.Value!.Id;
        var participants = await db.CycleParticipants.AsNoTracking().Where(p => p.CycleId == cycleId).ToListAsync();
        participants.Should().ContainSingle().Which.EmployeeId.Should().Be(_empB);
    }

    // ── FR-7: status state machine ───────────────────────────────────────

    [Fact]
    public async Task Transition_FullLifecycle_DraftActivePausedActiveCompleted()
    {
        SeedEmployees();
        var cycleId = await CreateDraftAsync();
        var svc = CreateService();

        (await svc.TransitionStatusAsync(cycleId, AppraisalCycleStatus.Active, null))
            .Value!.Status.Should().Be(AppraisalCycleStatus.Active);
        (await CreateService().TransitionStatusAsync(cycleId, AppraisalCycleStatus.Paused, null))
            .Value!.Status.Should().Be(AppraisalCycleStatus.Paused);
        (await CreateService().TransitionStatusAsync(cycleId, AppraisalCycleStatus.Active, null))
            .Value!.Status.Should().Be(AppraisalCycleStatus.Active);
        (await CreateService().TransitionStatusAsync(cycleId, AppraisalCycleStatus.Completed, null))
            .Value!.Status.Should().Be(AppraisalCycleStatus.Completed);
    }

    [Fact]
    public async Task Transition_DraftToCancelled_WithReason_Succeeds_AndNotifies()
    {
        SeedEmployees();
        var cycleId = await CreateDraftAsync();

        var result = await CreateService().TransitionStatusAsync(
            cycleId, AppraisalCycleStatus.Cancelled, "Budget cut for the year.");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Status.Should().Be(AppraisalCycleStatus.Cancelled);
        result.Value!.CancellationReason.Should().Be("Budget cut for the year.");

        // BR-6: every participant was notified of the cancellation.
        await _notifications.Received(2).NotifyCycleEventAsync(
            "cycle-cancelled", cycleId, Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(AppraisalCycleStatus.Draft, AppraisalCycleStatus.Paused)]     // can't pause a draft
    [InlineData(AppraisalCycleStatus.Draft, AppraisalCycleStatus.Completed)]  // can't complete a draft
    [InlineData(AppraisalCycleStatus.Active, AppraisalCycleStatus.Draft)]     // can't go back to draft
    public async Task Transition_IllegalEdges_AreRejected(AppraisalCycleStatus from, AppraisalCycleStatus to)
    {
        SeedEmployees();
        var cycleId = await CreateDraftAsync();
        if (from == AppraisalCycleStatus.Active)
            (await CreateService().TransitionStatusAsync(cycleId, AppraisalCycleStatus.Active, null)).IsSuccess.Should().BeTrue();

        var result = await CreateService().TransitionStatusAsync(cycleId, to, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("invalid_status_transition");
    }

    [Fact]
    public async Task Transition_FromTerminal_IsRejected()
    {
        SeedEmployees();
        var cycleId = await CreateDraftAsync();
        (await CreateService().TransitionStatusAsync(cycleId, AppraisalCycleStatus.Cancelled, "done"))
            .IsSuccess.Should().BeTrue();

        var result = await CreateService().TransitionStatusAsync(cycleId, AppraisalCycleStatus.Active, null);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_status_transition");
    }

    // ── BR-6: cancellation requires a reason ─────────────────────────────

    [Fact]
    public async Task Transition_Cancel_WithoutReason_IsRejected()
    {
        SeedEmployees();
        var cycleId = await CreateDraftAsync();

        var result = await CreateService().TransitionStatusAsync(cycleId, AppraisalCycleStatus.Cancelled, "   ");

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("cancellation_reason_required");
    }

    // ── BR-5: rating scale locks once Active ─────────────────────────────

    [Fact]
    public async Task Update_RatingScale_AllowedWhileDraft()
    {
        SeedEmployees();
        var cycleId = await CreateDraftAsync();
        var start = DateTime.UtcNow.Date.AddDays(1);
        var update = new UpdateCycleInput("FY2026 Annual", start, start.AddDays(40), DefaultPhases(start),
            Is360Enabled: false, IsCalibrationEnabled: false, IsAnonymousFeedback: false,
            RatingScaleMax: 7, SelfWeightPercent: 40);

        var result = await CreateService().UpdateAsync(cycleId, update);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.RatingScaleMax.Should().Be(7);
        result.Value!.SelfWeightPercent.Should().Be(40);
    }

    [Fact]
    public async Task Update_RatingScale_LockedOnceActive()
    {
        SeedEmployees();
        var cycleId = await CreateDraftAsync();
        (await CreateService().TransitionStatusAsync(cycleId, AppraisalCycleStatus.Active, null)).IsSuccess.Should().BeTrue();

        var start = DateTime.UtcNow.Date.AddDays(1);
        var update = new UpdateCycleInput("FY2026 Annual", start, start.AddDays(40), DefaultPhases(start),
            Is360Enabled: false, IsCalibrationEnabled: false, IsAnonymousFeedback: false,
            RatingScaleMax: 8, SelfWeightPercent: 50); // attempt to change the locked scale

        var result = await CreateService().UpdateAsync(cycleId, update);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("rating_scale_locked");
    }

    [Fact]
    public async Task Update_PhaseDatesOnly_AllowedWhileActive_WhenScaleUnchanged()
    {
        SeedEmployees();
        var cycleId = await CreateDraftAsync();
        (await CreateService().TransitionStatusAsync(cycleId, AppraisalCycleStatus.Active, null)).IsSuccess.Should().BeTrue();

        var start = DateTime.UtcNow.Date.AddDays(1);
        // extend the manager-review phase; leave scale fields null (no change)
        var extendedPhases = new List<CyclePhaseInput>(DefaultPhases(start))
        {
            [2] = new(CyclePhaseType.ManagerReview, start.AddDays(21), start.AddDays(35)),
        };
        var update = new UpdateCycleInput("FY2026 Annual", start, start.AddDays(40), extendedPhases,
            Is360Enabled: false, IsCalibrationEnabled: false, IsAnonymousFeedback: false,
            RatingScaleMax: null, SelfWeightPercent: null);

        var result = await CreateService().UpdateAsync(cycleId, update);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Phases.Single(p => p.PhaseType == CyclePhaseType.ManagerReview)
            .EndDate.Should().Be(start.AddDays(35));
    }

    // ── BR-2: delete rules ───────────────────────────────────────────────

    [Fact]
    public async Task Delete_DraftWithNoReviews_Succeeds()
    {
        SeedEmployees();
        var cycleId = await CreateDraftAsync();

        var result = await CreateService().DeleteAsync(cycleId);

        result.IsSuccess.Should().BeTrue(result.Error);
        using var db = CreateDbContext();
        (await db.AppraisalCycles.AsNoTracking().CountAsync()).Should().Be(0); // soft-deleted ⇒ filtered out
    }

    [Fact]
    public async Task Delete_NonDraftCycle_IsRejected_CancelOnly()
    {
        SeedEmployees();
        var cycleId = await CreateDraftAsync();
        (await CreateService().TransitionStatusAsync(cycleId, AppraisalCycleStatus.Active, null)).IsSuccess.Should().BeTrue();

        var result = await CreateService().DeleteAsync(cycleId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("cycle_not_draft");
    }

    [Fact]
    public async Task Delete_WithSubmittedReviewActivity_IsRejected()
    {
        SeedEmployees();
        var cycleId = await CreateDraftAsync();

        // Seed a submitted self-assessment for this cycle ⇒ review activity exists.
        using (var db = CreateDbContext())
        {
            db.SelfAssessments.Add(new SelfAssessment
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, CycleId = cycleId, EmployeeId = _empA,
                Status = SelfAssessmentStatus.Submitted, SubmittedAt = DateTime.UtcNow, IsDeleted = false,
            });
            await db.SaveChangesAsync();
        }

        var result = await CreateService().DeleteAsync(cycleId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("cycle_has_reviews");
    }

    // ── FR-8: clone ──────────────────────────────────────────────────────

    [Fact]
    public async Task Clone_CopiesSettingsAndPhases_WithNewNameAndDates_AsDraft()
    {
        SeedEmployees();
        var start = DateTime.UtcNow.Date.AddDays(1);
        var srcInput = CreateInput(start, start.AddDays(40), DefaultPhases(start), AllScope(),
            type: CycleType.Quarterly, name: "Q1 KPI", ratingScaleMax: 4, selfWeightPercent: 25);
        srcInput = srcInput with { Is360Enabled = true, IsCalibrationEnabled = true, IsAnonymousFeedback = true };
        var src = await CreateService().CreateAsync(srcInput);
        src.IsSuccess.Should().BeTrue(src.Error);

        var newStart = start.AddDays(100);
        var clone = await CreateService().CloneAsync(new CloneCycleInput(src.Value!.Id, "Q2 KPI", newStart, newStart.AddDays(40)));

        clone.IsSuccess.Should().BeTrue(clone.Error);
        var dto = clone.Value!;
        dto.Id.Should().NotBe(src.Value!.Id);
        dto.Name.Should().Be("Q2 KPI");
        dto.Status.Should().Be(AppraisalCycleStatus.Draft);
        dto.Type.Should().Be(CycleType.Quarterly);          // type copied
        dto.RatingScaleMax.Should().Be(4);                  // settings copied
        dto.SelfWeightPercent.Should().Be(25);
        dto.Is360Enabled.Should().BeTrue();
        dto.IsCalibrationEnabled.Should().BeTrue();
        dto.IsAnonymousFeedback.Should().BeTrue();
        dto.Phases.Should().HaveCount(3);                   // phases copied
        dto.ParticipantCount.Should().Be(0);               // a template copies settings only, not participants

        // new dates: phases fall within the new window, not the source window.
        dto.Phases.Should().OnlyContain(p => p.StartDate >= newStart && p.EndDate <= newStart.AddDays(40));
        dto.StartDate.Should().Be(newStart);
    }

    [Fact]
    public async Task Clone_UnknownSource_IsRejected()
    {
        SeedEmployees();
        var newStart = DateTime.UtcNow.Date.AddDays(1);
        var clone = await CreateService().CloneAsync(
            new CloneCycleInput(Guid.NewGuid(), "X", newStart, newStart.AddDays(40)));

        clone.IsFailure.Should().BeTrue();
        clone.ErrorCode.Should().Be("cycle_not_found");
    }

    // ── Phase windows drive the IsXOpen gates (US-PRF-001/002/003 convergence) ─

    [Fact]
    public async Task ActiveCycle_GoalSettingWindowOpen_DrivesIsGoalSettingOpen()
    {
        SeedEmployees();
        // goal-setting window straddles "now"; self/manager later. Cycle must be Active.
        var now = DateTime.UtcNow;
        var start = now.AddDays(-2);
        IReadOnlyList<CyclePhaseInput> phases =
        [
            new(CyclePhaseType.GoalSetting, now.AddDays(-1), now.AddDays(5)),
            new(CyclePhaseType.SelfAssessment, now.AddDays(6), now.AddDays(12)),
            new(CyclePhaseType.ManagerReview, now.AddDays(13), now.AddDays(20)),
        ];
        var input = CreateInput(start, now.AddDays(25), phases, AllScope());
        var created = await CreateService().CreateAsync(input);
        created.IsSuccess.Should().BeTrue(created.Error);
        (await CreateService().TransitionStatusAsync(created.Value!.Id, AppraisalCycleStatus.Active, null))
            .IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var cycle = await db.AppraisalCycles.AsNoTracking().Include(c => c.Phases)
            .FirstAsync(c => c.Id == created.Value!.Id);

        cycle.IsGoalSettingOpen(now).Should().BeTrue();
        cycle.IsSelfAssessmentOpen(now).Should().BeFalse();
        cycle.IsManagerReviewOpen(now).Should().BeFalse();
    }

    [Fact]
    public async Task DraftCycle_WindowsAreClosed_EvenWhenInstantInside()
    {
        SeedEmployees();
        var now = DateTime.UtcNow;
        var start = now.AddDays(-2);
        IReadOnlyList<CyclePhaseInput> phases =
        [
            new(CyclePhaseType.GoalSetting, now.AddDays(-1), now.AddDays(5)),
            new(CyclePhaseType.SelfAssessment, now.AddDays(6), now.AddDays(12)),
            new(CyclePhaseType.ManagerReview, now.AddDays(13), now.AddDays(20)),
        ];
        var created = await CreateService().CreateAsync(CreateInput(start, now.AddDays(25), phases, AllScope()));
        created.IsSuccess.Should().BeTrue(created.Error);

        using var db = CreateDbContext();
        var cycle = await db.AppraisalCycles.AsNoTracking().Include(c => c.Phases)
            .FirstAsync(c => c.Id == created.Value!.Id);

        // Draft ⇒ gate is closed even though "now" is inside the goal-setting phase window.
        cycle.Status.Should().Be(AppraisalCycleStatus.Draft);
        cycle.IsGoalSettingOpen(now).Should().BeFalse();
    }

    // ── BR-4 (single-tenant view): conflict on second active same-type cycle ─

    [Fact]
    public async Task Create_Rejected_WhenEmployeeAlreadyInActiveSameTypeCycle()
    {
        SeedEmployees();
        var start = DateTime.UtcNow.Date.AddDays(1);
        // First annual cycle, activated, containing _empA via custom list.
        var firstScope = new ParticipantScopeInput(ParticipantScopeType.CustomList, EmployeeIds: new[] { _empA });
        var first = await CreateService().CreateAsync(
            CreateInput(start, start.AddDays(40), DefaultPhases(start), firstScope, type: CycleType.Annual, name: "Annual 1"));
        first.IsSuccess.Should().BeTrue(first.Error);
        (await CreateService().TransitionStatusAsync(first.Value!.Id, AppraisalCycleStatus.Active, null))
            .IsSuccess.Should().BeTrue();

        // Second annual cycle that also includes _empA ⇒ BR-4 conflict.
        var second = await CreateService().CreateAsync(
            CreateInput(start, start.AddDays(40), DefaultPhases(start), firstScope, type: CycleType.Annual, name: "Annual 2"));

        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("active_same_type_conflict");
    }

    [Fact]
    public async Task Create_Allowed_WhenSecondCycleIsDifferentType()
    {
        SeedEmployees();
        var start = DateTime.UtcNow.Date.AddDays(1);
        var scope = new ParticipantScopeInput(ParticipantScopeType.CustomList, EmployeeIds: new[] { _empA });
        var annual = await CreateService().CreateAsync(
            CreateInput(start, start.AddDays(40), DefaultPhases(start), scope, type: CycleType.Annual, name: "Annual"));
        annual.IsSuccess.Should().BeTrue(annual.Error);
        (await CreateService().TransitionStatusAsync(annual.Value!.Id, AppraisalCycleStatus.Active, null))
            .IsSuccess.Should().BeTrue();

        // A quarterly cycle for the same employee is allowed (different type, BR-4 is per-type).
        var quarterly = await CreateService().CreateAsync(
            CreateInput(start, start.AddDays(40), DefaultPhases(start), scope, type: CycleType.Quarterly, name: "Quarterly"));

        quarterly.IsSuccess.Should().BeTrue(quarterly.Error);
    }

    // ── BUG-260: department-scope selection persistence + full-scope read DTO ────
    // The department selection is persisted ALONGSIDE the resolved participant snapshot (purely so the edit
    // form can prefill it) — but ONLY when the scope is Departments. GetByIdAsync then exposes the full scope
    // (scope type + persisted department ids + resolved participant employee ids) via CycleDto.Scope, while the
    // flat ParticipantScope field is kept for back-compat.

    [Fact]
    public async Task Create_DepartmentsScope_PersistsSelectedDepartmentIds_Distinct()
    {
        SeedEmployees();
        var start = DateTime.UtcNow.Date.AddDays(1);
        // Pass a duplicate department id to prove the persisted list is de-duplicated (.Distinct()).
        var scope = new ParticipantScopeInput(
            ParticipantScopeType.Departments, DepartmentIds: new[] { _deptHr, _deptEng, _deptHr });
        var input = CreateInput(start, start.AddDays(40), DefaultPhases(start), scope);

        var result = await CreateService().CreateAsync(input);

        result.IsSuccess.Should().BeTrue(result.Error);

        using var db = CreateDbContext();
        var cycle = await db.AppraisalCycles.AsNoTracking().FirstAsync();
        cycle.ScopeDepartmentIds.Should().BeEquivalentTo(new[] { _deptHr, _deptEng });
        cycle.ScopeDepartmentIds.Should().HaveCount(2); // duplicate collapsed
    }

    [Theory]
    [InlineData(ParticipantScopeType.AllEmployees)]
    [InlineData(ParticipantScopeType.CustomList)]
    public async Task Create_NonDepartmentsScope_PersistsEmptyDepartmentIds_EvenWhenSupplied(
        ParticipantScopeType scopeType)
    {
        SeedEmployees();
        var start = DateTime.UtcNow.Date.AddDays(1);
        // Deliberately supply DepartmentIds for a NON-Departments scope; they must be ignored (not persisted),
        // proving the column only stores the selection for a Departments-scoped cycle.
        var scope = scopeType == ParticipantScopeType.CustomList
            ? new ParticipantScopeInput(ParticipantScopeType.CustomList,
                DepartmentIds: new[] { _deptHr }, EmployeeIds: new[] { _empA })
            : new ParticipantScopeInput(ParticipantScopeType.AllEmployees,
                DepartmentIds: new[] { _deptHr });
        var input = CreateInput(start, start.AddDays(40), DefaultPhases(start), scope);

        var result = await CreateService().CreateAsync(input);

        result.IsSuccess.Should().BeTrue(result.Error);

        using var db = CreateDbContext();
        var cycle = await db.AppraisalCycles.AsNoTracking().FirstAsync();
        cycle.ParticipantScope.Should().Be(scopeType);
        cycle.ScopeDepartmentIds.Should().BeEmpty(); // only a Departments scope persists the selection
    }

    [Fact]
    public async Task GetById_DepartmentsCycle_ExposesFullScope_DepartmentsEmployeesAndFlat()
    {
        SeedEmployees();
        var start = DateTime.UtcNow.Date.AddDays(1);
        // Both seeded employees live in these two departments, so participants resolve to {_empA, _empB}.
        var scope = new ParticipantScopeInput(
            ParticipantScopeType.Departments, DepartmentIds: new[] { _deptHr, _deptEng });
        var created = await CreateService().CreateAsync(
            CreateInput(start, start.AddDays(40), DefaultPhases(start), scope));
        created.IsSuccess.Should().BeTrue(created.Error);

        var read = await CreateService().GetByIdAsync(created.Value!.Id);

        read.IsSuccess.Should().BeTrue(read.Error);
        var dto = read.Value!;
        // All three nested Scope fields are populated.
        dto.Scope.ScopeType.Should().Be(ParticipantScopeType.Departments);
        dto.Scope.DepartmentIds.Should().BeEquivalentTo(new[] { _deptHr, _deptEng }); // persisted selection
        dto.Scope.EmployeeIds.Should().BeEquivalentTo(new[] { _empA, _empB });        // resolved participants
        // The flat back-compat field is unchanged.
        dto.ParticipantScope.Should().Be(ParticipantScopeType.Departments);
        dto.ParticipantCount.Should().Be(2);
    }

    [Fact]
    public async Task GetById_AllEmployeesCycle_HasEmptyDepartmentIds_ButResolvedEmployeeIds()
    {
        SeedEmployees();
        var start = DateTime.UtcNow.Date.AddDays(1);
        var created = await CreateService().CreateAsync(
            CreateInput(start, start.AddDays(40), DefaultPhases(start), AllScope()));
        created.IsSuccess.Should().BeTrue(created.Error);

        var dto = (await CreateService().GetByIdAsync(created.Value!.Id)).Value!;

        dto.Scope.ScopeType.Should().Be(ParticipantScopeType.AllEmployees);
        dto.Scope.DepartmentIds.Should().BeEmpty();                            // non-Departments ⇒ no selection
        dto.Scope.EmployeeIds.Should().BeEquivalentTo(new[] { _empA, _empB }); // participants still exposed
    }

    [Fact]
    public async Task GetById_PreColumnDepartmentsCycle_WithEmptySelection_ReadsWithoutError()
    {
        // Simulate a cycle created before the scope_department_ids column existed: a Departments-scoped cycle
        // whose ScopeDepartmentIds was never populated (it cannot be backfilled). The read must not error and
        // must expose an empty DepartmentIds.
        SeedEmployees();
        var cycleId = BaseEntity.NewUuidV7();
        using (var db = CreateDbContext())
        {
            db.AppraisalCycles.Add(new AppraisalCycle
            {
                Id = cycleId, TenantId = _tenantId, Name = "Legacy", Type = CycleType.Annual,
                Status = AppraisalCycleStatus.Draft,
                StartDate = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddDays(40),
                ParticipantScope = ParticipantScopeType.Departments,
                ScopeDepartmentIds = [], // pre-column: never populated
                IsDeleted = false,
            });
            await db.SaveChangesAsync();
        }

        var read = await CreateService().GetByIdAsync(cycleId);

        read.IsSuccess.Should().BeTrue(read.Error);
        read.Value!.Scope.ScopeType.Should().Be(ParticipantScopeType.Departments);
        read.Value!.Scope.DepartmentIds.Should().BeEmpty(); // nothing to prefill, no error
        read.Value!.Scope.EmployeeIds.Should().BeEmpty();   // no participants resolved
    }

    // ── helper: create a Draft cycle scoped to all employees and return its id ──
    private async Task<Guid> CreateDraftAsync(CycleType type = CycleType.Annual)
    {
        var start = DateTime.UtcNow.Date.AddDays(1);
        var result = await CreateService().CreateAsync(
            CreateInput(start, start.AddDays(40), DefaultPhases(start), AllScope(), type: type));
        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value!.Id;
    }
}
