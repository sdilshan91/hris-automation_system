// ============================================================================
// US-LV-005: Manager Approves/Rejects Leave — service unit tests.
//
// Covers approve happy path (status -> Approved, LeaveLedger "Used" deduction
// with balance_after from the running total, LeaveApprovalHistory row at level 1),
// reject happy path (status -> Rejected, NO ledger entry, history row carrying the
// reason), mandatory rejection reason (BR-2), insufficient-balance-at-approval
// (AC-3, BR-5: blocked when negative not allowed, permitted when allowed),
// re-actioning an already-actioned request (BR-3), and approver-scope authorization
// (BR-1). Mirrors LeaveRequestServiceTests / PendingLeaveQueueServiceTests and uses
// the EF Core InMemory provider through the real LeaveRequestService.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class LeaveApprovalServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _managerUser;
    private readonly IHolidayProvider _holidayProvider;
    private readonly ILeaveNotificationService _notificationService;
    private readonly ILogger<LeaveRequestService> _logger;

    private Guid _annualLeaveTypeId;     // negative NOT allowed
    private Guid _unpaidLeaveTypeId;     // negative allowed
    private Guid _managerEmployeeId;
    private Guid _reportId;              // direct report of the manager
    private Guid _outsiderId;           // reports to a different manager

    public LeaveApprovalServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _managerUser = Substitute.For<ICurrentUser>();
        _managerUser.UserId.Returns(_managerUserId);

        _holidayProvider = new NoOpHolidayProvider();
        _notificationService = Substitute.For<ILeaveNotificationService>();
        _logger = Substitute.For<ILogger<LeaveRequestService>>();

        SeedReferenceData();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private LeaveRequestService CreateService(ICurrentUser? user = null)
        => new(CreateDbContext(), _tenantContext, user ?? _managerUser,
            _holidayProvider, _notificationService, _logger);

    private static readonly DateOnly Base = new(2026, 6, 1);

    private void SeedReferenceData()
    {
        using var db = CreateDbContext();

        db.LeaveTypes.AddRange(
            new LeaveType
            {
                Id = _annualLeaveTypeId = Guid.NewGuid(), TenantId = _tenantId,
                Name = "Annual Leave", AnnualEntitlement = 14,
                AccrualFrequency = AccrualFrequency.Upfront, Gender = LeaveTypeGender.All,
                NegativeBalanceAllowed = false, IsActive = true,
            },
            new LeaveType
            {
                Id = _unpaidLeaveTypeId = Guid.NewGuid(), TenantId = _tenantId,
                Name = "Unpaid Leave", AnnualEntitlement = 0,
                AccrualFrequency = AccrualFrequency.Upfront, Gender = LeaveTypeGender.All,
                NegativeBalanceAllowed = true, IsActive = true,
            });

        var manager = NewEmployee(_managerEmployeeId = Guid.NewGuid(), "Mary", _managerUserId, reportsTo: null);
        var report = NewEmployee(_reportId = Guid.NewGuid(), "Rita", null, reportsTo: _managerEmployeeId);
        var outsider = NewEmployee(_outsiderId = Guid.NewGuid(), "Otto", null, reportsTo: Guid.NewGuid());

        db.Employees.AddRange(manager, report, outsider);
        db.SaveChanges();
    }

    private Employee NewEmployee(Guid id, string first, Guid? userId, Guid? reportsTo) => new()
    {
        Id = id,
        TenantId = _tenantId,
        UserId = userId,
        EmployeeNo = $"EMP-{id.ToString()[..4]}",
        FirstName = first,
        LastName = "Test",
        Email = $"{first}@test.com".ToLowerInvariant(),
        DateOfJoining = new DateTime(2020, 1, 1),
        DepartmentId = Guid.NewGuid(),
        JobTitleId = Guid.NewGuid(),
        EmploymentType = EmploymentType.FullTime,
        Status = EmployeeStatus.Active,
        ReportsToEmployeeId = reportsTo,
        IsActive = true,
    };

    private void SeedBalance(Guid employeeId, Guid leaveTypeId, int year, decimal balance)
    {
        using var db = CreateDbContext();
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EntryType = LedgerEntryType.Accrual,
            EmployeeId = employeeId, LeaveTypeId = leaveTypeId, LeaveYear = year,
            Amount = balance, BalanceAfter = balance, OccurredAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    private Guid SeedRequest(
        Guid employeeId, Guid leaveTypeId, decimal totalDays = 3m,
        LeaveRequestStatus status = LeaveRequestStatus.Pending,
        DateOnly? start = null, DateOnly? end = null)
    {
        using var db = CreateDbContext();
        var id = BaseEntity.NewUuidV7();
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = id, TenantId = _tenantId, EmployeeId = employeeId, LeaveTypeId = leaveTypeId,
            StartDate = start ?? Base, EndDate = end ?? Base.AddDays(2), TotalDays = totalDays,
            Status = status, RequestedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        return id;
    }

    private LeaveRequest LoadRequest(Guid id)
    {
        using var db = CreateDbContext();
        return db.LeaveRequests.Single(lr => lr.Id == id);
    }

    private List<LeaveLedger> LoadLedger(Guid employeeId, Guid leaveTypeId)
    {
        using var db = CreateDbContext();
        return db.LeaveLedgerEntries
            .Where(l => l.EmployeeId == employeeId && l.LeaveTypeId == leaveTypeId)
            .ToList();
    }

    private List<LeaveApprovalHistory> LoadHistory(Guid leaveRequestId)
    {
        using var db = CreateDbContext();
        return db.LeaveApprovalHistories
            .Where(h => h.LeaveRequestId == leaveRequestId)
            .ToList();
    }

    // ── AC-1 / FR-3 / BR-5: approve happy path ──────────────────────

    [Fact]
    public async Task Approve_HappyPath_SetsApproved_AppendsUsedLedger_WritesHistory()
    {
        SeedBalance(_reportId, _annualLeaveTypeId, Base.Year, 10m);
        var requestId = SeedRequest(_reportId, _annualLeaveTypeId, totalDays: 3m);

        var result = await CreateService().ApproveAsync(requestId, comment: "Enjoy");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Approved");
        result.Value.Action.Should().Be("Approved");
        result.Value.ApprovalLevel.Should().Be(1);
        result.Value.LedgerEntryId.Should().NotBeNull();
        result.Value.BalanceAfter.Should().Be(7m); // 10 - 3

        // Status persisted.
        LoadRequest(requestId).Status.Should().Be(LeaveRequestStatus.Approved);

        // A "Used" ledger row deducting the total days, with balance_after from the running total.
        var ledger = LoadLedger(_reportId, _annualLeaveTypeId);
        var used = ledger.Single(l => l.EntryType == LedgerEntryType.Used);
        used.Amount.Should().Be(-3m);
        used.BalanceAfter.Should().Be(7m);
        used.LeaveRequestId.Should().Be(requestId);

        // A history row: Approved, level 1, carries the comment.
        var history = LoadHistory(requestId).Single();
        history.Action.Should().Be(LeaveApprovalAction.Approved);
        history.ApprovalLevel.Should().Be(1);
        history.ApproverEmployeeId.Should().Be(_managerEmployeeId);
        history.Comment.Should().Be("Enjoy");
    }

    [Fact]
    public async Task Approve_FiresLeaveApprovedNotification()
    {
        SeedBalance(_reportId, _annualLeaveTypeId, Base.Year, 10m);
        var requestId = SeedRequest(_reportId, _annualLeaveTypeId, totalDays: 2m);

        var result = await CreateService().ApproveAsync(requestId, comment: null);

        result.IsSuccess.Should().BeTrue();
        await _notificationService.Received(1).NotifyLeaveApprovedAsync(
            requestId, _reportId, _managerEmployeeId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approve_NullComment_WritesHistoryWithNoComment()
    {
        SeedBalance(_reportId, _annualLeaveTypeId, Base.Year, 10m);
        var requestId = SeedRequest(_reportId, _annualLeaveTypeId, totalDays: 1m);

        var result = await CreateService().ApproveAsync(requestId, comment: null);

        result.IsSuccess.Should().BeTrue();
        LoadHistory(requestId).Single().Comment.Should().BeNull();
    }

    // ── AC-2 / FR-4: reject happy path ──────────────────────────────

    [Fact]
    public async Task Reject_HappyPath_SetsRejected_NoLedgerEntry_WritesHistoryWithReason()
    {
        SeedBalance(_reportId, _annualLeaveTypeId, Base.Year, 10m);
        var requestId = SeedRequest(_reportId, _annualLeaveTypeId, totalDays: 3m);

        var result = await CreateService().RejectAsync(requestId, reason: "Team capacity too low");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Rejected");
        result.Value.Action.Should().Be("Rejected");
        result.Value.LedgerEntryId.Should().BeNull();
        result.Value.BalanceAfter.Should().BeNull();

        LoadRequest(requestId).Status.Should().Be(LeaveRequestStatus.Rejected);

        // No "Used" ledger entry on rejection — only the seeded accrual remains.
        LoadLedger(_reportId, _annualLeaveTypeId)
            .Should().NotContain(l => l.EntryType == LedgerEntryType.Used);

        var history = LoadHistory(requestId).Single();
        history.Action.Should().Be(LeaveApprovalAction.Rejected);
        history.ApprovalLevel.Should().Be(1);
        history.Comment.Should().Be("Team capacity too low");
    }

    [Fact]
    public async Task Reject_FiresLeaveRejectedNotificationWithReason()
    {
        var requestId = SeedRequest(_reportId, _annualLeaveTypeId, totalDays: 2m);

        var result = await CreateService().RejectAsync(requestId, reason: "Blackout period");

        result.IsSuccess.Should().BeTrue();
        await _notificationService.Received(1).NotifyLeaveRejectedAsync(
            requestId, _reportId, _managerEmployeeId, "Blackout period", Arg.Any<CancellationToken>());
    }

    // ── BR-2: rejection reason is mandatory ─────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reject_EmptyOrWhitespaceReason_Fails(string reason)
    {
        var requestId = SeedRequest(_reportId, _annualLeaveTypeId, totalDays: 1m);

        var result = await CreateService().RejectAsync(requestId, reason);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("rejection reason is required");

        // Request stays Pending; no history written.
        LoadRequest(requestId).Status.Should().Be(LeaveRequestStatus.Pending);
        LoadHistory(requestId).Should().BeEmpty();
    }

    // ── AC-3 / BR-5: insufficient balance at approval ───────────────

    [Fact]
    public async Task Approve_InsufficientBalance_NegativeNotAllowed_IsBlocked()
    {
        // Balance 2, request 3 -> projected -1; annual leave forbids negative.
        SeedBalance(_reportId, _annualLeaveTypeId, Base.Year, 2m);
        var requestId = SeedRequest(_reportId, _annualLeaveTypeId, totalDays: 3m);

        var result = await CreateService().ApproveAsync(requestId, comment: null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("Insufficient leave balance to approve");

        // Nothing committed: still Pending, no ledger deduction, no history.
        LoadRequest(requestId).Status.Should().Be(LeaveRequestStatus.Pending);
        LoadLedger(_reportId, _annualLeaveTypeId)
            .Should().NotContain(l => l.EntryType == LedgerEntryType.Used);
        LoadHistory(requestId).Should().BeEmpty();
    }

    [Fact]
    public async Task Approve_InsufficientBalance_NegativeAllowed_SucceedsWithNegativeBalanceAfter()
    {
        // Unpaid leave allows negative. Balance 0, request 3 -> approved at -3.
        SeedBalance(_reportId, _unpaidLeaveTypeId, Base.Year, 0m);
        var requestId = SeedRequest(_reportId, _unpaidLeaveTypeId, totalDays: 3m);

        var result = await CreateService().ApproveAsync(requestId, comment: null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.BalanceAfter.Should().Be(-3m);

        var used = LoadLedger(_reportId, _unpaidLeaveTypeId).Single(l => l.EntryType == LedgerEntryType.Used);
        used.BalanceAfter.Should().Be(-3m);
    }

    // ── BR-3: an already-actioned request cannot be re-actioned ─────

    [Fact]
    public async Task Approve_AlreadyApprovedRequest_IsBlocked()
    {
        var requestId = SeedRequest(_reportId, _annualLeaveTypeId, status: LeaveRequestStatus.Approved);

        var result = await CreateService().ApproveAsync(requestId, comment: null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.Error.Should().Contain("already been actioned");
    }

    [Fact]
    public async Task Reject_AlreadyRejectedRequest_IsBlocked()
    {
        var requestId = SeedRequest(_reportId, _annualLeaveTypeId, status: LeaveRequestStatus.Rejected);

        var result = await CreateService().RejectAsync(requestId, reason: "Late");

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.Error.Should().Contain("already been actioned");
    }

    [Fact]
    public async Task Approve_AlreadyRejectedRequest_IsBlocked()
    {
        var requestId = SeedRequest(_reportId, _annualLeaveTypeId, status: LeaveRequestStatus.Rejected);

        var result = await CreateService().ApproveAsync(requestId, comment: null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.Error.Should().Contain("already been actioned");
    }

    // ── BR-1: only the requester's own manager may action ───────────

    [Fact]
    public async Task Approve_ByManagerWhoIsNotTheApprover_IsDenied()
    {
        // The request belongs to "outsider", who reports to a different manager.
        SeedBalance(_outsiderId, _annualLeaveTypeId, Base.Year, 10m);
        var requestId = SeedRequest(_outsiderId, _annualLeaveTypeId, totalDays: 2m);

        var result = await CreateService().ApproveAsync(requestId, comment: null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("not the approver");

        LoadRequest(requestId).Status.Should().Be(LeaveRequestStatus.Pending);
        LoadHistory(requestId).Should().BeEmpty();
    }

    [Fact]
    public async Task Reject_ByManagerWhoIsNotTheApprover_IsDenied()
    {
        var requestId = SeedRequest(_outsiderId, _annualLeaveTypeId, totalDays: 2m);

        var result = await CreateService().RejectAsync(requestId, reason: "Not my call");

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("not the approver");
    }

    [Fact]
    public async Task Approve_UserWithNoEmployeeRecord_IsDenied()
    {
        var requestId = SeedRequest(_reportId, _annualLeaveTypeId, totalDays: 2m);

        var stranger = Substitute.For<ICurrentUser>();
        stranger.UserId.Returns(Guid.NewGuid());

        var result = await CreateService(stranger).ApproveAsync(requestId, comment: null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("No employee record");
    }

    // ── 404 + balance running-total ─────────────────────────────────

    [Fact]
    public async Task Approve_UnknownRequest_Returns404()
    {
        var result = await CreateService().ApproveAsync(Guid.NewGuid(), comment: null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Approve_DeductsFromLatestRunningTotal_NotTheInitialAccrual()
    {
        // Two prior ledger rows: accrual 14, then an earlier used -4 leaving 10. The approval
        // must deduct from the latest running total (10), not the original accrual (14).
        SeedBalance(_reportId, _annualLeaveTypeId, Base.Year, 14m);
        using (var db = CreateDbContext())
        {
            db.LeaveLedgerEntries.Add(new LeaveLedger
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EntryType = LedgerEntryType.Used,
                EmployeeId = _reportId, LeaveTypeId = _annualLeaveTypeId, LeaveYear = Base.Year,
                Amount = -4m, BalanceAfter = 10m, OccurredAt = DateTime.UtcNow.AddMinutes(1),
            });
            db.SaveChanges();
        }

        var requestId = SeedRequest(_reportId, _annualLeaveTypeId, totalDays: 3m);

        var result = await CreateService().ApproveAsync(requestId, comment: null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.BalanceAfter.Should().Be(7m); // 10 - 3, not 14 - 3
    }
}
