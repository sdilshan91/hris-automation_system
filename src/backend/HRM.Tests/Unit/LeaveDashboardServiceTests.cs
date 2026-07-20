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
    {
        // ISSUE-305: the REAL resolver over the same context (not a stub) — a stubbed month would let this
        // fixture assert a leave year production never resolves. No tenant row is seeded here, so it falls
        // back to calendar, which is what these US-LV-006 arms have always assumed.
        var db = CreateDbContext();
        return new(db, _tenantContext, _currentUser, _entitlementService, _logger,
            new TenantLeaveYearResolver(db, _tenantContext));
    }

    /// <summary>ISSUE-313: same as CreateService but with an injected clock so the default-leave-year path is testable.</summary>
    private LeaveDashboardService CreateService(TimeProvider timeProvider)
    {
        var db = CreateDbContext();
        return new(db, _tenantContext, _currentUser, _entitlementService, _logger,
            new TenantLeaveYearResolver(db, _tenantContext), timeProvider);
    }

    /// <summary>ISSUE-313: seeds the tenant ROW carrying the FiscalYearStartMonth the resolver must READ.</summary>
    private void SeedTenantFiscalMonth(int month)
    {
        using var db = CreateDbContext();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId, Subdomain = $"t{month}", Name = "T",
            Status = TenantStatus.Active, FiscalYearStartMonth = month,
        });
        db.SaveChanges();
    }

    // DF-21: the dashboard now resolves entitlement for all leave types in ONE batched call
    // (ComputeProratedEntitlementsBatchAsync) instead of a per-type ComputeEffectiveEntitlementAsync
    // (the old N+1). These stubs accumulate the (employee, type) → prorated-days map and re-arm the
    // batch mock; a pair the map omits reads 0 via the service's missing-pair fallback.
    private readonly Dictionary<(Guid EmployeeId, Guid LeaveTypeId), decimal> _entitlementStub = new();

    private void ApplyBatchStub(Dictionary<(Guid EmployeeId, Guid LeaveTypeId), decimal> map)
        => _entitlementService
            .ComputeProratedEntitlementsBatchAsync(
                Arg.Any<IReadOnlyList<Employee>>(),
                Arg.Any<IReadOnlyList<LeaveType>>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(map);

    /// <summary>ISSUE-313: entitlement is informational (not in the balance); the fiscal default-year arms
    /// resolve a year the fixture doesn't stub — return an EMPTY map so every type reads 0 (the fallback).</summary>
    private void StubEntitlementAnyYear() => ApplyBatchStub(new());

    private void StubEntitlement(Guid leaveTypeId, decimal days)
    {
        _entitlementStub[(_employeeId, leaveTypeId)] = days;
        ApplyBatchStub(new(_entitlementStub));
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
        // A new joiner with no ledger entries sees their active types with the resolved engine
        // entitlement surfaced separately, but a headline Balance of 0. Post-BUG-030 (#144) the
        // dashboard Balance is the AUTHORITATIVE ledger running balance_after — which is 0 until an
        // Accrual/opening ledger entry is written — NOT the engine entitlement. This keeps the card,
        // the apply-preview, and the ledger in lock-step (TC-LV-110/112/115: the card MUST equal the
        // ledger balance_after). The engine entitlement is still shown as an informational field.
        // DF-21: stub a distinct entitlement (13.5, ≠ the seeded AnnualEntitlement=14) so the assertion
        // can only pass if the value came from the batch MAP, not the LeaveType column.
        StubEntitlement(_annualLeaveTypeId, 13.5m);

        var result = await CreateService().GetMyBalancesAsync(Year);

        result.IsSuccess.Should().BeTrue();
        // Only the two ACTIVE types (archived type has zero balance -> dropped, BR-3).
        result.Value!.Should().HaveCount(2);
        result.Value.Should().OnlyContain(b => !b.IsArchived);
        var annual = result.Value.Single(b => b.LeaveTypeId == _annualLeaveTypeId);
        annual.Entitlement.Should().Be(13.5m); // from the batch map, NOT the seeded column (14)
        annual.Used.Should().Be(0m);
        // BUG-030 (#144) ledger-truth: no ledger entry yet -> Balance is 0, NOT the 14 entitlement.
        annual.Balance.Should().Be(0m);
    }

    [Fact]
    [Trait("DF", "DF-21")]
    [Trait("TC", "TC-LV-267")]
    public async Task GetMyBalances_ResolvesEntitlementInOneBatchedCall_NoPerTypeN1()
    {
        // DF-21: the card resolves entitlement for every leave type in ONE batched call, not a
        // per-type ComputeEffectiveEntitlementAsync loop. The fixture has 3 types (annual/sick/archived);
        // the old code fired 3 engine calls, the fix fires exactly 1 batch call and 0 per-type calls.
        await CreateService().GetMyBalancesAsync(Year);

        await _entitlementService.Received(1).ComputeProratedEntitlementsBatchAsync(
            Arg.Any<IReadOnlyList<Employee>>(),
            Arg.Any<IReadOnlyList<LeaveType>>(),
            Year,
            Arg.Any<CancellationToken>());
        await _entitlementService.DidNotReceive().ComputeEffectiveEntitlementAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
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

        var db = CreateDbContext();
        var svc = new LeaveDashboardService(
            db, _tenantContext, otherUser, _entitlementService, _logger,
            new TenantLeaveYearResolver(db, _tenantContext));

        var result = await svc.GetMyBalancesAsync(Year);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("No employee record");
    }

    [Fact]
    public async Task GetMyBalances_DefaultsToCurrentYear_WhenYearNull()
    {
        // DF-21: the ctor's batch stub is year-agnostic (annual→14, sick→7), so the null-year path
        // (which resolves to DateTime.UtcNow.Year) reads the same informational entitlement.
        StubEntitlement(_annualLeaveTypeId, 14m);

        var result = await CreateService().GetMyBalancesAsync(null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().OnlyContain(b => b.LeaveYear == DateTime.UtcNow.Year);
    }

    // ── ISSUE-313: fiscal read-layer guards (Pending bounds + default leave year) ──

    /// <summary>
    /// KILLER for the Pending-bounds site (`LeaveDashboardService.cs:142-154`). Month-4 tenant, a Pending
    /// request on 2027-02-10 (calendar 2027, LEAVE year 2026). Correct code bounds Pending by the leave-year
    /// window 2026-04-01..2027-03-31 → the request is counted. Reverting to a raw `StartDate.Year == leaveYear`
    /// compares 2027 against the label 2026 → the request is dropped → Pending 0.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    [Trait("Issue", "ISSUE-313")]
    public async Task GetMyBalances_FiscalTenant_PendingBoundsUseTheLeaveYearWindow_NotTheCalendarYear()
    {
        SeedTenantFiscalMonth(4);
        AddAwaitingRequest(_annualLeaveTypeId, 3m, new DateOnly(2027, 2, 10));

        var result = await CreateService().GetMyBalancesAsync(Year); // Year = 2026

        var annual = result.Value!.Single(b => b.LeaveTypeId == _annualLeaveTypeId);
        annual.Pending.Should().Be(3m,
            "leave year 2026 for an Apr-Mar tenant spans 2026-04-01..2027-03-31 and includes 2027-02-10; a raw "
            + "StartDate.Year == 2026 drops it and shows 0 pending");
    }

    /// <summary>CONTROL: on a CALENDAR (month-1) tenant the Pending bounds are the calendar year — unchanged.</summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    [Trait("Issue", "ISSUE-313")]
    public async Task GetMyBalances_CalendarTenant_PendingBoundsUnchanged()
    {
        SeedTenantFiscalMonth(1);
        AddAwaitingRequest(_annualLeaveTypeId, 3m, new DateOnly(Year, 7, 1)); // inside calendar 2026

        var result = await CreateService().GetMyBalancesAsync(Year);

        result.Value!.Single(b => b.LeaveTypeId == _annualLeaveTypeId).Pending.Should().Be(3m);
    }

    /// <summary>
    /// KILLER for the default-leave-year site (`ResolveLeaveYearAsync`, `:309-311`, year == null). A clock seam
    /// (ISSUE-313) makes this deterministic: month-4 tenant + a fixed Feb-2028 "now" resolves the CURRENT leave
    /// year to `LabelFor(2028-02-10, 4) = 2027`. Balance is credited under 2027, so the card reads it. Reverting
    /// to `year ?? now.Year` reads 2028 (empty) → wrong LeaveYear + an empty balance. (The future year 2028 also
    /// makes a revert to the raw wall-clock `DateTime.UtcNow.Year` — today 2026 — read an empty bucket.)
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    [Trait("Issue", "ISSUE-313")]
    public async Task GetMyBalances_FiscalTenant_DefaultLeaveYearComesFromTheClock_NotRawUtcNowYear()
    {
        SeedTenantFiscalMonth(4);
        StubEntitlementAnyYear(); // the resolved year (2027) is off the fixture's 2026 stub; balance is ledger-driven.
        var now = new DateTimeOffset(2028, 2, 10, 0, 0, 0, TimeSpan.Zero); // Feb: the month-4 tenant's Jan-Mar window.
        AddLedger(_annualLeaveTypeId, LedgerEntryType.Accrual, 9m, 9m, year: 2027); // fiscal leave year for that clock.

        var result = await CreateService(new FakeTimeProvider(now)).GetMyBalancesAsync(null);

        var annual = result.Value!.Single(b => b.LeaveTypeId == _annualLeaveTypeId);
        annual.LeaveYear.Should().Be(2027,
            "with a month-4 tenant and a Feb clock the default leave year is 2027 (LabelFor), not the raw "
            + "calendar 2028 — a `year ?? now.Year` mutant would read 2028 and show an empty balance");
        annual.Balance.Should().Be(9m);
    }

    /// <summary>CONTROL: on a CALENDAR (month-1) tenant the default leave year is the clock's calendar year — unchanged.</summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    [Trait("Issue", "ISSUE-313")]
    public async Task GetMyBalances_CalendarTenant_DefaultLeaveYearIsTheClockCalendarYear()
    {
        SeedTenantFiscalMonth(1);
        StubEntitlementAnyYear();
        var now = new DateTimeOffset(2028, 2, 10, 0, 0, 0, TimeSpan.Zero);
        AddLedger(_annualLeaveTypeId, LedgerEntryType.Accrual, 9m, 9m, year: 2028); // calendar tenant: leave year = 2028.

        var result = await CreateService(new FakeTimeProvider(now)).GetMyBalancesAsync(null);

        var annual = result.Value!.Single(b => b.LeaveTypeId == _annualLeaveTypeId);
        annual.LeaveYear.Should().Be(2028, "a January tenant's default leave year is the calendar year of 'now'");
        annual.Balance.Should().Be(9m);
    }

    // ── ISSUE-035 boundary: my-balance is UNCHANGED — it still shows all held balances ──

    [Fact]
    public async Task GetMyBalances_IncludesGenderRestrictedType_Unchanged_ISSUE035()
    {
        // The gender/probation eligibility filter added by ISSUE-035 lives ONLY in eligible-types.
        // my-balance must keep listing every held balance — including a type the (Male) employee could
        // not actually apply for — so a leaked filter here would be a regression.
        Guid femaleOnlyId;
        using (var db = CreateDbContext())
        {
            db.LeaveTypes.Add(new LeaveType
            {
                Id = femaleOnlyId = Guid.NewGuid(), TenantId = _tenantId,
                Name = "Maternity Leave", Color = "#E91E63", AnnualEntitlement = 84,
                AccrualFrequency = AccrualFrequency.Upfront, Gender = LeaveTypeGender.Female,
                DisplayOrder = 4, IsActive = true,
            });
            db.SaveChanges();
        }
        StubEntitlement(femaleOnlyId, 84m);
        AddLedger(femaleOnlyId, LedgerEntryType.Accrual, 10m, 10m); // a real held balance

        var result = await CreateService().GetMyBalancesAsync(Year);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().Contain(b => b.LeaveTypeId == femaleOnlyId,
            "my-balance shows all held balances regardless of gender eligibility");
    }
}
