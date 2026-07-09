// ============================================================================
// US-NTF-006 Phase 3 — LeaveNotificationService EMAIL leg.
//
// In addition to the pre-existing in-app leg (INotificationService, covered by
// LeaveNotificationServiceTests), every leave lifecycle method now ALSO dispatches
// an email via INotificationDispatcher.SendEmailAsync (injected as the last optional
// nullable ctor param). Each method must:
//   • still fire the in-app leg (existing behavior preserved), AND
//   • call SendEmailAsync exactly once with the correct catalog EventKey and the
//     correct RecipientUserId (employee vs approver/manager per the table), carrying
//     a payload with the leave fields (type/dates/reason).
//   Method                       EventKey          Recipient
//   NotifyLeaveRequestedAsync     leave_requested   approver/manager
//   NotifyLeaveApprovedAsync      leave_approved    employee
//   NotifyLeaveRejectedAsync      leave_rejected    employee
//   NotifyLeaveCancelledAsync     leave_cancelled   manager
//   NotifyLopAssignedAsync        leave_lop         employee
//
// Never-throw: the email leg is null-guarded + try/catch — a throwing dispatcher must
// NOT break the leave flow, and the in-app leg must still have happened.
//
// Faking: INotificationDispatcher is an NSubstitute mock; the dispatched
// NotificationRequest is captured with Arg.Do so we can assert EventKey +
// RecipientUserId + a payload field. Backing store is a named EF InMemory
// AppDbContext seeded with tenant/employee/manager/leave-type/leave-request.
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class LeaveNotificationEmailLegTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly INotificationDispatcher _dispatcher = Substitute.For<INotificationDispatcher>();

    public LeaveNotificationEmailLegTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    // Construct the service WITH the email dispatcher wired (the new optional last ctor param).
    private LeaveNotificationService Service() =>
        new(_notifications, Db(), _tenantContext, NullLogger<LeaveNotificationService>.Instance, _dispatcher);

    private Guid SeedTenant()
    {
        using var db = Db();
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme Corp", Status = TenantStatus.Active });
        db.SaveChanges();
        return _tenantId;
    }

    private Guid SeedEmployee(Guid userId, string no, string first, string email)
    {
        var id = Guid.NewGuid();
        using var db = Db();
        db.Employees.Add(new Employee
        {
            Id = id,
            TenantId = _tenantId,
            UserId = userId,
            EmployeeNo = no,
            FirstName = first,
            LastName = "Tester",
            Email = email,
            DateOfJoining = DateTime.UtcNow.AddYears(-1),
            EmploymentType = EmploymentType.FullTime,
            IsActive = true,
        });
        db.SaveChanges();
        return id;
    }

    private Guid SeedLeaveType(string name)
    {
        var id = Guid.NewGuid();
        using var db = Db();
        db.LeaveTypes.Add(new LeaveType { Id = id, TenantId = _tenantId, Name = name, AnnualEntitlement = 20 });
        db.SaveChanges();
        return id;
    }

    private Guid SeedLeaveRequest(Guid employeeId, Guid leaveTypeId, string reason)
    {
        var id = Guid.NewGuid();
        using var db = Db();
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = id,
            TenantId = _tenantId,
            EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId,
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 7, 5),
            TotalDays = 5,
            Reason = reason,
            Status = LeaveRequestStatus.Pending,
        });
        db.SaveChanges();
        return id;
    }

    // Capture the single NotificationRequest passed to SendEmailAsync.
    private List<NotificationRequest> CaptureEmails()
    {
        var captured = new List<NotificationRequest>();
        _dispatcher.SendEmailAsync(Arg.Do<NotificationRequest>(captured.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return captured;
    }

    private static JsonElement Leave(NotificationRequest req)
        => JsonDocument.Parse(req.PayloadJson).RootElement.GetProperty("leave");

    // ── NotifyLeaveRequested → in-app to manager AND email leave_requested to the MANAGER's user ──
    [Fact]
    public async Task NotifyLeaveRequested_FiresInApp_AndEmailsManager_WithLeaveRequestedEvent()
    {
        SeedTenant();
        var leaveTypeId = SeedLeaveType("Annual Leave");
        var employeeUserId = Guid.NewGuid();
        var employeeId = SeedEmployee(employeeUserId, "EMP-0001", "Ada", "ada@acme.com");
        var managerUserId = Guid.NewGuid();
        var managerEmployeeId = SeedEmployee(managerUserId, "EMP-MGR", "Grace", "grace@acme.com");
        var leaveRequestId = SeedLeaveRequest(employeeId, leaveTypeId, "Family vacation");
        var captured = CaptureEmails();

        await Service().NotifyLeaveRequestedAsync(leaveRequestId, employeeId, managerEmployeeId);

        // In-app leg preserved (recipient = manager's user).
        await _notifications.Received(1).CreateAndDispatchAsync(
            _tenantId, managerUserId, "leave.requested",
            Arg.Any<string>(), Arg.Any<string>(),
            "LeaveRequest", leaveRequestId.ToString(), Arg.Any<CancellationToken>());

        // Email leg: exactly once, correct EventKey + recipient (manager), payload carries leave fields.
        await _dispatcher.Received(1).SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
        var req = captured.Should().ContainSingle().Subject;
        req.EventKey.Should().Be("leave_requested");
        req.RecipientUserId.Should().Be(managerUserId);
        req.TenantId.Should().Be(_tenantId);
        Leave(req).GetProperty("type").GetString().Should().Be("Annual Leave");
        Leave(req).GetProperty("reason").GetString().Should().Be("Family vacation");
    }

    // ── NotifyLeaveApproved → in-app to employee AND email leave_approved to the EMPLOYEE's user ──
    [Fact]
    public async Task NotifyLeaveApproved_FiresInApp_AndEmailsEmployee_WithLeaveApprovedEvent()
    {
        SeedTenant();
        var leaveTypeId = SeedLeaveType("Annual Leave");
        var employeeUserId = Guid.NewGuid();
        var employeeId = SeedEmployee(employeeUserId, "EMP-0001", "Ada", "ada@acme.com");
        var approverId = SeedEmployee(Guid.NewGuid(), "EMP-MGR", "Grace", "grace@acme.com");
        var leaveRequestId = SeedLeaveRequest(employeeId, leaveTypeId, "Family vacation");
        var captured = CaptureEmails();

        await Service().NotifyLeaveApprovedAsync(leaveRequestId, employeeId, approverId);

        await _notifications.Received(1).CreateAndDispatchAsync(
            _tenantId, employeeUserId, "leave.approved",
            Arg.Any<string>(), Arg.Any<string>(),
            "LeaveRequest", leaveRequestId.ToString(), Arg.Any<CancellationToken>());

        await _dispatcher.Received(1).SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
        var req = captured.Should().ContainSingle().Subject;
        req.EventKey.Should().Be("leave_approved");
        req.RecipientUserId.Should().Be(employeeUserId);
        Leave(req).GetProperty("type").GetString().Should().Be("Annual Leave");
    }

    // ── NotifyLeaveRejected → in-app to employee AND email leave_rejected to the EMPLOYEE's user, reason in payload ──
    [Fact]
    public async Task NotifyLeaveRejected_FiresInApp_AndEmailsEmployee_WithLeaveRejectedEvent_AndReason()
    {
        SeedTenant();
        var leaveTypeId = SeedLeaveType("Annual Leave");
        var employeeUserId = Guid.NewGuid();
        var employeeId = SeedEmployee(employeeUserId, "EMP-0001", "Ada", "ada@acme.com");
        var approverId = SeedEmployee(Guid.NewGuid(), "EMP-MGR", "Grace", "grace@acme.com");
        var leaveRequestId = SeedLeaveRequest(employeeId, leaveTypeId, "Family vacation");
        var captured = CaptureEmails();

        await Service().NotifyLeaveRejectedAsync(leaveRequestId, employeeId, approverId, "Insufficient coverage");

        await _notifications.Received(1).CreateAndDispatchAsync(
            _tenantId, employeeUserId, "leave.rejected",
            Arg.Any<string>(), Arg.Is<string>(m => m.Contains("Insufficient coverage")),
            "LeaveRequest", leaveRequestId.ToString(), Arg.Any<CancellationToken>());

        await _dispatcher.Received(1).SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
        var req = captured.Should().ContainSingle().Subject;
        req.EventKey.Should().Be("leave_rejected");
        req.RecipientUserId.Should().Be(employeeUserId);
        // The rejection reason (passed to the method) takes precedence in the payload.
        Leave(req).GetProperty("reason").GetString().Should().Be("Insufficient coverage");
    }

    // ── NotifyLeaveCancelled → in-app to manager AND email leave_cancelled to the MANAGER's user ──
    [Fact]
    public async Task NotifyLeaveCancelled_FiresInApp_AndEmailsManager_WithLeaveCancelledEvent()
    {
        SeedTenant();
        var leaveTypeId = SeedLeaveType("Annual Leave");
        var employeeId = SeedEmployee(Guid.NewGuid(), "EMP-0001", "Ada", "ada@acme.com");
        var managerUserId = Guid.NewGuid();
        var managerEmployeeId = SeedEmployee(managerUserId, "EMP-MGR", "Grace", "grace@acme.com");
        var leaveRequestId = SeedLeaveRequest(employeeId, leaveTypeId, "Family vacation");
        var captured = CaptureEmails();

        await Service().NotifyLeaveCancelledAsync(leaveRequestId, employeeId, managerEmployeeId, "Plans changed");

        await _notifications.Received(1).CreateAndDispatchAsync(
            _tenantId, managerUserId, "leave.cancelled",
            Arg.Any<string>(), Arg.Any<string>(),
            "LeaveRequest", leaveRequestId.ToString(), Arg.Any<CancellationToken>());

        await _dispatcher.Received(1).SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
        var req = captured.Should().ContainSingle().Subject;
        req.EventKey.Should().Be("leave_cancelled");
        req.RecipientUserId.Should().Be(managerUserId);
        Leave(req).GetProperty("reason").GetString().Should().Be("Plans changed");
    }

    // ── NotifyLopAssigned → in-app to employee AND email leave_lop to the EMPLOYEE's user, LOP fields in payload ──
    [Fact]
    public async Task NotifyLopAssigned_FiresInApp_AndEmailsEmployee_WithLeaveLopEvent()
    {
        SeedTenant();
        var employeeUserId = Guid.NewGuid();
        var employeeId = SeedEmployee(employeeUserId, "EMP-0001", "Ada", "ada@acme.com");
        var captured = CaptureEmails();

        await Service().NotifyLopAssignedAsync(employeeId, "AttendanceSync", 2.5m, "Unapproved absence");

        await _notifications.Received(1).CreateAndDispatchAsync(
            _tenantId, employeeUserId, "leave.lop_assigned",
            Arg.Any<string>(), Arg.Any<string>(),
            "Employee", employeeId.ToString(), Arg.Any<CancellationToken>());

        await _dispatcher.Received(1).SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
        var req = captured.Should().ContainSingle().Subject;
        req.EventKey.Should().Be("leave_lop");
        req.RecipientUserId.Should().Be(employeeUserId);
        Leave(req).GetProperty("days").GetDecimal().Should().Be(2.5m);
        Leave(req).GetProperty("source").GetString().Should().Be("AttendanceSync");
        Leave(req).GetProperty("reason").GetString().Should().Be("Unapproved absence");
    }

    // ── Never-throw: a throwing email dispatcher must NOT break the leave flow, and the in-app leg still happened ──
    [Fact]
    public async Task WhenEmailDispatcherThrows_LeaveMethodStillCompletes_AndInAppLegStillFired()
    {
        SeedTenant();
        var leaveTypeId = SeedLeaveType("Annual Leave");
        var employeeUserId = Guid.NewGuid();
        var employeeId = SeedEmployee(employeeUserId, "EMP-0001", "Ada", "ada@acme.com");
        var approverId = SeedEmployee(Guid.NewGuid(), "EMP-MGR", "Grace", "grace@acme.com");
        var leaveRequestId = SeedLeaveRequest(employeeId, leaveTypeId, "Family vacation");

        _dispatcher.SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("SMTP down"));

        var act = async () => await Service().NotifyLeaveApprovedAsync(leaveRequestId, employeeId, approverId);

        await act.Should().NotThrowAsync();

        // The in-app leg is independent and must have completed despite the email failure.
        await _notifications.Received(1).CreateAndDispatchAsync(
            _tenantId, employeeUserId, "leave.approved",
            Arg.Any<string>(), Arg.Any<string>(),
            "LeaveRequest", leaveRequestId.ToString(), Arg.Any<CancellationToken>());
        // The email leg was attempted (and swallowed).
        await _dispatcher.Received(1).SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
    }
}
