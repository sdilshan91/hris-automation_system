// ============================================================================
// US-NTF-006 Phase 5b (the FINAL seam) — RealPerformanceNotificationService unit tests (US-PRF-001..009).
//
// The Real service RESOLVES recipients from the DB (goal owner / reporting-line manager / HR pool / passed
// stakeholder) and dispatches BOTH legs (in-app + email) when the recipient has a linked User, else email-only
// against Employee.Email. The load-bearing behaviours genuinely observed here:
//   • MANAGER HOP — self-assessment-submitted routes to the submitter's ReportsToEmployeeId manager, NOT the
//     submitter (#2).
//   • HR POOL — review-auto-closed goes to the Performance.Review.All holders ONLY (not the non-holder); review-
//     disputed goes to the manager AND the HR pool (#3).
//   • detail=="hr" BROADCAST — goal-progress with detail=="hr" resolves the HR pool; a normal recipientEmployeeId
//     resolves that one employee (#5).
//   • EVENT-KEY MAPPING + PARAMETRIZED SUBTYPES — goal-assigned/removed → goal_assigned/goal_removed; cycle/pip
//     carry their subtype in the payload (#1/#4).
//   • EMAIL-ONLY FALLBACK — an employee with UserId==null is reached email-only via Employee.Email (#6).
//   • NEVER-THROW — a dispatcher failure must not break the committed performance write (#7).
//
// PROVIDER: EF Core InMemory AppDbContext (the HR-pool resolution is a real UserTenants⋈UserTenantRoles⋈
// RolePermissions query with IgnoreQueryFilters; the manager hop is a real ReportsToEmployeeId lookup) + a hand
// RecordingDispatcher. Mirrors RealPayroll/RealRecruitmentNotificationServiceTests' seeding.
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class RealPerformanceNotificationServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    // HR pool: two users holding Performance.Review.All + one decoy without it.
    private readonly Guid _hr1 = Guid.NewGuid();
    private readonly Guid _hr2 = Guid.NewGuid();
    private readonly Guid _nonHr = Guid.NewGuid();

    // Reporting chain: an employee (with a linked user) reporting to a manager (with a linked user).
    private readonly Guid _managerEmpId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _employeeEmpId = Guid.NewGuid();
    private readonly Guid _employeeUserId = Guid.NewGuid();

    // A second employee (with a linked user) used as an explicit recipient stakeholder.
    private readonly Guid _stakeholderEmpId = Guid.NewGuid();
    private readonly Guid _stakeholderUserId = Guid.NewGuid();

    // An employee with NO linked user → email-only fallback.
    private readonly Guid _emailOnlyEmpId = Guid.NewGuid();
    private const string EmailOnlyAddress = "no.user@acme.test";

    private readonly Guid _cycleId = Guid.NewGuid();
    private readonly Guid _goalId = Guid.NewGuid();
    private readonly Guid _pipId = Guid.NewGuid();

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;

    public RealPerformanceNotificationServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private RealPerformanceNotificationService Service(INotificationDispatcher dispatcher) =>
        new(Db(), dispatcher, _tenantContext, NullLogger<RealPerformanceNotificationService>.Instance);

    // ── Fake dispatcher ────────────────────────────────────────────────────────────────────────────

    /// <summary>Records dispatched requests per leg; optionally throws to prove the never-throw contract.</summary>
    private sealed class RecordingDispatcher : INotificationDispatcher
    {
        private readonly bool _throw;
        public List<NotificationRequest> InApp { get; } = new();
        public List<NotificationRequest> Email { get; } = new();
        public RecordingDispatcher(bool throwOnDispatch = false) => _throw = throwOnDispatch;

        public Task SendInAppAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            if (_throw) throw new InvalidOperationException("in-app dispatch boom");
            InApp.Add(request);
            return Task.CompletedTask;
        }

        public Task SendEmailAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            if (_throw) throw new InvalidOperationException("email dispatch boom");
            Email.Add(request);
            return Task.CompletedTask;
        }
    }

    // ── Seeding ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Two users holding a Performance.Review.All role + one user without it (the HR pool + a decoy).</summary>
    private async Task SeedHrPoolAsync()
    {
        using var db = Db();
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, TenantId = _tenantId, Name = "HR Manager" });
        db.RolePermissions.Add(new RolePermission { RoleId = roleId, Permission = PermissionCatalog.Performance.ReviewAll });

        foreach (var uid in new[] { _hr1, _hr2 })
        {
            var utId = Guid.NewGuid();
            db.UserTenants.Add(new UserTenant { Id = utId, UserId = uid, TenantId = _tenantId, Status = UserTenantStatus.Active });
            db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = utId, RoleId = roleId });
        }

        // A tenant user WITHOUT Performance.Review.All (a role with no such permission) — must never be notified.
        var plainRoleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = plainRoleId, TenantId = _tenantId, Name = "Employee" });
        var nonUt = Guid.NewGuid();
        db.UserTenants.Add(new UserTenant { Id = nonUt, UserId = _nonHr, TenantId = _tenantId, Status = UserTenantStatus.Active });
        db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = nonUt, RoleId = plainRoleId });

        await db.SaveChangesAsync();
    }

    /// <summary>Manager + employee (employee → manager via ReportsToEmployeeId), a stakeholder, and an email-only employee.</summary>
    private async Task SeedEmployeesAsync()
    {
        using var db = Db();
        db.Employees.Add(new Employee
        {
            Id = _managerEmpId, TenantId = _tenantId, FirstName = "Morgan", LastName = "Hale",
            Email = "morgan.hale@acme.test", UserId = _managerUserId,
        });
        db.Employees.Add(new Employee
        {
            Id = _employeeEmpId, TenantId = _tenantId, FirstName = "Jordan", LastName = "Rivera",
            Email = "jordan.rivera@acme.test", UserId = _employeeUserId, ReportsToEmployeeId = _managerEmpId,
        });
        db.Employees.Add(new Employee
        {
            Id = _stakeholderEmpId, TenantId = _tenantId, FirstName = "Sam", LastName = "Okafor",
            Email = "sam.okafor@acme.test", UserId = _stakeholderUserId,
        });
        db.Employees.Add(new Employee
        {
            Id = _emailOnlyEmpId, TenantId = _tenantId, FirstName = "Casey", LastName = "Lee",
            Email = EmailOnlyAddress, UserId = null,
        });
        await db.SaveChangesAsync();
    }

    private static string Subtype(NotificationRequest request, string parent)
    {
        using var doc = JsonDocument.Parse(request.PayloadJson);
        return doc.RootElement.GetProperty(parent).GetProperty("subtype").GetString()!;
    }

    // ── #1: goal-changed → the goal's employee (both legs) + eventType mapping ──

    [Fact]
    public async Task NotifyGoalChangedAsync_GoalAssigned_DispatchesToTheGoalsEmployee_BothLegs()
    {
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyGoalChangedAsync("goal-assigned", _goalId, _employeeEmpId, _cycleId);

        dispatcher.InApp.Should().ContainSingle();
        dispatcher.Email.Should().ContainSingle();
        dispatcher.InApp.Single().RecipientUserId.Should().Be(_employeeUserId);
        dispatcher.Email.Single().RecipientUserId.Should().Be(_employeeUserId);
        dispatcher.InApp.Single().EventKey.Should().Be("goal_assigned");
        dispatcher.Email.Single().EventKey.Should().Be("goal_assigned");
        dispatcher.Email.Single().TenantId.Should().Be(_tenantId);
    }

    [Fact]
    public async Task NotifyGoalChangedAsync_GoalRemoved_MapsEventTypeTo_goal_removed()
    {
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyGoalChangedAsync("goal-removed", _goalId, _employeeEmpId, _cycleId);

        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "goal_removed");
        dispatcher.InApp.Should().OnlyContain(r => r.EventKey == "goal_removed");
        dispatcher.Email.Single().RecipientUserId.Should().Be(_employeeUserId);
    }

    // ── #2: MANAGER HOP — self-assessment-submitted → the submitter's manager, NOT the submitter ──

    [Fact]
    public async Task NotifySelfAssessmentSubmittedAsync_DispatchesToManager_NotTheSubmitter()
    {
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifySelfAssessmentSubmittedAsync(
            selfAssessmentId: Guid.NewGuid(), employeeId: _employeeEmpId, cycleId: _cycleId);

        // Genuinely observed: the recipient is the reporting-line MANAGER (resolved via ReportsToEmployeeId),
        // never the submitter.
        dispatcher.InApp.Should().ContainSingle();
        dispatcher.Email.Should().ContainSingle();
        dispatcher.InApp.Single().RecipientUserId.Should().Be(_managerUserId);
        dispatcher.Email.Single().RecipientUserId.Should().Be(_managerUserId);
        dispatcher.Email.Single().RecipientUserId.Should().NotBe(_employeeUserId);
        dispatcher.Email.Single().EventKey.Should().Be("self_assessment_submitted");
    }

    // ── #3: HR POOL — auto-closed → HR holders ONLY; disputed → manager AND HR pool ──

    [Fact]
    public async Task NotifyReviewAutoClosedAsync_DispatchesToHrPoolOnly_NotTheNonHolder()
    {
        await SeedHrPoolAsync();
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyReviewAutoClosedAsync(
            managerReviewId: Guid.NewGuid(), employeeId: _employeeEmpId, cycleId: _cycleId);

        // Both legs go to exactly the two Performance.Review.All holders — NOT the non-holder, NOT the employee.
        dispatcher.Email.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { _hr1, _hr2 });
        dispatcher.InApp.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { _hr1, _hr2 });
        dispatcher.Email.Select(r => r.RecipientUserId).Should().NotContain(_nonHr);
        dispatcher.Email.Select(r => r.RecipientUserId).Should().NotContain(_employeeUserId);
        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "review_auto_closed");
    }

    [Fact]
    public async Task NotifyReviewDisputedAsync_DispatchesToManagerAndHrPool()
    {
        await SeedHrPoolAsync();
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyReviewDisputedAsync(
            managerReviewId: Guid.NewGuid(), employeeId: _employeeEmpId, cycleId: _cycleId);

        // Recipient union genuinely observed: the disputed employee's manager PLUS the HR pool (not the non-holder).
        var expected = new Guid?[] { _managerUserId, _hr1, _hr2 };
        dispatcher.Email.Select(r => r.RecipientUserId).Should().BeEquivalentTo(expected);
        dispatcher.InApp.Select(r => r.RecipientUserId).Should().BeEquivalentTo(expected);
        dispatcher.Email.Select(r => r.RecipientUserId).Should().NotContain(_nonHr);
        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "review_disputed");
    }

    // ── #4: PARAMETRIZED EVENTS — cycle/pip → fixed event key + subtype carried in the payload ──

    [Theory]
    [InlineData("phase-advanced")]
    [InlineData("cycle-launched")]
    public async Task NotifyCycleEventAsync_UsesCycleEventKey_AndCarriesSubtypeInPayload(string subtype)
    {
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyCycleEventAsync(subtype, _cycleId, _employeeEmpId, detail: "some detail");

        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "cycle_event");
        dispatcher.InApp.Should().OnlyContain(r => r.EventKey == "cycle_event");
        dispatcher.Email.Single().RecipientUserId.Should().Be(_employeeUserId);
        Subtype(dispatcher.Email.Single(), "event").Should().Be(subtype);
    }

    [Theory]
    [InlineData("pip-initiated")]
    [InlineData("pip-checkin-due")]
    public async Task NotifyPipEventAsync_UsesPipEventKey_AndCarriesSubtypeInPayload(string subtype)
    {
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyPipEventAsync(
            subtype, _pipId, employeeId: _employeeEmpId, recipientEmployeeId: _stakeholderEmpId, detail: "d");

        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "pip_event");
        // Recipient is the passed stakeholder, not the PIP subject employee.
        dispatcher.Email.Single().RecipientUserId.Should().Be(_stakeholderUserId);
        Subtype(dispatcher.Email.Single(), "pip").Should().Be(subtype);
    }

    // ── #5: goal-progress detail=="hr" → HR pool; a normal recipient → that employee; comment-added mapping ──

    [Fact]
    public async Task NotifyGoalProgressAsync_DetailHr_ResolvesTheHrPool()
    {
        await SeedHrPoolAsync();
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        // detail == "hr" is the goal-blocked HR broadcast: recipientEmployeeId flows in as null.
        await Service(dispatcher).NotifyGoalProgressAsync(
            "goal-blocked", _goalId, _employeeEmpId, recipientEmployeeId: null, detail: "hr");

        dispatcher.Email.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { _hr1, _hr2 });
        dispatcher.InApp.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { _hr1, _hr2 });
        dispatcher.Email.Select(r => r.RecipientUserId).Should().NotContain(_nonHr);
        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "goal_blocked");
    }

    [Fact]
    public async Task NotifyGoalProgressAsync_NormalRecipient_ResolvesThatEmployee()
    {
        await SeedHrPoolAsync();
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyGoalProgressAsync(
            "goal-progress-updated", _goalId, _employeeEmpId, recipientEmployeeId: _stakeholderEmpId, detail: "50%");

        // The single passed stakeholder is reached — NOT the HR pool.
        dispatcher.Email.Should().ContainSingle();
        dispatcher.Email.Single().RecipientUserId.Should().Be(_stakeholderUserId);
        dispatcher.Email.Select(r => r.RecipientUserId).Should().NotContain(_hr1);
        dispatcher.Email.Single().EventKey.Should().Be("goal_progress_updated");
    }

    [Fact]
    public async Task NotifyGoalProgressAsync_CommentAdded_MapsTo_goal_comment_added()
    {
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyGoalProgressAsync(
            "goal-comment-added", _goalId, _employeeEmpId, recipientEmployeeId: _employeeEmpId, detail: null);

        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "goal_comment_added");
        dispatcher.Email.Single().RecipientUserId.Should().Be(_employeeUserId);
    }

    // ── #6: EMAIL-ONLY FALLBACK — an employee with no linked user → email-only via Employee.Email ──

    [Fact]
    public async Task NotifyGoalChangedAsync_EmployeeWithoutUser_FallsBackToEmailOnly()
    {
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyGoalChangedAsync("goal-assigned", _goalId, _emailOnlyEmpId, _cycleId);

        // No in-app leg (no User row); a single email addressed to the employee's own address.
        dispatcher.InApp.Should().BeEmpty();
        dispatcher.Email.Should().ContainSingle();
        dispatcher.Email.Single().RecipientEmail.Should().Be(EmailOnlyAddress);
        dispatcher.Email.Single().RecipientUserId.Should().BeNull();
        dispatcher.Email.Single().EventKey.Should().Be("goal_assigned");
    }

    // ── #7: NEVER-THROW — a dispatcher failure must not break the committed performance write ──

    [Fact]
    public async Task NotifyGoalChangedAsync_DispatcherThrows_DoesNotThrow()
    {
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher(throwOnDispatch: true);

        var act = () => Service(dispatcher).NotifyGoalChangedAsync("goal-assigned", _goalId, _employeeEmpId, _cycleId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyReviewDisputedAsync_DispatcherThrows_DoesNotThrow()
    {
        await SeedHrPoolAsync();
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher(throwOnDispatch: true);

        var act = () => Service(dispatcher).NotifyReviewDisputedAsync(Guid.NewGuid(), _employeeEmpId, _cycleId);

        await act.Should().NotThrowAsync();
    }
}
