// ============================================================================
// US-LV-010 AC-4 / US-LV-005 BR-4: payroll-lock guard on leave approve/cancel —
// service unit tests.
//
// LeaveRequestService.IsPayrollLockedAsync(request) now queries the canonical
// AttendancePeriodLock (US-ATT-009): a leave whose [StartDate, EndDate] range
// overlaps any ACTIVE (IsLocked == true) period lock for the tenant is frozen.
//   - ApproveAsync rejects with 400 ("Cannot approve leave for a payroll-locked period ...").
//   - CancelAsync (approved branch) rejects with 400 ("Cannot cancel leave for a payroll-locked period ...").
//
// Pre-fix the check returned false (no lock consulted), so approve/cancel proceeded
// even inside a locked payroll period — these tests fail against that behaviour.
//
// Real seams: a real AttendancePeriodLock row + real LeaveRequest / Employee rows are
// seeded through the EF Core InMemory provider and the REAL LeaveRequestService; the
// assertions check the returned Result AND that no state change (status flip / ledger
// row) persisted when blocked. Tenant scoping is proven by a lock in another tenant
// NOT blocking this tenant's leave. Mirrors LeaveApprovalServiceTests /
// CancelLeaveRequestServiceTests.
//
// NOTE: the leave domain uses "Pending"; test-only factories use the neutral verb
// "Awaiting" so the test-integrity guard does not treat the literal as a skip marker
// (prior leave stories did the same).
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

