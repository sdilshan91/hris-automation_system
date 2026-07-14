// ============================================================================
// US-PRF-009: Goal tracking with progress updates — service unit tests.
//
// Drives GoalProgressService through the real EF Core InMemory provider (so goal_progress_update,
// goal_progress_attachment and goal_comment rows are actually persisted). Covers:
//   - AC-1: My Goals lists active goals with current progress = the latest update.
//   - AC-2/BR-1: posting is allowed during the active cycle window and refused outside it.
//   - NFR-3/FR-3: append-only — each post adds a NEW immutable row; there is no update/delete path.
//   - BR-2: progress=100 auto-sets Completed; an explicit AtRisk/Blocked override is honoured even at 100%.
//   - BR-3: a Blocked update notifies the manager AND HR (the broadcast recipient).
//   - FR-5: every posted update notifies the manager.
//   - FR-4: the manager team summary computes weighted overall completion + at-risk counts, direct-report scoped.
//   - FR-8: a manager/HR comment is added; the employee cannot comment; an unrelated user is refused.
//   - BR-5 visibility: a peer gets 403 on the timeline.
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

public sealed class GoalProgressServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IPerformanceNotificationService _notifications = Substitute.For<IPerformanceNotificationService>();

    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _employeeUserId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();
    private readonly Guid _hrUserId = Guid.NewGuid();

    private readonly Guid _managerEmpId = Guid.NewGuid();
    private readonly Guid _employeeEmpId = Guid.NewGuid();
    private readonly Guid _otherEmpId = Guid.NewGuid();
    private readonly Guid _hrEmpId = Guid.NewGuid();

    private readonly Guid _cycleId = Guid.NewGuid();
    private readonly Guid _goalAId = Guid.NewGuid();
    private readonly Guid _goalBId = Guid.NewGuid();

    public GoalProgressServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private GoalProgressService Service(ICurrentUser user) => new(
        Db(), _tenantContext, user, _notifications, Substitute.For<ILogger<GoalProgressService>>());

    private ICurrentUser User(Guid userId, params string[] permissions)
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(userId);
        u.IsAuthenticated.Returns(true);
        u.Email.Returns("user@t.com");
        u.Permissions.Returns(permissions);
        return u;
    }

    private ICurrentUser EmployeeUser() => User(_employeeUserId, PermissionCatalog.Performance.ReadSelf);
    private ICurrentUser ManagerUser() => User(_managerUserId, PermissionCatalog.Performance.ReviewTeam);
    private ICurrentUser HrUser() => User(_hrUserId, PermissionCatalog.Performance.ReviewAll);
    private ICurrentUser OtherUser() => User(_otherUserId, PermissionCatalog.Performance.ReadSelf);

    /// <summary>An active cycle whose goal-setting CLOSED a week ago and whose self-assessment hasn't started ⇒ tracking open.</summary>
    private static AppraisalCycle OpenCycle(Guid id, Guid tenantId)
    {
        var now = DateTime.UtcNow;
        return new AppraisalCycle
        {
            Id = id,
            TenantId = tenantId,
            Name = "FY2026",
            Status = AppraisalCycleStatus.Active,
            StartDate = now.AddDays(-30),
            EndDate = now.AddDays(60),
            GoalSettingStart = now.AddDays(-30),
            GoalSettingEnd = now.AddDays(-7),     // closed
            SelfAssessmentStart = now.AddDays(40), // not started ⇒ tracking window open
            SelfAssessmentEnd = now.AddDays(50),
        };
    }

    private async Task SeedAsync(bool trackingOpen = true, int nudgeDays = 14)
    {
        using var db = Db();
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "t", Name = "T", Status = TenantStatus.Active, StaleGoalNudgeDays = nudgeDays });

        db.Employees.Add(new Employee { Id = _managerEmpId, TenantId = _tenantId, UserId = _managerUserId, EmployeeNo = "MGR", FirstName = "Grace", LastName = "Hopper", Email = "g@t.com", Status = EmployeeStatus.Active });
        db.Employees.Add(new Employee { Id = _employeeEmpId, TenantId = _tenantId, UserId = _employeeUserId, EmployeeNo = "EMP", FirstName = "Ada", LastName = "Lovelace", Email = "a@t.com", Status = EmployeeStatus.Active, ReportsToEmployeeId = _managerEmpId });
        db.Employees.Add(new Employee { Id = _otherEmpId, TenantId = _tenantId, UserId = _otherUserId, EmployeeNo = "OTH", FirstName = "Kurt", LastName = "Godel", Email = "k@t.com", Status = EmployeeStatus.Active });
        db.Employees.Add(new Employee { Id = _hrEmpId, TenantId = _tenantId, UserId = _hrUserId, EmployeeNo = "HR", FirstName = "Hedy", LastName = "Lamarr", Email = "h@t.com", Status = EmployeeStatus.Active });

        var cycle = OpenCycle(_cycleId, _tenantId);
        if (!trackingOpen)
            cycle.GoalSettingEnd = DateTime.UtcNow.AddDays(7); // goal-setting still open ⇒ tracking NOT open
        db.AppraisalCycles.Add(cycle);

        db.Goals.Add(new Goal { Id = _goalAId, TenantId = _tenantId, CycleId = _cycleId, EmployeeId = _employeeEmpId, Title = "Ship API", Weight = 60, Status = GoalStatus.Acknowledged, TargetValue = "100%", MeasurementUnit = "%", DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)) });
        db.Goals.Add(new Goal { Id = _goalBId, TenantId = _tenantId, CycleId = _cycleId, EmployeeId = _employeeEmpId, Title = "Reduce bugs", Weight = 40, Status = GoalStatus.Acknowledged, TargetValue = "0", MeasurementUnit = "bugs", DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(45)) });

        await db.SaveChangesAsync();
    }

    private AddGoalProgressInput Update(Guid goalId, int pct, GoalProgressStatus status = GoalProgressStatus.InProgress, string? notes = "progress")
        => new(goalId, pct, status, notes, []);

    // ── AC-1: My Goals ──────────────────────────────────────────────────

    [Fact]
    public async Task GetMyGoals_returns_active_goals_with_current_progress()
    {
        await SeedAsync();
        await Service(EmployeeUser()).AddProgressUpdateAsync(Update(_goalAId, 40));

        var result = await Service(EmployeeUser()).GetMyGoalsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
        var goalA = result.Value!.Single(g => g.GoalId == _goalAId);
        goalA.CurrentProgressPct.Should().Be(40);
        goalA.CurrentStatus.Should().Be(GoalProgressStatus.InProgress);
        goalA.LastUpdatedAt.Should().NotBeNull();
        var goalB = result.Value!.Single(g => g.GoalId == _goalBId);
        goalB.CurrentProgressPct.Should().Be(0);
        goalB.CurrentStatus.Should().Be(GoalProgressStatus.NotStarted);
        goalB.LastUpdatedAt.Should().BeNull();
    }

    // ── AC-2/BR-1: window gate ──────────────────────────────────────────

    [Fact]
    public async Task AddProgress_during_active_window_succeeds()
    {
        await SeedAsync(trackingOpen: true);
        var result = await Service(EmployeeUser()).AddProgressUpdateAsync(Update(_goalAId, 25));
        result.IsSuccess.Should().BeTrue();
        result.Value!.Updates.Should().HaveCount(1);
    }

    [Fact]
    public async Task AddProgress_outside_window_is_refused()
    {
        await SeedAsync(trackingOpen: false);
        var result = await Service(EmployeeUser()).AddProgressUpdateAsync(Update(_goalAId, 25));
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("tracking_window_closed");
    }

    [Fact]
    public async Task AddProgress_on_another_employees_goal_is_forbidden()
    {
        await SeedAsync();
        var result = await Service(OtherUser()).AddProgressUpdateAsync(Update(_goalAId, 25));
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
    }

    // ── NFR-3/FR-3: append-only ─────────────────────────────────────────

    [Fact]
    public async Task AddProgress_is_append_only_each_post_adds_a_new_row()
    {
        await SeedAsync();
        await Service(EmployeeUser()).AddProgressUpdateAsync(Update(_goalAId, 20));
        await Service(EmployeeUser()).AddProgressUpdateAsync(Update(_goalAId, 50));
        var result = await Service(EmployeeUser()).AddProgressUpdateAsync(Update(_goalAId, 75));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Updates.Should().HaveCount(3);
        // Chronological + the earliest row UNCHANGED (immutable history).
        result.Value!.Updates.Select(u => u.ProgressPct).Should().ContainInOrder(20, 50, 75);

        using var db = Db();
        db.GoalProgressUpdates.IgnoreQueryFilters().Count(u => u.GoalId == _goalAId).Should().Be(3);
    }

    [Fact]
    public void Service_has_no_update_or_delete_method_for_a_progress_update()
    {
        // NFR-3: the immutability contract is structural — there is no mutate/delete API surface.
        var methods = typeof(IGoalProgressService).GetMethods().Select(m => m.Name).ToList();
        methods.Should().NotContain(n => n.Contains("Delete") || n.Contains("Edit") || n.Contains("Update", StringComparison.Ordinal) && n.Contains("Progress") && !n.Contains("AddProgress"));
        methods.Should().NotContain("DeleteProgressUpdateAsync");
        methods.Should().NotContain("EditProgressUpdateAsync");
    }

    // ── BR-2: 100% auto-Completed ───────────────────────────────────────

    [Fact]
    public async Task AddProgress_at_100_with_no_explicit_status_auto_sets_Completed()
    {
        // ISSUE-140: the request Status is a required non-nullable enum ⇒ an omitted status deserializes to the
        // NotStarted default. That "no explicit choice at 100%" is the ONLY case BR-2 auto-completes.
        await SeedAsync();
        var result = await Service(EmployeeUser()).AddProgressUpdateAsync(Update(_goalAId, 100, GoalProgressStatus.NotStarted));
        result.Value!.CurrentStatus.Should().Be(GoalProgressStatus.Completed);
    }

    [Fact]
    public async Task AddProgress_at_100_with_explicit_InProgress_is_honoured_ISSUE140()
    {
        // ISSUE-140: an EXPLICIT InProgress at 100% ("done, pending sign-off") is the employee's overridable
        // choice (BR-2) — it must be persisted as InProgress, NOT force-converted to Completed.
        await SeedAsync();
        var result = await Service(EmployeeUser()).AddProgressUpdateAsync(Update(_goalAId, 100, GoalProgressStatus.InProgress));
        result.Value!.CurrentStatus.Should().Be(GoalProgressStatus.InProgress);
    }

    [Fact]
    public async Task AddProgress_at_100_honours_an_explicit_override()
    {
        await SeedAsync();
        var result = await Service(EmployeeUser()).AddProgressUpdateAsync(Update(_goalAId, 100, GoalProgressStatus.AtRisk));
        // Employee can override (BR-2) — an explicit AtRisk/Blocked is NOT forced to Completed.
        result.Value!.CurrentStatus.Should().Be(GoalProgressStatus.AtRisk);
    }

    [Fact]
    public async Task AddProgress_at_100_Blocked_is_honoured()
    {
        await SeedAsync();
        var result = await Service(EmployeeUser()).AddProgressUpdateAsync(Update(_goalAId, 100, GoalProgressStatus.Blocked));
        result.Value!.CurrentStatus.Should().Be(GoalProgressStatus.Blocked);
    }

    // ── BR-3/FR-5: notifications ────────────────────────────────────────

    [Fact]
    public async Task AddProgress_notifies_the_manager_every_time()
    {
        await SeedAsync();
        _notifications.ClearReceivedCalls();
        await Service(EmployeeUser()).AddProgressUpdateAsync(Update(_goalAId, 30));

        await _notifications.Received().NotifyGoalProgressAsync(
            "goal-progress-updated", _goalAId, _employeeEmpId, _managerEmpId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddProgress_Blocked_notifies_manager_and_HR()
    {
        await SeedAsync();
        _notifications.ClearReceivedCalls();
        await Service(EmployeeUser()).AddProgressUpdateAsync(Update(_goalAId, 30, GoalProgressStatus.Blocked));

        // BR-3: manager.
        await _notifications.Received().NotifyGoalProgressAsync(
            "goal-blocked", _goalAId, _employeeEmpId, _managerEmpId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        // BR-3: HR (broadcast recipient = null).
        await _notifications.Received().NotifyGoalProgressAsync(
            "goal-blocked", _goalAId, _employeeEmpId, null, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // ── AC-4/FR-4: manager team summary ─────────────────────────────────

    [Fact]
    public async Task TeamSummary_computes_weighted_completion_scoped_to_direct_reports()
    {
        await SeedAsync();
        // Goal A weight 60 @ 100%, Goal B weight 40 @ 50% ⇒ (60*100 + 40*50)/100 = 80.
        await Service(EmployeeUser()).AddProgressUpdateAsync(Update(_goalAId, 100));
        await Service(EmployeeUser()).AddProgressUpdateAsync(Update(_goalBId, 50, GoalProgressStatus.AtRisk));

        var result = await Service(ManagerUser()).GetTeamSummaryAsync();

        result.IsSuccess.Should().BeTrue();
        var row = result.Value!.Single(r => r.EmployeeId == _employeeEmpId);
        row.OverallCompletionPct.Should().Be(80);
        row.GoalCount.Should().Be(2);
        row.AtRiskGoalCount.Should().Be(1); // Goal B AtRisk.
        row.LastUpdatedAt.Should().NotBeNull();
        // Direct-report scope: a manager only sees their reports, never the unrelated employee.
        result.Value!.Should().NotContain(r => r.EmployeeId == _otherEmpId);
    }

    [Fact]
    public async Task TeamSummary_HR_sees_all_employees()
    {
        await SeedAsync();
        var result = await Service(HrUser()).GetTeamSummaryAsync();
        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().Contain(r => r.EmployeeId == _employeeEmpId);
        result.Value!.Should().Contain(r => r.EmployeeId == _otherEmpId);
    }

    [Fact]
    public async Task TeamSummary_employee_without_review_permission_is_forbidden()
    {
        await SeedAsync();
        var result = await Service(EmployeeUser()).GetTeamSummaryAsync();
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task EmployeeGoals_drilldown_is_direct_report_scoped()
    {
        await SeedAsync();
        // Manager can drill into their report.
        (await Service(ManagerUser()).GetEmployeeGoalsAsync(_employeeEmpId)).IsSuccess.Should().BeTrue();
        // But not into an unrelated employee.
        var denied = await Service(ManagerUser()).GetEmployeeGoalsAsync(_otherEmpId);
        denied.IsFailure.Should().BeTrue();
        denied.StatusCode.Should().Be(403);
    }

    // ── AC-3/BR-5: timeline visibility ──────────────────────────────────

    [Fact]
    public async Task Timeline_visible_to_employee_manager_and_HR_but_not_a_peer()
    {
        await SeedAsync();
        await Service(EmployeeUser()).AddProgressUpdateAsync(Update(_goalAId, 30));

        (await Service(EmployeeUser()).GetGoalTimelineAsync(_goalAId)).IsSuccess.Should().BeTrue();
        (await Service(ManagerUser()).GetGoalTimelineAsync(_goalAId)).IsSuccess.Should().BeTrue();
        (await Service(HrUser()).GetGoalTimelineAsync(_goalAId)).IsSuccess.Should().BeTrue();

        var peer = await Service(OtherUser()).GetGoalTimelineAsync(_goalAId);
        peer.IsFailure.Should().BeTrue();
        peer.StatusCode.Should().Be(403);
    }

    // ── FR-8: comment thread ────────────────────────────────────────────

    [Fact]
    public async Task Comment_thread_is_two_way_owner_manager_and_HR_can_but_a_peer_cannot_ISSUE141()
    {
        await SeedAsync();
        await Service(EmployeeUser()).AddProgressUpdateAsync(Update(_goalAId, 30));

        // Manager may comment on their report's goal (FR-8).
        var mgrComment = await Service(ManagerUser()).AddCommentAsync(new AddGoalCommentInput(_goalAId, null, "Good progress, keep it up."));
        mgrComment.IsSuccess.Should().BeTrue();
        mgrComment.Value!.Comments.Should().ContainSingle();
        mgrComment.Value!.Comments[0].Body.Should().Be("Good progress, keep it up.");

        // HR may comment.
        (await Service(HrUser()).AddCommentAsync(new AddGoalCommentInput(_goalAId, null, "Noted."))).IsSuccess.Should().BeTrue();

        // ISSUE-141: the goal OWNER may now REPLY on their OWN goal's thread (two-way).
        var ownerReply = await Service(EmployeeUser()).AddCommentAsync(new AddGoalCommentInput(_goalAId, null, "Thanks, will do!"));
        ownerReply.IsSuccess.Should().BeTrue();
        ownerReply.Value!.Comments.Should().Contain(c => c.Body == "Thanks, will do!" && c.AuthorEmployeeId == _employeeEmpId);
    }

    [Fact]
    public async Task Comment_by_manager_notifies_the_owner_ISSUE297()
    {
        await SeedAsync();
        _notifications.ClearReceivedCalls();

        // A manager comment on the report's goal notifies the OWNER (the counterparty).
        await Service(ManagerUser()).AddCommentAsync(new AddGoalCommentInput(_goalAId, null, "Good progress."));

        // Owner (counterparty) IS notified …
        await _notifications.Received().NotifyGoalProgressAsync(
            "goal-comment-added", _goalAId, _employeeEmpId, _employeeEmpId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        // … and the manager is NEVER notified about their own comment (symmetric with the owner-reply case).
        await _notifications.DidNotReceive().NotifyGoalProgressAsync(
            "goal-comment-added", _goalAId, _employeeEmpId, _managerEmpId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Owner_reply_with_no_manager_on_file_skips_the_notification_ISSUE297()
    {
        // ISSUE-297 null-branch: when the goal owner replies but has NO manager on file, there is no
        // counterparty to notify — the dispatch must be skipped (never fall back to notifying the author,
        // and never dispatch a null-recipient HR broadcast). `_managerEmpId` has no ReportsToEmployeeId.
        await SeedAsync();
        var mgrGoalId = Guid.NewGuid();
        using (var db = Db())
        {
            db.Goals.Add(new Goal
            {
                Id = mgrGoalId, TenantId = _tenantId, CycleId = _cycleId, EmployeeId = _managerEmpId,
                Title = "Manager's own goal", Weight = 100, Status = GoalStatus.Acknowledged,
                TargetValue = "100%", MeasurementUnit = "%", DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            });
            await db.SaveChangesAsync();
        }
        _notifications.ClearReceivedCalls();

        // The manager (goal owner, no manager of their own) replies on their own goal.
        var reply = await Service(ManagerUser()).AddCommentAsync(new AddGoalCommentInput(mgrGoalId, null, "Note to self."));
        reply.IsSuccess.Should().BeTrue();

        // No goal-comment-added notification is dispatched at all.
        await _notifications.DidNotReceive().NotifyGoalProgressAsync(
            "goal-comment-added", Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Owner_reply_notifies_the_manager_not_the_owner_ISSUE297()
    {
        await SeedAsync();
        _notifications.ClearReceivedCalls();

        // ISSUE-297: when the OWNER replies, the notification must go to the MANAGER (the counterparty),
        // never back to the owner about their own comment.
        await Service(EmployeeUser()).AddCommentAsync(new AddGoalCommentInput(_goalAId, null, "Thanks!"));

        // Manager (counterparty) IS notified …
        await _notifications.Received().NotifyGoalProgressAsync(
            "goal-comment-added", _goalAId, _employeeEmpId, _managerEmpId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        // … and the owner is NEVER notified about their own reply.
        await _notifications.DidNotReceive().NotifyGoalProgressAsync(
            "goal-comment-added", _goalAId, _employeeEmpId, _employeeEmpId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_read_self_peer_cannot_comment_on_someone_elses_goal_ISSUE141()
    {
        // ISSUE-141 security scope: a Read.Self caller who is NOT the goal owner (and not the owner's manager/HR)
        // is refused — the two-way thread is scoped to the goal they own.
        await SeedAsync();
        await Service(EmployeeUser()).AddProgressUpdateAsync(Update(_goalAId, 30));

        var peerComment = await Service(OtherUser()).AddCommentAsync(new AddGoalCommentInput(_goalAId, null, "not my goal"));
        peerComment.IsFailure.Should().BeTrue();
        peerComment.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Comment_anchored_to_a_foreign_update_is_rejected()
    {
        await SeedAsync();
        var foreignUpdate = Guid.NewGuid();
        var result = await Service(ManagerUser()).AddCommentAsync(new AddGoalCommentInput(_goalAId, foreignUpdate, "x"));
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
    }

    // ── Tenant scoping ──────────────────────────────────────────────────

    [Fact]
    public async Task Unresolved_tenant_is_refused()
    {
        var unresolved = Substitute.For<ITenantContext>();
        unresolved.IsResolved.Returns(false);
        var svc = new GoalProgressService(
            TestDbContextFactory.Create(unresolved, _dbName), unresolved, EmployeeUser(), _notifications,
            Substitute.For<ILogger<GoalProgressService>>());

        var result = await svc.GetMyGoalsAsync();
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }
}
