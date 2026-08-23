// ============================================================================
// US-ONB-005: OffboardingService unit tests.
//
// Drives OffboardingService through the real EF Core InMemory provider (so instances, task instances, the
// asset register, and the global query filter actually apply), with NSubstitute seams for IAuthService,
// ISessionRevoker, and IPayrollFnFIntegration. Covers:
//   - AC-1/FR-2/FR-3: initiate creates the default clearance task set with LWD-relative due dates, the
//     session tenant_id, and resolved responsible users.
//   - BR-1: status gate (only Terminated/Suspended employees may be offboarded).
//   - §7: last_working_day must be today or in the future.
//   - AC-3: RecordClearance sets the clearance status, completes on approval, leaves open on pending_issues,
//     and recomputes the department/overall summary.
//   - AC-2/BR-3: ReturnAsset flips the asset to Available/Disposed, unlinks the employee, and completes the
//     task; rejects non-assigned assets and assets belonging to another employee.
//   - AC-5/BR-2: CompleteOffboarding BLOCKS when a mandatory task/clearance is pending and returns the list.
//   - AC-4/FR-5/FR-6/FR-7: on success terminates the employee, deactivates the user, revokes sessions, and
//     triggers F&F (the seam), returning a settlement ref.
//   - BR-6: completion is irreversible.
//   - NFR-2: tenant isolation on reads.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class OffboardingServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _hrUser;

    private readonly Guid _hrUserId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _employeeUserId = Guid.NewGuid();
    private readonly Guid _managerId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();

    private readonly IAuthService _authService = Substitute.For<IAuthService>();
    private readonly ISessionRevoker _sessionRevoker = Substitute.For<ISessionRevoker>();
    private readonly IPayrollFnFIntegration _payrollFnF = Substitute.For<IPayrollFnFIntegration>();
    private readonly Guid _settlementRef = Guid.NewGuid();

    public OffboardingServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _hrUser = Substitute.For<ICurrentUser>();
        _hrUser.UserId.Returns(_hrUserId);
        _hrUser.Email.Returns("hr@acme.com");
        _hrUser.IsAuthenticated.Returns(true);

        _authService.RevokeAllSessionsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _payrollFnF.TriggerFinalSettlementAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(_settlementRef);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private OffboardingService Service() =>
        new(Db(), _tenantContext, _hrUser, _authService, _sessionRevoker, _payrollFnF,
            Substitute.For<ILogger<OffboardingService>>());

    /// <summary>Seeds a manager + a departing employee (Suspended/notice by default, eligible for offboarding).</summary>
    private void SeedEmployees(EmployeeStatus status = EmployeeStatus.Suspended)
    {
        using var db = Db();
        db.Users.Add(new User { Id = _employeeUserId, Email = "dana@acme.com", IsActive = true });
        db.Employees.AddRange(
            new Employee
            {
                Id = _managerId, TenantId = _tenantId, EmployeeNo = "EMP-0001",
                FirstName = "Mona", LastName = "Manager", Email = "mona@acme.com",
                DateOfJoining = DateTime.UtcNow.AddYears(-3).Date, UserId = _managerUserId,
                Status = EmployeeStatus.Active,
            },
            new Employee
            {
                Id = _employeeId, TenantId = _tenantId, EmployeeNo = "EMP-0002",
                FirstName = "Dana", LastName = "Departing", Email = "dana@acme.com",
                DateOfJoining = DateTime.UtcNow.AddYears(-2).Date, UserId = _employeeUserId,
                ReportsToEmployeeId = _managerId, Status = status,
            });
        db.SaveChanges();
    }

    private Guid SeedAsset(string tag, AssetStatus status, Guid? assignedTo = null)
    {
        var id = BaseEntity.NewUuidV7();
        using var db = Db();
        db.Assets.Add(new Asset
        {
            Id = id, TenantId = _tenantId, AssetType = "Laptop", AssetTag = tag,
            Status = status, Condition = AssetCondition.Good, AssignedEmployeeId = assignedTo,
        });
        db.SaveChanges();
        return id;
    }

    private InitiateOffboardingInput InitiateInput(DateOnly? lwd = null, OffboardingReason reason = OffboardingReason.Resignation) =>
        new(_employeeId, lwd ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), null, reason, "Leaving for a new role.");

    // ── AC-1 / FR-2 / FR-3 initiate ─────────────────────────────────────

    [Fact]
    public async Task Initiate_creates_default_clearance_tasks_with_lwd_relative_due_dates_and_tenant_id()
    {
        SeedEmployees();
        var lwd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));

        var result = await Service().InitiateAsync(InitiateInput(lwd));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(OffboardingStatus.InProgress);
        result.Value.TotalTasks.Should().Be(6); // the built-in default clearance set.

        using var db = Db();
        var instance = db.OffboardingInstances.Include(o => o.Tasks).Single();
        instance.TenantId.Should().Be(_tenantId);     // FR-8 stamped from the session.
        instance.LastWorkingDay.Should().Be(lwd);
        instance.InitiatedByUserId.Should().Be(_hrUserId);
        instance.Tasks.Should().HaveCount(6);
        instance.Tasks.Should().OnlyContain(t => t.TenantId == _tenantId);

        // FR-2: due_date = LWD - offset_days. "Return IT assets" has offset 0 -> due == LWD.
        var itReturn = instance.Tasks.Single(t => t.Title == "Return IT assets");
        itReturn.DueDate.Should().Be(lwd);
        itReturn.IsMandatory.Should().BeTrue();

        // "Knowledge transfer and handover" has offset 3 -> due == LWD - 3 days.
        var handover = instance.Tasks.Single(t => t.Title == "Knowledge transfer and handover");
        handover.DueDate.Should().Be(lwd.AddDays(-3));

        // FR-3: responsible-party resolution — Manager task -> the reporting manager's user.
        handover.ResponsibleRole.Should().Be(OnboardingResponsibleRole.Manager);
        handover.ResponsibleUserId.Should().Be(_managerUserId);

        // Employee task -> the departing hire's user.
        var personal = instance.Tasks.Single(t => t.Title == "Personal handover items");
        personal.ResponsibleUserId.Should().Be(_employeeUserId);
    }

    [Fact]
    public async Task Initiate_sets_the_full_fr3_department_set()
    {
        SeedEmployees();

        var result = await Service().InitiateAsync(InitiateInput());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Departments.Select(d => d.ClearanceCategory)
            .Should().BeEquivalentTo(new[]
            {
                ClearanceCategory.IT, ClearanceCategory.Finance, ClearanceCategory.Admin,
                ClearanceCategory.Manager, ClearanceCategory.Employee,
            });
    }

    // ── BR-1 status gate ────────────────────────────────────────────────

    [Theory]
    [InlineData(EmployeeStatus.Active)]
    [InlineData(EmployeeStatus.Probation)]
    [InlineData(EmployeeStatus.Inactive)]
    public async Task Initiate_for_ineligible_status_is_rejected(EmployeeStatus status)
    {
        SeedEmployees(status);

        var result = await Service().InitiateAsync(InitiateInput());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("invalid_employee_status");
    }

    [Theory]
    [InlineData(EmployeeStatus.Terminated)]
    [InlineData(EmployeeStatus.Suspended)]
    public async Task Initiate_for_eligible_status_succeeds(EmployeeStatus status)
    {
        SeedEmployees(status);

        var result = await Service().InitiateAsync(InitiateInput());

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Initiate_with_last_working_day_in_the_past_is_rejected()
    {
        SeedEmployees();

        var result = await Service().InitiateAsync(InitiateInput(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("lwd_in_past");
    }

    [Fact]
    public async Task Initiate_with_last_working_day_today_is_allowed()
    {
        SeedEmployees();

        var result = await Service().InitiateAsync(InitiateInput(DateOnly.FromDateTime(DateTime.UtcNow)));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Initiate_for_unknown_employee_returns_404()
    {
        // No employees seeded.
        var result = await Service().InitiateAsync(InitiateInput());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("employee_not_found");
    }

    [Fact]
    public async Task Initiate_when_one_already_in_progress_is_rejected()
    {
        SeedEmployees();
        await Service().InitiateAsync(InitiateInput());

        var second = await Service().InitiateAsync(InitiateInput());

        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("offboarding_in_progress");
    }

    // ── AC-3 record clearance ───────────────────────────────────────────

    [Fact]
    public async Task RecordClearance_approved_completes_task_and_marks_department_cleared()
    {
        SeedEmployees();
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;
        // Admin department has a single (non-mandatory) task.
        var adminTaskId = TaskId(initiated, ClearanceCategory.Admin);

        var result = await Service().RecordClearanceAsync(
            new RecordClearanceInput(adminTaskId, ClearanceStatus.Approved, "All collected."));

        result.IsSuccess.Should().BeTrue();
        var admin = Department(result.Value!, ClearanceCategory.Admin);
        admin.Status.Should().Be("cleared");
        admin.ApprovedTasks.Should().Be(1);

        using var db = Db();
        var task = db.OffboardingTaskInstances.Single(t => t.Id == adminTaskId);
        task.Status.Should().Be(OnboardingTaskStatus.Completed);
        task.ClearanceStatus.Should().Be(ClearanceStatus.Approved);
        task.CompletedByUserId.Should().Be(_hrUserId);
        task.Remarks.Should().Be("All collected.");
    }

    [Fact]
    public async Task RecordClearance_pending_issues_leaves_task_open_and_flags_department_issues()
    {
        SeedEmployees();
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;
        var financeTaskId = TaskId(initiated, ClearanceCategory.Finance);

        var result = await Service().RecordClearanceAsync(
            new RecordClearanceInput(financeTaskId, ClearanceStatus.PendingIssues, "Outstanding loan."));

        result.IsSuccess.Should().BeTrue();
        var finance = Department(result.Value!, ClearanceCategory.Finance);
        finance.Status.Should().Be("issues");
        finance.PendingIssueTasks.Should().Be(1);

        using var db = Db();
        var task = db.OffboardingTaskInstances.Single(t => t.Id == financeTaskId);
        task.Status.Should().Be(OnboardingTaskStatus.InProgress); // NOT completed.
        task.ClearanceStatus.Should().Be(ClearanceStatus.PendingIssues);
    }

    [Fact]
    public async Task RecordClearance_for_unknown_task_returns_404()
    {
        SeedEmployees();
        await Service().InitiateAsync(InitiateInput());

        var result = await Service().RecordClearanceAsync(
            new RecordClearanceInput(Guid.NewGuid(), ClearanceStatus.Approved, null));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("task_not_found");
    }

    // ── AC-2 / BR-3 return asset ────────────────────────────────────────

    [Fact]
    public async Task ReturnAsset_flips_asset_to_available_and_completes_the_task()
    {
        SeedEmployees();
        var assetId = SeedAsset("LAP-001", AssetStatus.Assigned, assignedTo: _employeeId);
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;
        var itReturnTaskId = TaskId(initiated, ClearanceCategory.IT, "Return IT assets");

        var result = await Service().ReturnAssetAsync(
            new ReturnAssetInput(itReturnTaskId, assetId, AssetCondition.Fair, Disposed: false));

        result.IsSuccess.Should().BeTrue();

        using var db = Db();
        var asset = db.Assets.Single(a => a.Id == assetId);
        asset.Status.Should().Be(AssetStatus.Available);    // returned to the pool.
        asset.AssignedEmployeeId.Should().BeNull();         // unlinked.
        asset.Condition.Should().Be(AssetCondition.Fair);

        var task = db.OffboardingTaskInstances.Single(t => t.Id == itReturnTaskId);
        task.Status.Should().Be(OnboardingTaskStatus.Completed);
        task.ClearanceStatus.Should().Be(ClearanceStatus.Approved);
        task.LinkedAssetId.Should().Be(assetId);
    }

    [Fact]
    public async Task ReturnAsset_disposed_flips_asset_to_disposed()
    {
        SeedEmployees();
        var assetId = SeedAsset("LAP-002", AssetStatus.Assigned, assignedTo: _employeeId);
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;
        var itReturnTaskId = TaskId(initiated, ClearanceCategory.IT, "Return IT assets");

        var result = await Service().ReturnAssetAsync(
            new ReturnAssetInput(itReturnTaskId, assetId, AssetCondition.Poor, Disposed: true));

        result.IsSuccess.Should().BeTrue();
        using var db = Db();
        db.Assets.Single(a => a.Id == assetId).Status.Should().Be(AssetStatus.Disposed);
    }

    [Fact]
    public async Task ReturnAsset_that_is_not_assigned_is_rejected()
    {
        SeedEmployees();
        var assetId = SeedAsset("LAP-003", AssetStatus.Available);
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;
        var itReturnTaskId = TaskId(initiated, ClearanceCategory.IT, "Return IT assets");

        var result = await Service().ReturnAssetAsync(
            new ReturnAssetInput(itReturnTaskId, assetId, AssetCondition.Good, Disposed: false));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("asset_not_assigned");
    }

    [Fact]
    public async Task ReturnAsset_assigned_to_a_different_employee_is_rejected()
    {
        SeedEmployees();
        var assetId = SeedAsset("LAP-004", AssetStatus.Assigned, assignedTo: _managerId);
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;
        var itReturnTaskId = TaskId(initiated, ClearanceCategory.IT, "Return IT assets");

        var result = await Service().ReturnAssetAsync(
            new ReturnAssetInput(itReturnTaskId, assetId, AssetCondition.Good, Disposed: false));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("asset_employee_mismatch");
    }

    // ── AC-5 / BR-2 completion gate ─────────────────────────────────────

    [Fact]
    public async Task Complete_blocks_when_mandatory_tasks_are_pending_and_returns_the_list()
    {
        SeedEmployees();
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;

        var result = await Service().CompleteAsync(initiated.Id);

        result.IsSuccess.Should().BeTrue();      // returns a structured "blocked" result, not a failure.
        result.Value!.Completed.Should().BeFalse();
        // The default set has two mandatory tasks: "Return IT assets" and "Settle advances and loans".
        result.Value.PendingItems.Should().HaveCount(2);
        result.Value.PendingItems.Select(p => p.ClearanceCategory)
            .Should().BeEquivalentTo(new[] { ClearanceCategory.IT, ClearanceCategory.Finance });
        result.Value.PendingItems.Should().OnlyContain(p => p.Reason == "not_completed");

        // The employee is NOT terminated, the account NOT deactivated, F&F NOT triggered.
        using var db = Db();
        db.OffboardingInstances.Single(o => o.Id == initiated.Id).Status.Should().Be(OffboardingStatus.InProgress);
        db.Employees.Single(e => e.Id == _employeeId).Status.Should().Be(EmployeeStatus.Suspended);
        await _payrollFnF.DidNotReceive().TriggerFinalSettlementAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Complete_blocks_when_a_mandatory_clearance_has_pending_issues()
    {
        SeedEmployees();
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;
        var financeTaskId = TaskId(initiated, ClearanceCategory.Finance); // mandatory.

        // Flag the mandatory finance clearance with issues (task stays InProgress).
        await Service().RecordClearanceAsync(
            new RecordClearanceInput(financeTaskId, ClearanceStatus.PendingIssues, "Loan unpaid."));
        // Clear the other mandatory task (IT asset return) by approving it directly.
        var itReturnTaskId = TaskId(initiated, ClearanceCategory.IT, "Return IT assets");
        await Service().RecordClearanceAsync(
            new RecordClearanceInput(itReturnTaskId, ClearanceStatus.Approved, null));

        var result = await Service().CompleteAsync(initiated.Id);

        result.Value!.Completed.Should().BeFalse();
        var pending = result.Value.PendingItems.Should().ContainSingle().Subject;
        pending.ClearanceCategory.Should().Be(ClearanceCategory.Finance);
        pending.Reason.Should().Be("clearance_not_approved");
    }

    // ── AC-4 / FR-5 / FR-6 / FR-7 successful completion ─────────────────

    [Fact]
    public async Task Complete_succeeds_when_all_mandatory_cleared_terminates_deactivates_revokes_and_triggers_fnf()
    {
        SeedEmployees();
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;
        await ClearAllMandatory(initiated);

        var result = await Service().CompleteAsync(initiated.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Completed.Should().BeTrue();
        result.Value.Instance!.Status.Should().Be(OffboardingStatus.Completed);
        result.Value.FinalSettlementRef.Should().Be(_settlementRef); // FR-6 settlement ref returned.

        using var db = Db();
        // AC-4: employee terminated + deactivated.
        var employee = db.Employees.Single(e => e.Id == _employeeId);
        employee.Status.Should().Be(EmployeeStatus.Terminated);
        employee.IsActive.Should().BeFalse();
        // FR-5: linked user account deactivated.
        db.Users.IgnoreQueryFilters().Single(u => u.Id == _employeeUserId).IsActive.Should().BeFalse();
        // BR-6: instance marked completed with audit.
        var instance = db.OffboardingInstances.Single(o => o.Id == initiated.Id);
        instance.Status.Should().Be(OffboardingStatus.Completed);
        instance.CompletedAt.Should().NotBeNull();
        instance.CompletedByUserId.Should().Be(_hrUserId);

        // FR-7: sessions revoked (refresh tokens + denylist seam). FR-6: F&F triggered.
        await _authService.Received(1).RevokeAllSessionsAsync(_employeeUserId, _tenantId, Arg.Any<CancellationToken>());
        await _sessionRevoker.Received(1).RevokeActiveSessionsAsync(_employeeUserId, _tenantId, Arg.Any<CancellationToken>());
        await _payrollFnF.Received(1).TriggerFinalSettlementAsync(
            _tenantId, _employeeId, initiated.Id, initiated.LastWorkingDay, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Complete_an_already_completed_offboarding_is_rejected_br6()
    {
        SeedEmployees();
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;
        await ClearAllMandatory(initiated);
        (await Service().CompleteAsync(initiated.Id)).Value!.Completed.Should().BeTrue();

        var second = await Service().CompleteAsync(initiated.Id);

        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("offboarding_completed");
    }

    [Fact]
    public async Task Complete_for_unknown_instance_returns_404()
    {
        SeedEmployees();

        var result = await Service().CompleteAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("offboarding_not_found");
    }

    // ── B4/AC-5: the projected gate and the enforced gate are ONE rule ──────────────────────────────
    //
    // The Angular dashboard used to predict what would block completion so it could disable the button and
    // explain why. Its prediction and this service's enforcement were two hand-written descriptions of the
    // same rule, and they disagreed: the client compared a task's clearance against "cleared", a token only
    // departments ever carry, so it matched nothing and reported EVERY mandatory task as blocking. The
    // button was permanently disabled and no test anywhere noticed, because each side was self-consistent.
    //
    // The rule now lives in OffboardingCompletionGate and is projected onto the instance DTO. These arms
    // exist to keep the projection and the enforcement from drifting apart again — which is the only way
    // the client can safely stop deriving it.

    [Fact]
    public async Task Projected_pending_items_are_exactly_what_completion_refuses_on()
    {
        SeedEmployees();
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;

        var projected = (await Service().GetByIdAsync(initiated.Id)).Value!;
        var attempt = (await Service().CompleteAsync(initiated.Id)).Value!;

        attempt.Completed.Should().BeFalse("nothing has been cleared yet");
        projected.CanComplete.Should().BeFalse();
        projected.PendingMandatoryItems.Should().NotBeEmpty(
            "a projection that reported nothing blocking would enable a button that then 409s");
        projected.PendingMandatoryItems.Select(i => new { i.TaskId, i.Reason })
            .Should().BeEquivalentTo(
                attempt.PendingItems.Select(i => new { i.TaskId, i.Reason }),
                options => options.WithStrictOrdering(),
                "the dashboard renders the projection and the endpoint enforces the attempt; if these two "
                + "lists can differ, the client is back to guessing");
    }

    [Fact]
    public async Task Projection_flips_to_can_complete_at_the_same_moment_completion_starts_succeeding()
    {
        SeedEmployees();
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;
        await ClearAllMandatory(initiated);

        var projected = (await Service().GetByIdAsync(initiated.Id)).Value!;
        projected.CanComplete.Should().BeTrue();
        projected.PendingMandatoryItems.Should().BeEmpty();

        // ...and the enforcement agrees, on the very same state.
        (await Service().CompleteAsync(initiated.Id)).Value!.Completed.Should().BeTrue();
    }

    /// <summary>
    /// BR-6: once completed nothing more can happen, so the button must be off even though no item blocks.
    /// This is the half of <c>CanComplete</c> that the pending-items list alone cannot express — a
    /// projection defined as "no blocking items" would re-enable the button on a finished offboarding.
    /// </summary>
    [Fact]
    public async Task A_completed_offboarding_can_no_longer_be_completed_even_with_nothing_blocking()
    {
        SeedEmployees();
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;
        await ClearAllMandatory(initiated);
        (await Service().CompleteAsync(initiated.Id)).Value!.Completed.Should().BeTrue();

        var projected = (await Service().GetByIdAsync(initiated.Id)).Value!;

        projected.PendingMandatoryItems.Should().BeEmpty();
        projected.CanComplete.Should().BeFalse("BR-6 makes completion irreversible");
    }

    /// <summary>
    /// AC-5 blocks on an explicit refusal, not on the absence of a decision. Not every mandatory task is a
    /// clearance; requiring a verdict on tasks that never receive one would deadlock every offboarding.
    /// </summary>
    [Fact]
    public async Task A_refused_clearance_blocks_and_names_its_reason()
    {
        SeedEmployees();
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;
        await ClearAllMandatory(initiated);

        // Flip one approved MANDATORY clearance back to "issues" — the department found something
        // outstanding. It must be a mandatory one: the gate ignores optional tasks by design, so picking
        // whichever task happened to sort first would prove nothing (it is what the first draft of this
        // arm did, and it passed against a refusal the gate was right to ignore).
        var itTaskId = initiated.Departments
            .Single(d => d.ClearanceCategory == ClearanceCategory.IT)
            .Tasks.First(t => t.IsMandatory).Id;
        var flip = await Service().RecordClearanceAsync(
            new RecordClearanceInput(itTaskId, ClearanceStatus.PendingIssues, "Laptop not returned"));
        flip.IsSuccess.Should().BeTrue(
            "the arm asserts on the state this call creates; a silently-failed flip would leave it "
            + $"asserting against an all-approved instance. Error: {flip.Error}");

        var projected = (await Service().GetByIdAsync(initiated.Id)).Value!;

        projected.CanComplete.Should().BeFalse();
        projected.PendingMandatoryItems.Should().ContainSingle(i => i.TaskId == itTaskId)
            .Which.Reason.Should().Be("clearance_not_approved",
                "the dashboard distinguishes 'nobody has looked at this' from 'a department refused it'");
    }

    /// <summary>
    /// A task can be <see cref="OnboardingTaskStatus.Completed"/> while its clearance is still REFUSED, and
    /// that must still block. This is not a hypothetical: <c>ExitInterviewService</c> completes the exit
    /// interview task on submission (<c>ExitInterviewService.cs:345</c>) and never touches
    /// <c>ClearanceStatus</c> — so a department that flagged issues, followed by the employee submitting
    /// their interview, produces exactly this state.
    ///
    /// <para>
    /// This arm exists because mutation testing found the gap: deleting the
    /// <c>|| ClearanceStatus == PendingIssues</c> clause left the whole suite GREEN. Every other arm reaches
    /// a refused clearance through <c>RecordClearanceAsync</c>, which also sets the task back to
    /// <c>InProgress</c> — so the first clause was doing all the work and the second was untested. Without
    /// it, an outstanding department refusal silently stops blocking the moment anything else completes the
    /// task.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_completed_task_whose_clearance_was_refused_still_blocks()
    {
        SeedEmployees();
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;
        await ClearAllMandatory(initiated);

        var taskId = initiated.Departments
            .Single(d => d.ClearanceCategory == ClearanceCategory.IT)
            .Tasks.First(t => t.IsMandatory).Id;
        var refused = await Service().RecordClearanceAsync(
            new RecordClearanceInput(taskId, ClearanceStatus.PendingIssues, "Outstanding"));
        refused.IsSuccess.Should().BeTrue($"the arm depends on this flip. Error: {refused.Error}");

        // Now complete the task the way ExitInterviewService does — status only, clearance untouched.
        await using (var db = Db())
        {
            var task = db.OffboardingTaskInstances.Single(t => t.Id == taskId);
            task.Status = OnboardingTaskStatus.Completed;
            await db.SaveChangesAsync();
        }

        await using (var check = Db())
        {
            var stored = check.OffboardingTaskInstances.Single(t => t.Id == taskId);
            stored.Status.Should().Be(OnboardingTaskStatus.Completed,
                "the arm is only meaningful if the task really is completed");
            stored.ClearanceStatus.Should().Be(ClearanceStatus.PendingIssues,
                "...while the refusal still stands — that combination is the whole point");
        }

        var projected = (await Service().GetByIdAsync(initiated.Id)).Value!;
        var attempt = (await Service().CompleteAsync(initiated.Id)).Value!;

        projected.CanComplete.Should().BeFalse(
            "a department's refusal must outlive anything else marking the task done");
        projected.PendingMandatoryItems.Should().ContainSingle(i => i.TaskId == taskId)
            .Which.Reason.Should().Be("clearance_not_approved");
        attempt.Completed.Should().BeFalse("the enforcement path must agree with the projection");
    }

    /// <summary>
    /// The complement, and the reason the arm above must target a mandatory task: an OPTIONAL task that a
    /// department refused does not block completion. AC-5 gates on mandatory items only, so a refused exit
    /// survey must not strand an offboarding forever.
    /// </summary>
    [Fact]
    public async Task A_refused_OPTIONAL_clearance_does_not_block_completion()
    {
        SeedEmployees();
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;
        await ClearAllMandatory(initiated);

        var optional = initiated.Departments
            .SelectMany(d => d.Tasks)
            .FirstOrDefault(t => !t.IsMandatory);
        optional.Should().NotBeNull(
            "the default template must carry at least one optional task, or this arm is vacuous");

        var refused = await Service().RecordClearanceAsync(
            new RecordClearanceInput(optional!.Id, ClearanceStatus.PendingIssues, "Survey skipped"));
        refused.IsSuccess.Should().BeTrue(
            $"a silently-failed refusal would leave this arm asserting on the pre-flip state, where nothing "
            + $"blocked either — green whether the gate ignores optional refusals or the refusal never "
            + $"happened. Error: {refused.Error}");

        var projected = (await Service().GetByIdAsync(initiated.Id)).Value!;

        // POSITIVE GUARDIAN: the refusal really did land somewhere observable. Without this the two
        // assertions below describe exactly the state that already existed before the flip.
        projected.Departments
            .Single(d => d.ClearanceCategory == optional.ClearanceCategory)
            .Status.Should().Be("issues", "the department the refused task belongs to must show it");

        projected.PendingMandatoryItems.Should().BeEmpty();
        projected.CanComplete.Should().BeTrue("AC-5 gates on MANDATORY items only");
    }

    /// <summary>
    /// A soft-deleted mandatory task must not block — otherwise a removed task strands the offboarding
    /// forever with no way to satisfy it, the same permanently-blocked symptom B4 fixed by another route.
    ///
    /// <para>
    /// <b>What this arm does and does not prove.</b> It pins the end-to-end BEHAVIOUR, which is delivered by
    /// the global query filter on <c>OffboardingTaskInstance</c> (<c>AppDbContext.cs:285</c>) — the deleted
    /// row never reaches the gate at all. It does <i>not</i> pin the gate's own <c>!t.IsDeleted</c> clause:
    /// removing that clause leaves this arm green, which mutation testing confirmed. The clause is pinned
    /// instead by <see cref="OffboardingCompletionGateTests"/>, which calls the gate directly — it is a
    /// public static that any caller can hand an unfiltered list to, so its correctness must not depend on
    /// how the list was fetched.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_soft_deleted_mandatory_task_does_not_block()
    {
        SeedEmployees();
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;
        await ClearAllMandatory(initiated);

        var taskId = initiated.Departments
            .Single(d => d.ClearanceCategory == ClearanceCategory.IT)
            .Tasks.First(t => t.IsMandatory).Id;

        // Put it back into a blocking state, then soft-delete it.
        await using (var db = Db())
        {
            var task = db.OffboardingTaskInstances.Single(t => t.Id == taskId);
            task.Status = OnboardingTaskStatus.Pending;
            await db.SaveChangesAsync();
        }
        (await Service().GetByIdAsync(initiated.Id)).Value!.CanComplete.Should().BeFalse(
            "the arm is only meaningful if the task blocks BEFORE it is deleted");

        await using (var db = Db())
        {
            var task = db.OffboardingTaskInstances.Single(t => t.Id == taskId);
            task.IsDeleted = true;
            await db.SaveChangesAsync();
        }

        (await Service().GetByIdAsync(initiated.Id)).Value!.CanComplete.Should().BeTrue(
            "a removed task cannot be satisfied, so blocking on it strands the offboarding permanently");
    }

    /// <summary>
    /// The DTO documents the blocking list as being "in display order", and the dashboard renders it as a
    /// sentence. Nothing pinned that ordering, so <c>OrderByDescending</c> survived as a mutation.
    /// </summary>
    [Fact]
    public async Task Blocking_items_come_back_in_display_order()
    {
        SeedEmployees();
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;

        var projected = (await Service().GetByIdAsync(initiated.Id)).Value!;

        var expected = initiated.Departments
            .SelectMany(d => d.Tasks)
            .Where(t => t.IsMandatory)
            .OrderBy(t => t.SortOrder)
            .Select(t => t.Id)
            .ToList();
        expected.Should().HaveCountGreaterThan(1, "ordering is unobservable with a single item");
        projected.PendingMandatoryItems.Select(i => i.TaskId).Should().Equal(expected);
    }

    /// <summary>
    /// C3/GAP-025: completing an offboarding TERMINATES an employee, and did so with no audit row of any
    /// kind. `Employee` is IAuditExempt so the interceptor skips it, and this path hand-assigns the status
    /// rather than going through `EmployeeStatusService`, so it wrote neither the central trail nor the
    /// forensic table. Termination is the most compliance-sensitive employee event in the product and was
    /// the least traceable.
    /// </summary>
    [Fact]
    public async Task Complete_writes_a_central_audit_row_for_the_termination_c3()
    {
        SeedEmployees();
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;
        await ClearAllMandatory(initiated);

        (await Service().CompleteAsync(initiated.Id)).Value!.Completed.Should().BeTrue();

        using var db = Db();
        var central = db.AuditLogs
            .Where(a => a.ResourceId == _employeeId.ToString() && a.EventType == "Employee.StatusChanged")
            .ToList();

        central.Should().ContainSingle("a termination must be answerable from the audit viewer");
        central[0].TenantId.Should().Be(_tenantId,
            "audit_logs reads are scoped by an explicit TenantId predicate — a null-tenant row is invisible");
        central[0].ResourceType.Should().Be("Employee");
        central[0].After!.Should().Contain("Terminated");
        central[0].Detail.Should().Contain("offboarding",
            "the trail must say HOW the termination happened; both routes share one action name");
    }

    // ── NFR-2 tenant isolation ──────────────────────────────────────────

    [Fact]
    public async Task GetById_does_not_return_another_tenants_offboarding()
    {
        SeedEmployees();
        var initiated = (await Service().InitiateAsync(InitiateInput())).Value!;

        // A service scoped to a different tenant cannot read the instance.
        var otherTenant = Substitute.For<ITenantContext>();
        otherTenant.TenantId.Returns(Guid.NewGuid());
        otherTenant.IsResolved.Returns(true);
        var otherService = new OffboardingService(
            TestDbContextFactory.Create(otherTenant, _dbName), otherTenant, _hrUser,
            _authService, _sessionRevoker, _payrollFnF, Substitute.For<ILogger<OffboardingService>>());

        var result = await otherService.GetByIdAsync(initiated.Id);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private async Task ClearAllMandatory(Application.Features.Onboarding.DTOs.OffboardingInstanceDto instance)
    {
        foreach (var dept in instance.Departments)
        {
            foreach (var task in dept.Tasks.Where(t => t.IsMandatory))
            {
                await Service().RecordClearanceAsync(
                    new RecordClearanceInput(task.Id, ClearanceStatus.Approved, null));
            }
        }
    }

    private static Guid TaskId(
        Application.Features.Onboarding.DTOs.OffboardingInstanceDto instance,
        ClearanceCategory category, string? title = null)
    {
        var tasks = instance.Departments.Single(d => d.ClearanceCategory == category).Tasks;
        var task = title is null ? tasks.First() : tasks.Single(t => t.Title == title);
        return task.Id;
    }

    private static Application.Features.Onboarding.DTOs.DepartmentClearanceDto Department(
        Application.Features.Onboarding.DTOs.OffboardingInstanceDto instance, ClearanceCategory category)
        => instance.Departments.Single(d => d.ClearanceCategory == category);
}
