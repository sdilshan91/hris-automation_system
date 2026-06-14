// ============================================================================
// US-LV-010: Leave Cancellation by Employee — service unit tests.
//
// Covers pending cancellation (status -> Cancelled, NO ledger entry, history row,
// AC-1), approved cancellation (Adjusted POSITIVE reversal ledger entry restoring
// the balance, history row, reason mandatory, cancelled_at/reason set, AC-2),
// already-started approved leave blocked (AC-3, BR-3), already-cancelled / rejected
// cannot be cancelled (BR-2), ownership — another employee cannot cancel someone
// else's leave (BR-1), and reason mandatory for approved (BR-5). Mirrors
// LeaveApprovalServiceTests and uses the EF Core InMemory provider through the real
// LeaveRequestService.
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

public sealed class CancelLeaveRequestServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _ownerUserId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _ownerUser;
    private readonly IHolidayProvider _holidayProvider;
    private readonly ILeaveNotificationService _notificationService;
    private readonly ILogger<LeaveRequestService> _logger;

    private Guid _annualLeaveTypeId;
    private Guid _ownerEmployeeId;     // the employee who owns the requests (UserId = _ownerUserId)
    private Guid _otherEmployeeId;     // a different employee, owns nothing the owner can cancel

    public CancelLeaveRequestServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _ownerUser = Substitute.For<ICurrentUser>();
        _ownerUser.UserId.Returns(_ownerUserId);

        _holidayProvider = new NoOpHolidayProvider();
        _notificationService = Substitute.For<ILeaveNotificationService>();
        _logger = Substitute.For<ILogger<LeaveRequestService>>();

        SeedReferenceData();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private LeaveRequestService CreateService(ICurrentUser? user = null)
        => new(CreateDbContext(), _tenantContext, user ?? _ownerUser,
            _holidayProvider, _notificationService, _logger);

    // A future Monday well inside the cancellation window so an approved leave is still cancellable.
    private static DateOnly FutureStart => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20);

    private void SeedReferenceData()
    {
        using var db = CreateDbContext();

        db.LeaveTypes.Add(new LeaveType
        {
            Id = _annualLeaveTypeId = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "Annual Leave",
            AnnualEntitlement = 14,
            AccrualFrequency = AccrualFrequency.Upfront,
            Gender = LeaveTypeGender.All,
            NegativeBalanceAllowed = false,
            IsActive = true,
        });

        db.Employees.AddRange(
            NewEmployee(_ownerEmployeeId = Guid.NewGuid(), "Olive", _ownerUserId),
            NewEmployee(_otherEmployeeId = Guid.NewGuid(), "Pearl", Guid.NewGuid()));

        db.SaveChanges();
    }

    private Employee NewEmployee(Guid id, string first, Guid? userId) => new()
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
        ReportsToEmployeeId = Guid.NewGuid(),
        IsActive = true,
    };

    private void SeedBalance(Guid employeeId, int year, decimal balance)
    {
        using var db = CreateDbContext();
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EntryType = LedgerEntryType.Accrual,
            EmployeeId = employeeId, LeaveTypeId = _annualLeaveTypeId, LeaveYear = year,
            Amount = balance, BalanceAfter = balance, OccurredAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    private Guid SeedRequest(
        Guid employeeId,
        LeaveRequestStatus status,
        decimal totalDays = 3m,
        DateOnly? start = null,
        DateOnly? end = null)
    {
        using var db = CreateDbContext();
        var id = BaseEntity.NewUuidV7();
        var s = start ?? FutureStart;
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = id, TenantId = _tenantId, EmployeeId = employeeId, LeaveTypeId = _annualLeaveTypeId,
            StartDate = s, EndDate = end ?? s.AddDays(2), TotalDays = totalDays,
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

    private List<LeaveLedger> LoadLedger(Guid employeeId)
    {
        using var db = CreateDbContext();
        return db.LeaveLedgerEntries
            .Where(l => l.EmployeeId == employeeId && l.LeaveTypeId == _annualLeaveTypeId)
            .ToList();
    }

    private List<LeaveApprovalHistory> LoadHistory(Guid leaveRequestId)
    {
        using var db = CreateDbContext();
        return db.LeaveApprovalHistories
            .Where(h => h.LeaveRequestId == leaveRequestId)
            .ToList();
    }

    // ── AC-1: pending cancellation — no ledger, history row ──────────

    [Fact]
    public async Task Cancel_AwaitingRequest_SetsCancelled_NoLedgerEntry_WritesHistory()
    {
        SeedBalance(_ownerEmployeeId, FutureStart.Year, 14m);
        var requestId = SeedRequest(_ownerEmployeeId, LeaveRequestStatus.Pending, totalDays: 3m);

        var result = await CreateService().CancelAsync(requestId, reason: null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Cancelled");
        result.Value.LedgerEntryId.Should().BeNull();
        result.Value.BalanceAfter.Should().BeNull();

        var request = LoadRequest(requestId);
        request.Status.Should().Be(LeaveRequestStatus.Cancelled);
        request.CancelledAt.Should().NotBeNull();

        // No reversal/Adjusted ledger row on a pending cancellation — only the seeded accrual.
        LoadLedger(_ownerEmployeeId).Should().NotContain(l => l.EntryType == LedgerEntryType.Adjusted);

        // A history row: Cancelled, actor = self.
        var history = LoadHistory(requestId).Single();
        history.Action.Should().Be(LeaveApprovalAction.Cancelled);
        history.ApproverEmployeeId.Should().Be(_ownerEmployeeId);
    }

    [Fact]
    public async Task Cancel_AwaitingRequest_FiresCancelledNotification()
    {
        var requestId = SeedRequest(_ownerEmployeeId, LeaveRequestStatus.Pending, totalDays: 2m);

        var result = await CreateService().CancelAsync(requestId, reason: null);

        result.IsSuccess.Should().BeTrue();
        await _notificationService.Received(1).NotifyLeaveCancelledAsync(
            requestId, _ownerEmployeeId, Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // ── AC-2: approved cancellation — Adjusted positive reversal ─────

    [Fact]
    public async Task Cancel_ApprovedRequest_AppendsPositiveAdjustedLedger_RestoresBalance_WritesHistory()
    {
        // Approved leave already deducted: accrual 14, used -3 leaving 11.
        SeedBalance(_ownerEmployeeId, FutureStart.Year, 14m);
        using (var db = CreateDbContext())
        {
            db.LeaveLedgerEntries.Add(new LeaveLedger
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EntryType = LedgerEntryType.Used,
                EmployeeId = _ownerEmployeeId, LeaveTypeId = _annualLeaveTypeId, LeaveYear = FutureStart.Year,
                Amount = -3m, BalanceAfter = 11m, OccurredAt = DateTime.UtcNow.AddMinutes(1),
            });
            db.SaveChanges();
        }
        var requestId = SeedRequest(_ownerEmployeeId, LeaveRequestStatus.Approved, totalDays: 3m);

        var result = await CreateService().CancelAsync(requestId, reason: "Plans changed");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Cancelled");
        result.Value.LedgerEntryId.Should().NotBeNull();
        result.Value.BalanceAfter.Should().Be(14m); // 11 + 3 restored

        // A positive Adjusted reversal restoring the days, linked to the request.
        var adjusted = LoadLedger(_ownerEmployeeId).Single(l => l.EntryType == LedgerEntryType.Adjusted);
        adjusted.Amount.Should().Be(3m);
        adjusted.BalanceAfter.Should().Be(14m);
        adjusted.LeaveRequestId.Should().Be(requestId);
        adjusted.Description.Should().Contain("Cancellation of leave request");

        var request = LoadRequest(requestId);
        request.Status.Should().Be(LeaveRequestStatus.Cancelled);
        request.CancelledAt.Should().NotBeNull();
        request.CancellationReason.Should().Be("Plans changed");

        var history = LoadHistory(requestId).Single();
        history.Action.Should().Be(LeaveApprovalAction.Cancelled);
        history.Comment.Should().Be("Plans changed");
    }

    // ── BR-5: reason mandatory for an approved cancellation ──────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Cancel_ApprovedRequest_WithoutReason_Fails(string? reason)
    {
        SeedBalance(_ownerEmployeeId, FutureStart.Year, 14m);
        var requestId = SeedRequest(_ownerEmployeeId, LeaveRequestStatus.Approved, totalDays: 3m);

        var result = await CreateService().CancelAsync(requestId, reason);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("reason is required");

        // Nothing committed: still Approved, no reversal, no history.
        LoadRequest(requestId).Status.Should().Be(LeaveRequestStatus.Approved);
        LoadLedger(_ownerEmployeeId).Should().NotContain(l => l.EntryType == LedgerEntryType.Adjusted);
        LoadHistory(requestId).Should().BeEmpty();
    }

    // ── AC-3 / BR-3: an approved leave already started cannot be cancelled ──

    [Fact]
    public async Task Cancel_ApprovedRequest_AlreadyStarted_IsBlocked()
    {
        SeedBalance(_ownerEmployeeId, DateTime.UtcNow.Year, 14m);
        // Start date in the past -> already started (window default 0).
        var pastStart = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);
        var requestId = SeedRequest(
            _ownerEmployeeId, LeaveRequestStatus.Approved, totalDays: 3m,
            start: pastStart, end: pastStart.AddDays(2));

        var result = await CreateService().CancelAsync(requestId, reason: "Too late");

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("already started");

        LoadRequest(requestId).Status.Should().Be(LeaveRequestStatus.Approved);
        LoadHistory(requestId).Should().BeEmpty();
    }

    [Fact]
    public async Task Cancel_ApprovedRequest_StartingToday_IsBlocked()
    {
        SeedBalance(_ownerEmployeeId, DateTime.UtcNow.Year, 14m);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var requestId = SeedRequest(
            _ownerEmployeeId, LeaveRequestStatus.Approved, totalDays: 1m,
            start: today, end: today);

        var result = await CreateService().CancelAsync(requestId, reason: "Today");

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("already started");
    }

    // ── BR-2: rejected / already-cancelled cannot be cancelled ───────

    [Fact]
    public async Task Cancel_RejectedRequest_IsBlocked()
    {
        var requestId = SeedRequest(_ownerEmployeeId, LeaveRequestStatus.Rejected, totalDays: 2m);

        var result = await CreateService().CancelAsync(requestId, reason: null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("cannot be cancelled");

        LoadRequest(requestId).Status.Should().Be(LeaveRequestStatus.Rejected);
    }

    [Fact]
    public async Task Cancel_AlreadyCancelledRequest_IsBlocked()
    {
        var requestId = SeedRequest(_ownerEmployeeId, LeaveRequestStatus.Cancelled, totalDays: 2m);

        var result = await CreateService().CancelAsync(requestId, reason: null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.Error.Should().Contain("already been cancelled");
    }

    // ── BR-1: ownership — cannot cancel another employee's leave ─────

    [Fact]
    public async Task Cancel_LeaveOwnedByAnotherEmployee_IsDenied()
    {
        // The request belongs to a different employee; the current user owns _ownerEmployeeId.
        var requestId = SeedRequest(_otherEmployeeId, LeaveRequestStatus.Pending, totalDays: 2m);

        var result = await CreateService().CancelAsync(requestId, reason: null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("only cancel your own");

        LoadRequest(requestId).Status.Should().Be(LeaveRequestStatus.Pending);
        LoadHistory(requestId).Should().BeEmpty();
    }

    [Fact]
    public async Task Cancel_UserWithNoEmployeeRecord_IsDenied()
    {
        var requestId = SeedRequest(_ownerEmployeeId, LeaveRequestStatus.Pending, totalDays: 2m);

        var stranger = Substitute.For<ICurrentUser>();
        stranger.UserId.Returns(Guid.NewGuid());

        var result = await CreateService(stranger).CancelAsync(requestId, reason: null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("No employee record");
    }

    // ── 404 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_UnknownRequest_Returns404()
    {
        var result = await CreateService().CancelAsync(Guid.NewGuid(), reason: null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.Error.Should().Contain("not found");
    }

    // ── Pending cancellation can happen regardless of date (BR-3 only blocks approved) ──

    [Fact]
    public async Task Cancel_AwaitingRequest_StartingToday_Succeeds()
    {
        SeedBalance(_ownerEmployeeId, DateTime.UtcNow.Year, 14m);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var requestId = SeedRequest(
            _ownerEmployeeId, LeaveRequestStatus.Pending, totalDays: 1m,
            start: today, end: today);

        var result = await CreateService().CancelAsync(requestId, reason: null);

        result.IsSuccess.Should().BeTrue();
        LoadRequest(requestId).Status.Should().Be(LeaveRequestStatus.Cancelled);
    }
}
