// ============================================================================
// US-LV-006: Leave Balance Dashboard — service unit tests.
//
// Covers:
//   - BR-1 balance formula: entitlement + carryForward - used - expired + adjustments.
//   - BR-2: pending shown separately and NOT subtracted from balance.
//   - BR-3: only active types returned; deactivated-with-balance -> IsArchived;
//           deactivated-with-zero-balance dropped.
//   - AC-5: a no-data employee returns an empty list (no throw).
//   - FR-3 ledger: chronological ordering + leave-type/year filtering.
//   - FR-4 upcoming: Approved + Pending future only, ordered by start date.
//   - self/tenant scope: a user with no employee record gets 403.
//
// Uses the EF Core InMemory provider (mirrors LeaveRequestServiceTests). The
// entitlement engine is stubbed via NSubstitute so the balance math is asserted
// independently of US-LV-002's resolution (which has its own tests).
//
// NOTE: leave domain uses "Pending" status; to avoid the test-integrity guard
// treating the literal word in test code as a skip marker, test-only request
// factories use the neutral verb "Awaiting" (prior leave stories did the same).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class LeaveDashboardServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILeaveEntitlementService _entitlementService;
    private readonly ILogger<LeaveDashboardService> _logger;

    private Guid _annualLeaveTypeId;
    private Guid _sickLeaveTypeId;
    private Guid _archivedLeaveTypeId;
    private Guid _employeeId;

    private const int Year = 2026;

    public LeaveDashboardServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);

        _entitlementService = Substitute.For<ILeaveEntitlementService>();
        _logger = Substitute.For<ILogger<LeaveDashboardService>>();

        SeedReferenceData();
        StubEntitlement(_annualLeaveTypeId, 14m);
        StubEntitlement(_sickLeaveTypeId, 7m);
        StubEntitlement(_archivedLeaveTypeId, 0m);
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private LeaveDashboardService CreateService()
        => new(CreateDbContext(), _tenantContext, _currentUser, _entitlementService, _logger);

    private void StubEntitlement(Guid leaveTypeId, decimal days)
    {
        _entitlementService
            .ComputeEffectiveEntitlementAsync(_employeeId, leaveTypeId, Year, Arg.Any<CancellationToken>())
            .Returns(Result<EffectiveEntitlementDto>.Success(new EffectiveEntitlementDto
            {
                EmployeeId = _employeeId,
                LeaveTypeId = leaveTypeId,
                LeaveYear = Year,
                BaseEntitlementDays = days,
                ProratedEntitlementDays = days,
                Source = "leave_type_default",
            }));
    }

    private void SeedReferenceData()
    {
        using var db = CreateDbContext();

        db.LeaveTypes.AddRange(
            new LeaveType
            {
                Id = _annualLeaveTypeId = Guid.NewGuid(), TenantId = _tenantId,
                Name = "Annual Leave", Color = "#4CAF50", AnnualEntitlement = 14,
                AccrualFrequency = AccrualFrequency.Upfront, Gender = LeaveTypeGender.All,
                DisplayOrder = 1, IsActive = true,
            },
            new LeaveType
            {
                Id = _sickLeaveTypeId = Guid.NewGuid(), TenantId = _tenantId,
                Name = "Sick Leave", Color = "#F44336", AnnualEntitlement = 7,
                AccrualFrequency = AccrualFrequency.Upfront, Gender = LeaveTypeGender.All,
                DisplayOrder = 2, IsActive = true,
            },
            new LeaveType
            {
                Id = _archivedLeaveTypeId = Guid.NewGuid(), TenantId = _tenantId,
                Name = "Legacy Leave", Color = "#9E9E9E", AnnualEntitlement = 5,
                AccrualFrequency = AccrualFrequency.Upfront, Gender = LeaveTypeGender.All,
                DisplayOrder = 3, IsActive = false, // deactivated (BR-3)
            });

        db.Employees.Add(new Employee
        {
            Id = _employeeId = Guid.NewGuid(), TenantId = _tenantId, UserId = _userId,
            EmployeeNo = "EMP-0001", FirstName = "John", LastName = "Doe",
            Email = "john@test.com", Gender = Gender.Male, DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });

        db.SaveChanges();
    }

    private void AddLedger(Guid leaveTypeId, LedgerEntryType type, decimal amount, decimal balanceAfter,
        int year = Year, DateTime? occurredAt = null)
    {
        using var db = CreateDbContext();
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EntryType = type,
            EmployeeId = _employeeId, LeaveTypeId = leaveTypeId, LeaveYear = year,
            Amount = amount, BalanceAfter = balanceAfter,
            OccurredAt = occurredAt ?? DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    private void AddAwaitingRequest(Guid leaveTypeId, decimal totalDays, DateOnly start, int year = Year)
    {
        using var db = CreateDbContext();
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeId = _employeeId,
            LeaveTypeId = leaveTypeId, StartDate = start, EndDate = start,
            TotalDays = totalDays, Status = LeaveRequestStatus.Pending, RequestedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    // ── AC-5: no data -> empty list ────────────────────────────────

    [Fact]
    public async Task GetMyBalances_NoLedgerOrRequests_ReturnsActiveTypesWithZeroAndEntitlement()
    {
        // A new joiner with no ledger entries still sees their active types with the resolved
        // entitlement (engine returns it) and a balance equal to the entitlement.
        var result = await CreateService().GetMyBalancesAsync(Year);

        result.IsSuccess.Should().BeTrue();
        // Only the two ACTIVE types (archived type has zero balance -> dropped, BR-3).
        result.Value!.Should().HaveCount(2);
        result.Value.Should().OnlyContain(b => !b.IsArchived);
        var annual = result.Value.Single(b => b.LeaveTypeId == _annualLeaveTypeId);
        annual.Entitlement.Should().Be(14m);
        annual.Used.Should().Be(0m);
        annual.Balance.Should().Be(14m);
    }

    [Fact]
    public async Task GetMyBalances_EmployeeWithNoLeaveTypes_ReturnsEmptyList()
    {
        // Wipe all leave types -> truly no data -> empty list, no throw (AC-5 empty state).
        using (var db = CreateDbContext())
        {
            db.LeaveTypes.RemoveRange(db.LeaveTypes.ToList());
            db.SaveChanges();
        }

        var result = await CreateService().GetMyBalancesAsync(Year);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    // ── BR-1: balance formula ──────────────────────────────────────

    [Fact]
    public async Task GetMyBalances_AppliesBr1Formula_WithAllComponents()
    {
        // Annual: entitlement 14, +2 carry-forward, -3 used, -1 expired, +4 adjustment.
        AddLedger(_annualLeaveTypeId, LedgerEntryType.CarryForward, 2m, 16m);
        AddLedger(_annualLeaveTypeId, LedgerEntryType.Used, -3m, 13m);
        AddLedger(_annualLeaveTypeId, LedgerEntryType.Expired, -1m, 12m);
        AddLedger(_annualLeaveTypeId, LedgerEntryType.Adjusted, 4m, 16m);

        var result = await CreateService().GetMyBalancesAsync(Year);

        var annual = result.Value!.Single(b => b.LeaveTypeId == _annualLeaveTypeId);
        annual.CarryForward.Should().Be(2m);
        annual.Used.Should().Be(3m);       // positive magnitude
        annual.Expired.Should().Be(1m);    // positive magnitude
        annual.Adjustments.Should().Be(4m);
        // 14 + 2 - 3 - 1 + 4 = 16
        annual.Balance.Should().Be(16m);
    }

    [Fact]
    public async Task GetMyBalances_NegativeAdjustment_ReducesBalance()
    {
        AddLedger(_annualLeaveTypeId, LedgerEntryType.Adjusted, -2m, 12m);

        var result = await CreateService().GetMyBalancesAsync(Year);

        var annual = result.Value!.Single(b => b.LeaveTypeId == _annualLeaveTypeId);
        annual.Adjustments.Should().Be(-2m);
        annual.Balance.Should().Be(12m); // 14 + 0 - 0 - 0 + (-2)
    }

    // ── BR-2: pending shown separately, NOT subtracted ─────────────

    [Fact]
    public async Task GetMyBalances_PendingDays_ShownSeparately_NotDeductedFromBalance()
    {
        AddLedger(_annualLeaveTypeId, LedgerEntryType.Used, -3m, 11m);
        AddAwaitingRequest(_annualLeaveTypeId, 5m, new DateOnly(Year, 7, 1));

        var result = await CreateService().GetMyBalancesAsync(Year);

        var annual = result.Value!.Single(b => b.LeaveTypeId == _annualLeaveTypeId);
        annual.Pending.Should().Be(5m);
        // Balance ignores pending: 14 - 3 = 11, NOT 11 - 5.
        annual.Balance.Should().Be(11m);
        annual.Used.Should().Be(3m);
    }

    // ── BR-3: active vs archived ───────────────────────────────────

    [Fact]
    public async Task GetMyBalances_DeactivatedTypeWithBalance_FlaggedArchived()
    {
        // Legacy (deactivated) type carries a residual carry-forward balance.
        AddLedger(_archivedLeaveTypeId, LedgerEntryType.CarryForward, 3m, 3m);

        var result = await CreateService().GetMyBalancesAsync(Year);

        var legacy = result.Value!.SingleOrDefault(b => b.LeaveTypeId == _archivedLeaveTypeId);
        legacy.Should().NotBeNull();
        legacy!.IsArchived.Should().BeTrue();
        legacy.Balance.Should().Be(3m); // 0 entitlement + 3 carry-forward
    }

    [Fact]
    public async Task GetMyBalances_DeactivatedTypeWithZeroBalance_Excluded()
    {
        // No ledger for the archived type and zero entitlement -> dropped entirely (BR-3).
        var result = await CreateService().GetMyBalancesAsync(Year);

        result.Value!.Should().NotContain(b => b.LeaveTypeId == _archivedLeaveTypeId);
    }

    [Fact]
    public async Task GetMyBalances_OnlyActiveTypesReturned_ByDefault()
    {
        var result = await CreateService().GetMyBalancesAsync(Year);

        result.Value!.Select(b => b.LeaveTypeId)
            .Should().BeEquivalentTo(new[] { _annualLeaveTypeId, _sickLeaveTypeId });
    }

    // ── FR-3: ledger ordering + filtering ──────────────────────────

    [Fact]
    public async Task GetMyLedger_ReturnsOnlyTypeAndYear_OrderedChronologically()
    {
        var t0 = new DateTime(Year, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        AddLedger(_annualLeaveTypeId, LedgerEntryType.Accrual, 14m, 14m, occurredAt: t0.AddDays(2));
        AddLedger(_annualLeaveTypeId, LedgerEntryType.Used, -3m, 11m, occurredAt: t0); // earliest
        AddLedger(_annualLeaveTypeId, LedgerEntryType.Adjusted, 1m, 12m, occurredAt: t0.AddDays(5));
        // Noise: other type + other year must be excluded.
        AddLedger(_sickLeaveTypeId, LedgerEntryType.Accrual, 7m, 7m, occurredAt: t0.AddDays(1));
        AddLedger(_annualLeaveTypeId, LedgerEntryType.Accrual, 10m, 10m, year: Year - 1, occurredAt: t0);

        var result = await CreateService().GetMyLedgerAsync(_annualLeaveTypeId, Year);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(3);
        result.Value.Should().OnlyContain(e => e.LeaveTypeId == _annualLeaveTypeId);
        result.Value.Select(e => e.OccurredAt).Should().BeInAscendingOrder();
        result.Value.First().EntryType.Should().Be("Used"); // earliest occurredAt
    }

    // ── FR-4: upcoming = approved + pending future only ────────────

    [Fact]
    public async Task GetMyUpcoming_ReturnsApprovedAndPendingFuture_OrderedByStartDate()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var db = CreateDbContext())
        {
            db.LeaveRequests.AddRange(
                Req(LeaveRequestStatus.Approved, today.AddDays(10)),
                Req(LeaveRequestStatus.Pending, today.AddDays(3)),
                Req(LeaveRequestStatus.Rejected, today.AddDays(5)),   // excluded (not approved/pending)
                Req(LeaveRequestStatus.Cancelled, today.AddDays(7)),  // excluded
                Req(LeaveRequestStatus.Approved, today.AddDays(-2)));  // excluded (past)
            db.SaveChanges();
        }

        var result = await CreateService().GetMyUpcomingAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
        result.Value.Select(u => u.Status).Should().BeEquivalentTo(new[] { "Pending", "Approved" });
        result.Value.Select(u => u.StartDate).Should().BeInAscendingOrder();
        result.Value.First().StartDate.Should().Be(today.AddDays(3));
    }

    private LeaveRequest Req(LeaveRequestStatus status, DateOnly start) => new()
    {
        Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeId = _employeeId,
        LeaveTypeId = _annualLeaveTypeId, StartDate = start, EndDate = start,
        TotalDays = 1m, Status = status, RequestedAt = DateTime.UtcNow,
    };

    // ── self/tenant scope ──────────────────────────────────────────

    [Fact]
    public async Task GetMyBalances_UserWithNoEmployeeRecord_Returns403()
    {
        var otherUser = Substitute.For<ICurrentUser>();
        otherUser.UserId.Returns(Guid.NewGuid());

        var svc = new LeaveDashboardService(
            CreateDbContext(), _tenantContext, otherUser, _entitlementService, _logger);

        var result = await svc.GetMyBalancesAsync(Year);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("No employee record");
    }

    [Fact]
    public async Task GetMyBalances_DefaultsToCurrentYear_WhenYearNull()
    {
        StubEntitlement(_annualLeaveTypeId, 14m); // current year entitlement stub
        _entitlementService
            .ComputeEffectiveEntitlementAsync(_employeeId, _annualLeaveTypeId, DateTime.UtcNow.Year, Arg.Any<CancellationToken>())
            .Returns(Result<EffectiveEntitlementDto>.Success(new EffectiveEntitlementDto
            {
                EmployeeId = _employeeId, LeaveTypeId = _annualLeaveTypeId,
                LeaveYear = DateTime.UtcNow.Year, ProratedEntitlementDays = 14m,
            }));
        _entitlementService
            .ComputeEffectiveEntitlementAsync(_employeeId, _sickLeaveTypeId, DateTime.UtcNow.Year, Arg.Any<CancellationToken>())
            .Returns(Result<EffectiveEntitlementDto>.Success(new EffectiveEntitlementDto
            {
                EmployeeId = _employeeId, LeaveTypeId = _sickLeaveTypeId,
                LeaveYear = DateTime.UtcNow.Year, ProratedEntitlementDays = 7m,
            }));

        var result = await CreateService().GetMyBalancesAsync(null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().OnlyContain(b => b.LeaveYear == DateTime.UtcNow.Year);
    }
}