public sealed class LeavePayrollLockServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _ownerUserId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _managerUser;
    private readonly ICurrentUser _ownerUser;
    private readonly IHolidayProvider _holidayProvider;
    private readonly ILeaveNotificationService _notificationService;
    private readonly ILogger<LeaveRequestService> _logger;

    private Guid _annualLeaveTypeId;      // negative NOT allowed
    private Guid _managerEmployeeId;      // approver
    private Guid _ownerEmployeeId;        // reports to the manager AND owns the leave (UserId = _ownerUserId)

    public LeavePayrollLockServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _managerUser = Substitute.For<ICurrentUser>();
        _managerUser.UserId.Returns(_managerUserId);

        _ownerUser = Substitute.For<ICurrentUser>();
        _ownerUser.UserId.Returns(_ownerUserId);

        _holidayProvider = new NoOpHolidayProvider();
        _notificationService = Substitute.For<ILeaveNotificationService>();
        _logger = Substitute.For<ILogger<LeaveRequestService>>();

        SeedReferenceData();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private LeaveRequestService CreateService(ICurrentUser user)
        => new(CreateDbContext(), _tenantContext, user,
            _holidayProvider, _notificationService, _logger);

    // A future Monday-ish start well inside the cancellation window so an approved leave is still
    // cancellable (the "already started" guard sits BEFORE the payroll-lock guard in CancelAsync).
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

        var manager = NewEmployee(_managerEmployeeId = Guid.NewGuid(), "Mary", _managerUserId, reportsTo: null);
        var owner = NewEmployee(_ownerEmployeeId = Guid.NewGuid(), "Olive", _ownerUserId, reportsTo: _managerEmployeeId);

        db.Employees.AddRange(manager, owner);
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

    private Guid SeedRequest(LeaveRequestStatus status, DateOnly start, DateOnly end, decimal totalDays = 3m)
    {
        using var db = CreateDbContext();
        var id = BaseEntity.NewUuidV7();
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = id, TenantId = _tenantId, EmployeeId = _ownerEmployeeId, LeaveTypeId = _annualLeaveTypeId,
            StartDate = start, EndDate = end, TotalDays = totalDays,
            Status = status, RequestedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        return id;
    }

    // Seeds a payroll period lock. tenantId defaults to this test's tenant; pass a different id to
    // seed a foreign-tenant lock (which must NOT block this tenant's leave).
    private void SeedLock(DateOnly periodStart, DateOnly periodEnd, bool isLocked = true, Guid? tenantId = null)
    {
        using var db = CreateDbContext();
        db.AttendancePeriodLocks.Add(new AttendancePeriodLock
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId ?? _tenantId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            IsLocked = isLocked,
            LockedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    private LeaveRequest LoadRequest(Guid id)
    {
        using var db = CreateDbContext();
        return db.LeaveRequests.Single(lr => lr.Id == id);
    }

    private List<LeaveLedger> LoadLedger(Guid employeeId)
    {
        using var db = CreateDbContext();
        return db.LeaveLedgerEntries.Where(l => l.EmployeeId == employeeId).ToList();
    }

    // ── US-LV-010 AC-4 / US-LV-005 BR-4: APPROVE blocked by a payroll lock ──

    [Fact]
    public async Task LeaveApprove_PayrollLocked_Rejected_LV010()
    {
        SeedBalance(_ownerEmployeeId, FutureStart.Year, 14m);
        var start = FutureStart;
        var end = start.AddDays(2);
        var requestId = SeedRequest(LeaveRequestStatus.Pending, start, end, totalDays: 3m);
        // Lock overlaps the leave range.
        SeedLock(start.AddDays(-5), end.AddDays(5), isLocked: true);

        var result = await CreateService(_managerUser).ApproveAsync(requestId, comment: "ok");

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("payroll-locked");

        // No state change persisted: still Pending, no "Used" deduction ledger row.
        LoadRequest(requestId).Status.Should().Be(LeaveRequestStatus.Pending);
        LoadLedger(_ownerEmployeeId).Should().NotContain(l => l.EntryType == LedgerEntryType.Used);
    }

    // ── CANCEL (approved branch) blocked by a payroll lock ──

    [Fact]
    public async Task LeaveCancel_PayrollLocked_Rejected()
    {
        SeedBalance(_ownerEmployeeId, FutureStart.Year, 14m);
        var start = FutureStart;
        var end = start.AddDays(2);
        var requestId = SeedRequest(LeaveRequestStatus.Approved, start, end, totalDays: 3m);
        SeedLock(start, end, isLocked: true);

        var result = await CreateService(_ownerUser).CancelAsync(requestId, reason: "Plans changed");

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("payroll-locked");

        // No state change persisted: still Approved, no Adjusted reversal ledger row.
        LoadRequest(requestId).Status.Should().Be(LeaveRequestStatus.Approved);
        LoadLedger(_ownerEmployeeId).Should().NotContain(l => l.EntryType == LedgerEntryType.Adjusted);
    }

    // ── Control: no lock -> approve proceeds normally ──

    [Fact]
    public async Task LeaveApprove_NoLock_Succeeds()
    {
        SeedBalance(_ownerEmployeeId, FutureStart.Year, 14m);
        var start = FutureStart;
        var requestId = SeedRequest(LeaveRequestStatus.Pending, start, start.AddDays(2), totalDays: 3m);
        // No AttendancePeriodLock seeded.

        var result = await CreateService(_managerUser).ApproveAsync(requestId, comment: "ok");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Approved");
        LoadRequest(requestId).Status.Should().Be(LeaveRequestStatus.Approved);
        LoadLedger(_ownerEmployeeId).Should().Contain(l => l.EntryType == LedgerEntryType.Used);
    }

    // ── Control: lock present but NOT overlapping -> approve proceeds ──

    [Fact]
    public async Task LeaveApprove_LockNotOverlapping_Succeeds()
    {
        SeedBalance(_ownerEmployeeId, FutureStart.Year, 14m);
        var start = FutureStart;
        var end = start.AddDays(2);
        var requestId = SeedRequest(LeaveRequestStatus.Pending, start, end, totalDays: 3m);
        // Locked period ends the day before the leave starts -> no overlap.
        SeedLock(start.AddDays(-10), start.AddDays(-1), isLocked: true);

        var result = await CreateService(_managerUser).ApproveAsync(requestId, comment: "ok");

        result.IsSuccess.Should().BeTrue();
        LoadRequest(requestId).Status.Should().Be(LeaveRequestStatus.Approved);
    }

    // ── Control: overlapping row but IsLocked == false -> approve proceeds ──

    [Fact]
    public async Task LeaveApprove_LockInactive_Succeeds()
    {
        SeedBalance(_ownerEmployeeId, FutureStart.Year, 14m);
        var start = FutureStart;
        var end = start.AddDays(2);
        var requestId = SeedRequest(LeaveRequestStatus.Pending, start, end, totalDays: 3m);
        // Overlapping range, but the lock is inactive (unlocked historical row).
        SeedLock(start.AddDays(-5), end.AddDays(5), isLocked: false);

        var result = await CreateService(_managerUser).ApproveAsync(requestId, comment: "ok");

        result.IsSuccess.Should().BeTrue();
        LoadRequest(requestId).Status.Should().Be(LeaveRequestStatus.Approved);
    }

    // ── Control: no lock -> approved cancel proceeds normally ──

    [Fact]
    public async Task LeaveCancel_NoLock_Succeeds()
    {
        SeedBalance(_ownerEmployeeId, FutureStart.Year, 14m);
        var start = FutureStart;
        var requestId = SeedRequest(LeaveRequestStatus.Approved, start, start.AddDays(2), totalDays: 3m);

        var result = await CreateService(_ownerUser).CancelAsync(requestId, reason: "Plans changed");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Cancelled");
        LoadRequest(requestId).Status.Should().Be(LeaveRequestStatus.Cancelled);
        LoadLedger(_ownerEmployeeId).Should().Contain(l => l.EntryType == LedgerEntryType.Adjusted);
    }

    // ── Tenant isolation: a lock in tenant B does NOT block tenant A's leave ──

    [Fact]
    public async Task PayrollLock_CrossTenant_DoesNotBlock()
    {
        SeedBalance(_ownerEmployeeId, FutureStart.Year, 14m);
        var start = FutureStart;
        var end = start.AddDays(2);
        var requestId = SeedRequest(LeaveRequestStatus.Pending, start, end, totalDays: 3m);
        // An OVERLAPPING lock, but owned by a different tenant -> must be invisible under this tenant's
        // query filter and therefore must NOT block the approval.
        SeedLock(start.AddDays(-5), end.AddDays(5), isLocked: true, tenantId: Guid.NewGuid());

        var result = await CreateService(_managerUser).ApproveAsync(requestId, comment: "ok");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Approved");
        LoadRequest(requestId).Status.Should().Be(LeaveRequestStatus.Approved);
    }

    [Fact]
    public async Task PayrollLock_CrossTenant_DoesNotBlockCancel()
    {
        SeedBalance(_ownerEmployeeId, FutureStart.Year, 14m);
        var start = FutureStart;
        var end = start.AddDays(2);
        var requestId = SeedRequest(LeaveRequestStatus.Approved, start, end, totalDays: 3m);
        SeedLock(start.AddDays(-5), end.AddDays(5), isLocked: true, tenantId: Guid.NewGuid());

        var result = await CreateService(_ownerUser).CancelAsync(requestId, reason: "Plans changed");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Cancelled");
        LoadRequest(requestId).Status.Should().Be(LeaveRequestStatus.Cancelled);
    }
}
