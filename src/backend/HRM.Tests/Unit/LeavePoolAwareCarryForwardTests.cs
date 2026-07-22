// ============================================================================
// DF-19 / ISSUE-045: pool-aware carry-forward restoration — the NEW per-pool behavior.
//
// The existing cancel tests seed Used rows WITHOUT a LeaveRequestId, so CancelAsync takes the legacy
// single-Adjusted fallback and never exercises the new split. These arms drive the REAL service end-to-end:
//   1. Approve splits the deduction FIFO (carry-forward first) into per-pool Used rows + bumps ConsumedDays.
//   2. Cancel restores each pool exactly (carry-forward back to its bucket, ExpiryDate PRESERVED), decrementing
//      ConsumedDays; the aggregate balance is byte-identical to a single-row restore.
//   3. Expiry reads the PERSISTED ConsumedDays — so after a cancel the restored carried days are correctly
//      forfeited at their (past) expiry. The OLD derived model saw the cancelled Used row and wrongly kept them.
//   4. A carry-forward restore whose bucket already terminally Expired falls back to accrual (can't un-expire).
// EF Core InMemory through the real LeaveRequestService + LeaveCarryForwardService.
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

public sealed class LeavePoolAwareCarryForwardTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _reportUserId = Guid.NewGuid();
    private Guid _managerEmpId, _reportEmpId, _leaveTypeId;

    private const int FromYear = 2026;
    private const int ToYear = 2027;
    private static readonly DateOnly LeaveStart = new(ToYear, 6, 1);         // in the ToYear leave year
    private static readonly DateOnly ExpiryDate = new(ToYear, 3, 31);        // carry-forward expiry (past by asOf)
    private static readonly DateOnly ExpiryAsOf = new(ToYear, 12, 31);

    private readonly ITenantContext _tenantContext;

    public LeavePoolAwareCarryForwardTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        SeedReferenceData();
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private LeaveRequestService LeaveService(Guid actingUserId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(actingUserId);
        return new LeaveRequestService(Db(), _tenantContext, user, new NoOpHolidayProvider(),
            Substitute.For<ILeaveNotificationService>(), Substitute.For<ILogger<LeaveRequestService>>(),
            new TenantLeaveYearResolver(Db(), _tenantContext));
    }

    private LeaveCarryForwardService CarryForwardService()
        => new(Db(), _tenantContext, Substitute.For<ILeaveEntitlementService>(),
            Substitute.For<ILogger<LeaveCarryForwardService>>(), new TenantLeaveYearResolver(Db(), _tenantContext));

    private void SeedReferenceData()
    {
        using var db = Db();
        db.LeaveTypes.Add(new LeaveType
        {
            Id = _leaveTypeId = Guid.NewGuid(), TenantId = _tenantId, Name = "Annual Leave",
            AnnualEntitlement = 20, AccrualFrequency = AccrualFrequency.Upfront, Gender = LeaveTypeGender.All,
            CarryForwardLimit = 5, CarryForwardExpiryMonths = 3, NegativeBalanceAllowed = false, IsActive = true,
        });
        db.Employees.AddRange(
            NewEmployee(_managerEmpId = Guid.NewGuid(), "Mgr", _managerUserId, reportsTo: null),
            NewEmployee(_reportEmpId = Guid.NewGuid(), "Rep", _reportUserId, reportsTo: null));
        db.SaveChanges();

        // Set the report → manager link after both exist.
        using var db2 = Db();
        var rep = db2.Employees.Single(e => e.Id == _reportEmpId);
        rep.ReportsToEmployeeId = _managerEmpId;
        db2.SaveChanges();
    }

    private Employee NewEmployee(Guid id, string name, Guid userId, Guid? reportsTo) => new()
    {
        Id = id, TenantId = _tenantId, UserId = userId, EmployeeNo = $"E-{name}",
        FirstName = name, LastName = "X", Email = $"{name}@t.com".ToLowerInvariant(),
        DateOfJoining = new DateTime(2020, 1, 1), DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
        EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        ReportsToEmployeeId = reportsTo,
    };

    /// <summary>Seeds the report's 2027 opening balance (accrual) + an active carry-forward bucket.</summary>
    private void SeedBalanceAndBucket(decimal openingBalance, decimal carried, string status = CarryForwardTrackingStatus.Active)
    {
        using var db = Db();
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EntryType = LedgerEntryType.Accrual,
            EmployeeId = _reportEmpId, LeaveTypeId = _leaveTypeId, LeaveYear = ToYear,
            // Opening balance must predate the approval timestamp (now) so it sorts first in the ledger.
            Amount = openingBalance, BalanceAfter = openingBalance, OccurredAt = new DateTime(FromYear, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        db.LeaveCarryForwardTrackings.Add(new LeaveCarryForwardTracking
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeId = _reportEmpId, LeaveTypeId = _leaveTypeId,
            FromYear = FromYear, ToYear = ToYear, CarriedDays = carried, ConsumedDays = 0m, ExpiredDays = 0m,
            ExpiryDate = ExpiryDate, Status = status,
        });
        db.SaveChanges();
    }

    private Guid SeedPendingRequest(decimal totalDays)
    {
        using var db = Db();
        var id = BaseEntity.NewUuidV7();
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = id, TenantId = _tenantId, EmployeeId = _reportEmpId, LeaveTypeId = _leaveTypeId,
            StartDate = LeaveStart, EndDate = LeaveStart.AddDays((int)totalDays - 1), TotalDays = totalDays,
            Status = LeaveRequestStatus.Pending, RequestedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        return id;
    }

    private List<LeaveLedger> Ledger()
    {
        using var db = Db();
        return db.LeaveLedgerEntries
            .Where(l => l.EmployeeId == _reportEmpId && l.LeaveTypeId == _leaveTypeId)
            .OrderBy(l => l.OccurredAt).ToList();
    }

    private LeaveCarryForwardTracking Bucket()
    {
        using var db = Db();
        return db.LeaveCarryForwardTrackings.Single(t => t.EmployeeId == _reportEmpId && t.ToYear == ToYear);
    }

    // ── Full lifecycle: approve splits FIFO, cancel restores per pool, expiry reads ConsumedDays ──
    [Fact]
    [Trait("TC", "TC-LV-008-19")]
    public async Task ApproveSplitsPools_CancelRestoresPools_ExpiryReadsConsumedDays()
    {
        SeedBalanceAndBucket(openingBalance: 20m, carried: 5m);   // 5 carried + 15 fresh = 20
        var requestId = SeedPendingRequest(totalDays: 8m);        // 8 > 5 carried → split 5 + 3

        // 1. APPROVE (as manager) → FIFO split: 5 from carry-forward, 3 from accrual.
        (await LeaveService(_managerUserId).ApproveAsync(requestId, null)).IsSuccess.Should().BeTrue();

        var used = Ledger().Where(l => l.EntryType == LedgerEntryType.Used).ToList();
        used.Should().HaveCount(2, "the deduction splits into a carry-forward row and an accrual row");
        var cfUsed = used.Single(l => l.Pool == LeavePool.CarryForward);
        cfUsed.Amount.Should().Be(-5m);
        cfUsed.CarryForwardTrackingId.Should().Be(Bucket().Id);
        used.Single(l => l.Pool == LeavePool.Accrual).Amount.Should().Be(-3m);
        Bucket().ConsumedDays.Should().Be(5m, "the carried bucket's persisted counter tracks the 5 consumed");
        Ledger().Last().BalanceAfter.Should().Be(12m, "20 − 8 = 12 (aggregate balance unchanged by the split)");

        // 2. CANCEL (as the report/owner) → restore each pool exactly; ExpiryDate preserved.
        (await LeaveService(_reportUserId).CancelAsync(requestId, "changed my mind")).IsSuccess.Should().BeTrue();

        var adjusted = Ledger().Where(l => l.EntryType == LedgerEntryType.Adjusted).ToList();
        adjusted.Should().HaveCount(2, "the restore splits back into the two pools");
        adjusted.Single(l => l.Pool == LeavePool.CarryForward).Amount.Should().Be(5m);
        adjusted.Single(l => l.Pool == LeavePool.Accrual).Amount.Should().Be(3m);
        Bucket().ConsumedDays.Should().Be(0m, "the 5 carried days are returned to the bucket");
        Bucket().ExpiryDate.Should().Be(ExpiryDate, "restored carry-forward days KEEP their original expiry");
        Ledger().Last().BalanceAfter.Should().Be(20m, "12 + 8 restored = 20 (aggregate restored, byte-identical)");

        // 3. EXPIRY reads the persisted ConsumedDays (now 0) → the 5 restored, past-expiry carried days forfeit.
        //    The OLD derived model still saw the cancelled −5/−3 Used rows → 0 remaining → wrongly forfeited none.
        (await CarryForwardService().ProcessExpiryAsync(ExpiryAsOf)).Value.Should().Be(1);
        Ledger().Where(l => l.EntryType == LedgerEntryType.Expired).Sum(l => l.Amount)
            .Should().Be(-5m, "all 5 restored carried days expire at their (past) expiry date — DF-19's fix");
    }

    // ── Restore whose bucket already terminally Expired → falls back to accrual (can't un-expire) ──
    [Fact]
    [Trait("TC", "TC-LV-008-19")]
    public async Task Cancel_WhenBucketAlreadyExpired_RestoresToAccrual_NotTheDeadBucket()
    {
        SeedBalanceAndBucket(openingBalance: 20m, carried: 5m);
        var requestId = SeedPendingRequest(totalDays: 5m);       // consumes all 5 carried

        (await LeaveService(_managerUserId).ApproveAsync(requestId, null)).IsSuccess.Should().BeTrue();
        Bucket().ConsumedDays.Should().Be(5m);

        // The bucket terminally expires before the cancel (its carried days are forfeited).
        using (var db = Db())
        {
            var b = db.LeaveCarryForwardTrackings.Single(t => t.Id == Bucket().Id);
            b.Status = CarryForwardTrackingStatus.Expired;
            b.ExpiredDays = 5m;
            db.SaveChanges();
        }

        (await LeaveService(_reportUserId).CancelAsync(requestId, "changed")).IsSuccess.Should().BeTrue();

        var adjusted = Ledger().Where(l => l.EntryType == LedgerEntryType.Adjusted).ToList();
        adjusted.Should().ContainSingle("the 5 days come back as one accrual restore, not to the dead bucket");
        adjusted[0].Pool.Should().Be(LeavePool.Accrual, "a terminally-expired bucket cannot be un-expired");
        adjusted[0].CarryForwardTrackingId.Should().BeNull();
        adjusted[0].Amount.Should().Be(5m);
        var dead = Bucket();
        dead.Status.Should().Be(CarryForwardTrackingStatus.Expired, "the expired bucket is left terminal");
        dead.ConsumedDays.Should().Be(5m, "the terminal bucket's counters are untouched by the accrual fallback");
        dead.ExpiredDays.Should().Be(5m);
    }

    // ── The DF-19 wiring fix: EVERY consumption path (encashment/LOP/approval) draws through the one
    //    shared pooled split, which bumps ConsumedDays — so expiry does not over-forfeit already-consumed
    //    carried days. This drives the shared choke point directly (as encashment does: Encashed, no request). ──
    [Fact]
    [Trait("TC", "TC-LV-008-19")]
    public async Task SharedPooledDeduction_BumpsConsumedDays_SoExpiryDoesNotOverForfeit()
    {
        SeedBalanceAndBucket(openingBalance: 20m, carried: 5m);   // 5 carried (exp 2027-03-31) + 15 accrual

        // Encashment-style draw-down: 5 forfeitable days, Encashed entry, NO leave request — exactly the
        // path that pre-fix bypassed ConsumedDays. The shared helper draws carry-forward FIRST.
        using (var db = Db())
        {
            var result = await PooledLeaveLedger.AppendDeductionAsync(
                db, _tenantId, _reportEmpId, _leaveTypeId, ToYear,
                totalDays: 5m, leaveRequestId: null, currentBalance: 20m,
                "HR leave encashment: 5 day(s)", new DateTime(ToYear, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                LedgerEntryType.Encashed, default);
            await db.SaveChangesAsync();
            result.CarriedPortion.Should().Be(5m, "all 5 forfeitable days are drawn from the carried bucket first");
        }

        var encashed = Ledger().Single(l => l.EntryType == LedgerEntryType.Encashed);
        encashed.Pool.Should().Be(LeavePool.CarryForward);
        encashed.CarryForwardTrackingId.Should().Be(Bucket().Id);
        Bucket().ConsumedDays.Should().Be(5m, "the encashment bumped the persisted counter — the fix");

        // Expiry now sees ConsumedDays=5 → nothing left to forfeit. WITHOUT the bump (the pre-fix bug) it
        // would forfeit the full 5 the employee was already paid for → a double-count driving balance negative.
        (await CarryForwardService().ProcessExpiryAsync(ExpiryAsOf)).Value.Should().Be(0);
        Ledger().Should().NotContain(l => l.EntryType == LedgerEntryType.Expired,
            "the encashed carried days are not forfeited a second time");
    }

    // ── Rollback safety (encashment when the earning adjustment is rejected): Undo reverts the bump + rows ──
    [Fact]
    [Trait("TC", "TC-LV-008-19")]
    public async Task SharedPooledDeduction_Undo_RevertsBump_AndDoesNotPersistRows()
    {
        SeedBalanceAndBucket(openingBalance: 20m, carried: 5m);

        using (var db = Db())
        {
            var result = await PooledLeaveLedger.AppendDeductionAsync(
                db, _tenantId, _reportEmpId, _leaveTypeId, ToYear,
                totalDays: 5m, leaveRequestId: null, currentBalance: 20m,
                "HR leave encashment (rolled back)", new DateTime(ToYear, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                LedgerEntryType.Encashed, default);

            result.Undo(db);          // adjustment rejected → both-or-neither
            await db.SaveChangesAsync();
        }

        Ledger().Should().NotContain(l => l.EntryType == LedgerEntryType.Encashed,
            "the detached draw-down row was never persisted");
        Bucket().ConsumedDays.Should().Be(0m, "the in-memory ConsumedDays bump was reverted");
    }
}
