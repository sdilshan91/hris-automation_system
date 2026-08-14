// ============================================================================
// US-PRF-008: Performance Improvement Plan (PIP) — service unit tests.
//
// Drives PipService through the real EF Core InMemory provider (so the pip, pip_objective, pip_checkpoint and
// pip_event rows are actually persisted). Covers:
//   - BR-1: HR-only create / set-outcome / confirm-escalation (a manager is refused 403); a manager CAN record
//     a checkpoint but CANNOT close/extend a PIP.
//   - BR-2: a second non-terminal PIP for the same employee is refused (409); a new PIP after a closed one is OK.
//   - BR-3: a <30-day duration is refused (422).
//   - BR-4: the employee acknowledges (immutable Acknowledged event, status flips); a non-employee cannot ack.
//   - AC-3/FR-4/FR-5: checkpoint recording is append-only — each call adds a NEW immutable row + event.
//   - Lifecycle: Draft/Active → checkpoint → Extended → SuccessfullyCompleted; and → NotMet → escalation.
//   - AC-5/BR-6: escalation only on a Not-Met PIP; immutable EscalationConfirmed event recorded.
//   - FR-8 visibility: an unrelated employee gets 403 on Get and an empty list; employee/manager/mentor/HR see it.
//   - Tenant scoping: an unresolved tenant context is refused.
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

