// ============================================================================
// ISSUE-188 regression: the leave lifecycle must produce REAL notifications
// (persist + SignalR push via INotificationService), not just log lines. The
// producer resolves the recipient employee to their linked user and dispatches;
// employees with no linked user are skipped.
// ============================================================================

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

public sealed class LeaveNotificationServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();

    public LeaveNotificationServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private async Task<Guid> SeedEmployeeAsync(Guid? userId)
    {
        var employeeId = Guid.NewGuid();
        using var db = Db();
        db.Employees.Add(new Employee
        {
            Id = employeeId,
            TenantId = _tenantId,
            UserId = userId,
            EmployeeNo = "EMP-0001",
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada@acme.com",
            DateOfJoining = DateTime.UtcNow.AddYears(-1),
            EmploymentType = EmploymentType.FullTime,
            IsActive = true,
        });
        await db.SaveChangesAsync();
        return employeeId;
    }

    private LeaveNotificationService Service() =>
        new(_notifications, Db(), _tenantContext, NullLogger<LeaveNotificationService>.Instance);

    [Fact]
    public async Task NotifyLeaveApproved_DispatchesToEmployeesUser()
    {
        var userId = Guid.NewGuid();
        var employeeId = await SeedEmployeeAsync(userId);
        var leaveRequestId = Guid.NewGuid();

        await Service().NotifyLeaveApprovedAsync(leaveRequestId, employeeId, Guid.NewGuid());

        await _notifications.Received(1).CreateAndDispatchAsync(
            _tenantId, userId, "leave.approved",
            Arg.Any<string>(), Arg.Any<string>(),
            "LeaveRequest", leaveRequestId.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyLeaveRejected_DispatchesToEmployeesUser()
    {
        var userId = Guid.NewGuid();
        var employeeId = await SeedEmployeeAsync(userId);

        await Service().NotifyLeaveRejectedAsync(Guid.NewGuid(), employeeId, Guid.NewGuid(), "insufficient balance");

        await _notifications.Received(1).CreateAndDispatchAsync(
            _tenantId, userId, "leave.rejected",
            Arg.Any<string>(), Arg.Is<string>(m => m.Contains("insufficient balance")),
            "LeaveRequest", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyLeaveRequested_DispatchesToManagersUser()
    {
        var managerUserId = Guid.NewGuid();
        var managerEmployeeId = await SeedEmployeeAsync(managerUserId);

        await Service().NotifyLeaveRequestedAsync(Guid.NewGuid(), Guid.NewGuid(), managerEmployeeId);

        await _notifications.Received(1).CreateAndDispatchAsync(
            _tenantId, managerUserId, "leave.requested",
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyLeaveApproved_EmployeeWithoutLinkedUser_DoesNotDispatch()
    {
        var employeeId = await SeedEmployeeAsync(userId: null);

        await Service().NotifyLeaveApprovedAsync(Guid.NewGuid(), employeeId, Guid.NewGuid());

        await _notifications.DidNotReceiveWithAnyArgs().CreateAndDispatchAsync(
            default, default, default!, default!, default!, default, default, default);
    }
}