public sealed class PipServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;

    private readonly Guid _hrUserId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _employeeUserId = Guid.NewGuid();
    private readonly Guid _mentorUserId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();

    private readonly Guid _managerEmpId = Guid.NewGuid();
    private readonly Guid _employeeEmpId = Guid.NewGuid();
    private readonly Guid _mentorEmpId = Guid.NewGuid();
    private readonly Guid _otherEmpId = Guid.NewGuid();

    public PipServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private PipService Service(ICurrentUser user) => new(
        Db(), _tenantContext, user,
        Substitute.For<IPerformanceNotificationService>(),
        Substitute.For<ILogger<PipService>>());

    private ICurrentUser User(Guid userId, params string[] permissions)
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(userId);
        u.IsAuthenticated.Returns(true);
        u.Email.Returns("user@t.com");
        u.Permissions.Returns(permissions);
        return u;
    }

    private ICurrentUser HrUser() => User(_hrUserId, PermissionCatalog.Performance.ReviewAll);
    private ICurrentUser ManagerUser() => User(_managerUserId, PermissionCatalog.Performance.ReviewTeam);
    private ICurrentUser EmployeeUser() => User(_employeeUserId, PermissionCatalog.Performance.ReadSelf);
    private ICurrentUser MentorUser() => User(_mentorUserId, PermissionCatalog.Performance.ReadSelf);
    private ICurrentUser OtherUser() => User(_otherUserId, PermissionCatalog.Performance.ReadSelf);

    private async Task SeedEmployeesAsync()
    {
        using var db = Db();
        db.Employees.Add(new Employee { Id = _managerEmpId, TenantId = _tenantId, UserId = _managerUserId, EmployeeNo = "MGR", FirstName = "Grace", LastName = "Hopper", Email = "g@t.com", Status = EmployeeStatus.Active });
        db.Employees.Add(new Employee { Id = _employeeEmpId, TenantId = _tenantId, UserId = _employeeUserId, EmployeeNo = "EMP", FirstName = "Ada", LastName = "Lovelace", Email = "a@t.com", Status = EmployeeStatus.Active, ReportsToEmployeeId = _managerEmpId });
        db.Employees.Add(new Employee { Id = _mentorEmpId, TenantId = _tenantId, UserId = _mentorUserId, EmployeeNo = "MEN", FirstName = "Alan", LastName = "Turing", Email = "t@t.com", Status = EmployeeStatus.Active });
        db.Employees.Add(new Employee { Id = _otherEmpId, TenantId = _tenantId, UserId = _otherUserId, EmployeeNo = "OTH", FirstName = "Kurt", LastName = "Godel", Email = "k@t.com", Status = EmployeeStatus.Active });
        await db.SaveChangesAsync();
    }

    /// <param name="subject">
    /// GAP-012 / ISSUE-373: lets a test create a SECOND pip for a different employee, so cross-pip objective
    /// attribution can be exercised with a real foreign objective rather than a made-up Guid.
    /// </param>
    private CreatePipInput CreateInput(int durationDays = 60, Guid? mentor = null, Guid? subject = null)
    {
        var start = new DateOnly(2026, 7, 1);
        return new CreatePipInput(
            subject ?? _employeeEmpId, "Underperformance on delivery", start, start.AddDays(durationDays),
            mentor ?? _mentorEmpId, null, PipEscalationAction.TerminationRecommendation,
            new[] { new PipObjectiveInput("Improve velocity", "desc", "Ship 5 tickets/sprint", start.AddDays(30)) },
            new[] { start.AddDays(15), start.AddDays(30) }, "127.0.0.1");
    }

    // ── BR-1: HR-only create ────────────────────────────────────────────

    [Fact]
    public async Task Create_by_HR_succeeds_and_initiates_active_pip()
    {
        await SeedEmployeesAsync();
        var result = await Service(HrUser()).CreateAsync(CreateInput());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(PipStatus.Active);
        result.Value.AcknowledgementStatus.Should().Be(PipAcknowledgementStatus.Pending);
        result.Value.ManagerEmployeeId.Should().Be(_managerEmpId);
        result.Value.Objectives.Should().HaveCount(1);
        result.Value.Events.Select(e => e.EventType)
            .Should().Contain(new[] { PipEventType.Created, PipEventType.Initiated });
    }

    [Fact]
    public async Task Create_by_manager_is_forbidden()
    {
        await SeedEmployeesAsync();
        var result = await Service(ManagerUser()).CreateAsync(CreateInput());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
    }

    // ── BR-2: one active PIP ────────────────────────────────────────────

    [Fact]
    public async Task Create_second_active_pip_is_rejected()
    {
        await SeedEmployeesAsync();
        (await Service(HrUser()).CreateAsync(CreateInput())).IsSuccess.Should().BeTrue();

        var second = await Service(HrUser()).CreateAsync(CreateInput());
        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("active_pip_exists");
    }

    [Fact]
    public async Task Create_after_closing_previous_pip_is_allowed()
    {
        await SeedEmployeesAsync();
        var first = await Service(HrUser()).CreateAsync(CreateInput());
        await Service(HrUser()).SetOutcomeAsync(new SetPipOutcomeInput(
            first.Value!.Id, PipOutcomeKind.SuccessfullyCompleted, "done", null, null, null));

        var second = await Service(HrUser()).CreateAsync(CreateInput());
        second.IsSuccess.Should().BeTrue();
    }

    // ── BR-3: ≥30 days ──────────────────────────────────────────────────

    [Fact]
    public async Task Create_with_duration_below_30_days_is_rejected()
    {
        await SeedEmployeesAsync();
        var result = await Service(HrUser()).CreateAsync(CreateInput(durationDays: 20));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("duration_too_short");
    }

    // ── BR-4: acknowledgement ───────────────────────────────────────────

    [Fact]
    public async Task Employee_acknowledges_pip_and_records_immutable_event()
    {
        await SeedEmployeesAsync();
        var pip = (await Service(HrUser()).CreateAsync(CreateInput())).Value!;

        var ack = await Service(EmployeeUser()).AcknowledgeAsync(new AcknowledgePipInput(pip.Id, "10.0.0.1"));

        ack.IsSuccess.Should().BeTrue();
        ack.Value!.AcknowledgementStatus.Should().Be(PipAcknowledgementStatus.Acknowledged);
        ack.Value.Events.Should().ContainSingle(e => e.EventType == PipEventType.Acknowledged);
    }

    [Fact]
    public async Task Non_employee_cannot_acknowledge_pip()
    {
        await SeedEmployeesAsync();
        var pip = (await Service(HrUser()).CreateAsync(CreateInput())).Value!;

        var ack = await Service(OtherUser()).AcknowledgeAsync(new AcknowledgePipInput(pip.Id, null));

        ack.IsFailure.Should().BeTrue();
        ack.StatusCode.Should().Be(403);
    }

    // ── AC-3/FR-4/FR-5: append-only checkpoints ─────────────────────────

    [Fact]
    public async Task ACheckpointAttributedToAnObjective_AppearsUnderThatObjective_AndNotUnderTheOthers()
    {
        // GAP-012 / ISSUE-373. The UI has always rendered checkpoints as a per-objective accordion body while
        // the model attached them only to the PIP, so IPipObjective.checkpoints read undefined. The model now
        // carries the relationship.
        await SeedEmployeesAsync();
        var pip = (await Service(HrUser()).CreateAsync(CreateInput())).Value!;
        var first = pip.Objectives[0];

        var result = await Service(ManagerUser()).RecordCheckpointAsync(new RecordCheckpointInput(
            pip.Id, new DateOnly(2026, 7, 15), PipCheckpointStatus.OnTrack, "Objective-specific progress",
            null, null, null, null, null, ObjectiveId: first.Id));

        result.IsSuccess.Should().BeTrue();
        var objectives = result.Value!.Objectives;
        objectives.Single(o => o.Id == first.Id).Checkpoints
            .Should().ContainSingle().Which.EvidenceNotes.Should().Be("Objective-specific progress");
        objectives.Where(o => o.Id != first.Id)
            .Should().OnlyContain(o => o.Checkpoints.Count == 0, "the grouping must not smear across objectives");
    }

    [Fact]
    public async Task APipLevelCheckpoint_StaysOutOfEveryObjective_ButRemainsInTheCompleteList()
    {
        // Null ObjectiveId is not a defect — it is what every checkpoint predating this change is, and what an
        // unattributed one still is. It must not vanish, and it must not be arbitrarily assigned to objective
        // one, which is what a backfill would have done.
        await SeedEmployeesAsync();
        var pip = (await Service(HrUser()).CreateAsync(CreateInput())).Value!;

        var result = await Service(ManagerUser()).RecordCheckpointAsync(new RecordCheckpointInput(
            pip.Id, new DateOnly(2026, 7, 15), PipCheckpointStatus.OnTrack, "Whole-plan progress",
            null, null, null, null, null));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Checkpoints.Should().ContainSingle("the flat list is the complete set");
        result.Value.Checkpoints[0].ObjectiveId.Should().BeNull();
        result.Value.Objectives.Should().OnlyContain(o => o.Checkpoints.Count == 0);
    }

    [Fact]
    public async Task AnObjectiveFromAnotherPip_IsRefused()
    {
        // Same tenant, so the global query filter does NOT stop this — the check has to be explicit. Without
        // it, progress could be attached to another employee's plan and would then render in their accordion.
        await SeedEmployeesAsync();
        var mine = (await Service(HrUser()).CreateAsync(CreateInput())).Value!;
        var other = (await Service(HrUser()).CreateAsync(CreateInput(subject: _managerEmpId))).Value!;

        var result = await Service(ManagerUser()).RecordCheckpointAsync(new RecordCheckpointInput(
            mine.Id, new DateOnly(2026, 7, 15), PipCheckpointStatus.OnTrack, "Wrong plan",
            null, null, null, null, null, ObjectiveId: other.Objectives[0].Id));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("objective_not_in_pip");
    }

    [Fact]
    public async Task Manager_records_checkpoint_appends_immutable_history()
    {
        await SeedEmployeesAsync();
        var pip = (await Service(HrUser()).CreateAsync(CreateInput())).Value!;

        var cp1 = await Service(ManagerUser()).RecordCheckpointAsync(new RecordCheckpointInput(
            pip.Id, new DateOnly(2026, 7, 15), PipCheckpointStatus.AtRisk, "Behind schedule", null, null, null, null, null));
        var cp2 = await Service(ManagerUser()).RecordCheckpointAsync(new RecordCheckpointInput(
            pip.Id, new DateOnly(2026, 7, 30), PipCheckpointStatus.OnTrack, "Recovered", null, null, null, null, null));

        cp1.IsSuccess.Should().BeTrue();
        cp2.IsSuccess.Should().BeTrue();
        cp2.Value!.Checkpoints.Should().HaveCount(2);
        cp2.Value.Events.Count(e => e.EventType == PipEventType.CheckpointRecorded).Should().Be(2);
        // Append-only: the first checkpoint row is unchanged after the second is recorded.
        cp2.Value.Checkpoints.First().EvidenceNotes.Should().Be("Behind schedule");
    }

    [Fact]
    public async Task Unrelated_user_cannot_record_checkpoint()
    {
        await SeedEmployeesAsync();
        var pip = (await Service(HrUser()).CreateAsync(CreateInput())).Value!;

        var result = await Service(EmployeeUser()).RecordCheckpointAsync(new RecordCheckpointInput(
            pip.Id, new DateOnly(2026, 7, 15), PipCheckpointStatus.OnTrack, "x", null, null, null, null, null));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
    }

    // ── BR-1: manager cannot close/extend ───────────────────────────────

    [Fact]
    public async Task Manager_cannot_set_outcome()
    {
        await SeedEmployeesAsync();
        var pip = (await Service(HrUser()).CreateAsync(CreateInput())).Value!;

        var result = await Service(ManagerUser()).SetOutcomeAsync(new SetPipOutcomeInput(
            pip.Id, PipOutcomeKind.SuccessfullyCompleted, null, null, null, null));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
    }

    // ── Lifecycle: extend then complete ─────────────────────────────────

    [Fact]
    public async Task Lifecycle_active_to_extended_to_completed()
    {
        await SeedEmployeesAsync();
        var pip = (await Service(HrUser()).CreateAsync(CreateInput())).Value!;

        var extended = await Service(HrUser()).SetOutcomeAsync(new SetPipOutcomeInput(
            pip.Id, PipOutcomeKind.Extended, "needs more time", new DateOnly(2026, 10, 1),
            new[] { new PipObjectiveInput("New goal", null, "criteria", new DateOnly(2026, 9, 1)) }, null));
        extended.IsSuccess.Should().BeTrue();
        extended.Value!.Status.Should().Be(PipStatus.Extended);
        extended.Value.Objectives.Should().HaveCount(2);
        extended.Value.Objectives.Should().Contain(o => o.AddedAtExtension);

        var completed = await Service(HrUser()).SetOutcomeAsync(new SetPipOutcomeInput(
            pip.Id, PipOutcomeKind.SuccessfullyCompleted, "improved", null, null, null));
        completed.IsSuccess.Should().BeTrue();
        completed.Value!.Status.Should().Be(PipStatus.SuccessfullyCompleted);
    }

    // ── Lifecycle: not-met then escalation ──────────────────────────────

    [Fact]
    public async Task Lifecycle_not_met_triggers_escalation()
    {
        await SeedEmployeesAsync();
        var pip = (await Service(HrUser()).CreateAsync(CreateInput())).Value!;

        var notMet = await Service(HrUser()).SetOutcomeAsync(new SetPipOutcomeInput(
            pip.Id, PipOutcomeKind.NotMet, "objectives not met", null, null, null));
        notMet.IsSuccess.Should().BeTrue();
        notMet.Value!.Status.Should().Be(PipStatus.NotMet);

        var escalated = await Service(HrUser()).ConfirmEscalationAsync(new ConfirmEscalationInput(
            pip.Id, PipEscalationAction.TerminationRecommendation, "recommend termination", "10.0.0.5"));
        escalated.IsSuccess.Should().BeTrue();
        escalated.Value!.EscalationConfirmed.Should().BeTrue();
        escalated.Value.Events.Should().ContainSingle(e => e.EventType == PipEventType.EscalationConfirmed);
    }

    [Fact]
    public async Task Escalation_on_non_not_met_pip_is_rejected()
    {
        await SeedEmployeesAsync();
        var pip = (await Service(HrUser()).CreateAsync(CreateInput())).Value!;

        var result = await Service(HrUser()).ConfirmEscalationAsync(new ConfirmEscalationInput(
            pip.Id, PipEscalationAction.Demotion, "x", null));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
    }

    // ── FR-8 visibility ─────────────────────────────────────────────────

    [Fact]
    public async Task Unrelated_employee_cannot_view_pip()
    {
        await SeedEmployeesAsync();
        var pip = (await Service(HrUser()).CreateAsync(CreateInput())).Value!;

        var result = await Service(OtherUser()).GetAsync(pip.Id);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData("employee")]
    [InlineData("manager")]
    [InlineData("mentor")]
    [InlineData("hr")]
    public async Task Related_parties_can_view_pip(string who)
    {
        await SeedEmployeesAsync();
        var pip = (await Service(HrUser()).CreateAsync(CreateInput())).Value!;

        var user = who switch
        {
            "employee" => EmployeeUser(),
            "manager" => ManagerUser(),
            "mentor" => MentorUser(),
            _ => HrUser(),
        };

        var result = await Service(user).GetAsync(pip.Id);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task List_excludes_pips_the_caller_cannot_see()
    {
        await SeedEmployeesAsync();
        await Service(HrUser()).CreateAsync(CreateInput());

        var otherView = await Service(OtherUser()).ListAsync();
        otherView.IsSuccess.Should().BeTrue();
        otherView.Value!.Should().BeEmpty();

        var hrView = await Service(HrUser()).ListAsync();
        hrView.Value!.Should().ContainSingle();
    }

    [Fact]
    public async Task List_by_read_self_employee_returns_only_their_own_pip_ISSUE133()
    {
        // ISSUE-133: a Performance.Read.Self caller (the PIP's employee) must reach the list and see ONLY their own
        // PIP — the self-visibility filter in ListAsync scopes them to employee/manager/mentor rows.
        await SeedEmployeesAsync();
        var pip = (await Service(HrUser()).CreateAsync(CreateInput())).Value!;

        var employeeView = await Service(EmployeeUser()).ListAsync();
        employeeView.IsSuccess.Should().BeTrue();
        employeeView.Value!.Should().ContainSingle()
            .Which.Should().Match<PipSummaryDto>(p => p.Id == pip.Id && p.EmployeeId == _employeeEmpId);

        // A Read.Self peer who is NOT on the PIP still sees nothing (no leak).
        (await Service(OtherUser()).ListAsync()).Value!.Should().BeEmpty();
    }

    // ── Tenant scoping ──────────────────────────────────────────────────

    [Fact]
    public async Task Unresolved_tenant_is_refused()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(false);
        var svc = new PipService(
            TestDbContextFactory.Create(ctx, _dbName), ctx, HrUser(),
            Substitute.For<IPerformanceNotificationService>(),
            Substitute.For<ILogger<PipService>>());

        var result = await svc.CreateAsync(CreateInput());
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }
}
