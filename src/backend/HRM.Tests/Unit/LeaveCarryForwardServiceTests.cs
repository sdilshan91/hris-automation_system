// ============================================================================
// US-LV-008: Leave carry-forward / expiry service unit tests.
//
// Covers:
//   - AC-1/AC-2: 8 unused, 5 limit -> 5 CarryForward + 3 Expired ledger entries (Test Hint).
//   - AC-4: zero limit -> all unused expired.
//   - BR-6: unlimited / negative-balance / zero-entitlement types are skipped.
//   - BR-5: encashable type -> Encashed ledger entry instead of Expired.
//   - NFR-3 idempotency: running the year-end calc twice yields no duplicate tracking/ledger rows.
//   - FR-3/BR-4 expiry: carried days remaining after FIFO usage are expired; idempotent re-run.
//   - FR-5 preview: preview output matches what the year-end job actually produces (Test Hint).
//
// Uses the EF Core InMemory provider (mirrors LeaveDashboardServiceTests). The entitlement
// engine is stubbed via NSubstitute so the balance math is asserted independently of US-LV-002.
// The unused balance is driven through the stubbed entitlement + seeded ledger entries.
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class LeaveCarryForwardServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ILeaveEntitlementService _entitlementService;
    private readonly ILogger<LeaveCarryForwardService> _logger;

    private readonly Guid _employeeId = Guid.NewGuid();

    private const int FromYear = 2026;
    private const int ToYear = 2027;

    public LeaveCarryForwardServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _entitlementService = Substitute.For<ILeaveEntitlementService>();
        _logger = Substitute.For<ILogger<LeaveCarryForwardService>>();

        SeedEmployee();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private LeaveCarryForwardService CreateService()
    {
        var db = CreateDbContext();
        return new(db, _tenantContext, _entitlementService, _logger,
            new TenantLeaveYearResolver(db, _tenantContext));
    }

    private void SeedEmployee()
    {
        using var db = CreateDbContext();
        db.Employees.Add(new Employee
        {
            Id = _employeeId,
            TenantId = _tenantId,
            UserId = Guid.NewGuid(),
            EmployeeNo = "E-1",
            FirstName = "Alice",
            LastName = "A",
            Email = "a@a.com",
            DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = Guid.NewGuid(),
            JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
        });
        db.SaveChanges();
    }

    /// <summary>Seeds a leave type and stubs the entitlement engine to return its annual value.</summary>
    private Guid SeedLeaveType(
        string name, decimal annual, decimal? carryLimit, int? expiryMonths,
        bool encashable = false, decimal? maxEncash = null, bool negativeAllowed = false)
    {
        var id = Guid.NewGuid();
        using (var db = CreateDbContext())
        {
            db.LeaveTypes.Add(new LeaveType
            {
                Id = id,
                TenantId = _tenantId,
                Name = name,
                AnnualEntitlement = annual,
                AccrualFrequency = AccrualFrequency.Upfront,
                CarryForwardLimit = carryLimit,
                CarryForwardExpiryMonths = expiryMonths,
                Encashable = encashable,
                MaxEncashDays = maxEncash,
                NegativeBalanceAllowed = negativeAllowed,
                Gender = LeaveTypeGender.All,
                IsActive = true,
            });
            db.SaveChanges();
        }

        _entitlementService
            .ComputeEffectiveEntitlementAsync(_employeeId, id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<EffectiveEntitlementDto>.Success(new EffectiveEntitlementDto
            {
                EmployeeId = _employeeId,
                LeaveTypeId = id,
                LeaveYear = FromYear,
                BaseEntitlementDays = annual,
                ProratedEntitlementDays = annual,
                Source = "leave_type_default",
            }));

        return id;
    }

    /// <summary>Seeds a "Used" ledger entry to drive the unused balance down.</summary>
    private void SeedUsed(Guid leaveTypeId, int year, decimal days)
    {
        using var db = CreateDbContext();
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EntryType = LedgerEntryType.Used,
            EmployeeId = _employeeId,
            LeaveTypeId = leaveTypeId,
            LeaveYear = year,
            Amount = -days,
            BalanceAfter = 0m,
            OccurredAt = new DateTime(year, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        db.SaveChanges();
    }

    /// <summary>
    /// DF-19 / ISSUE-045: records carry-forward-pool consumption on the tracking bucket the way the
    /// leave-approval deduction now does — by incrementing the PERSISTED ConsumedDays counter (which
    /// the expiry job reads), rather than only writing a new-year "Used" ledger row (which expiry no
    /// longer derives from). Call after ProcessYearEnd to represent days drawn from the carried pool.
    /// </summary>
    private async Task ConsumeCarried(Guid leaveTypeId, decimal days)
    {
        using var db = CreateDbContext();
        var tracking = await db.LeaveCarryForwardTrackings
            .SingleAsync(t => t.EmployeeId == _employeeId && t.LeaveTypeId == leaveTypeId);
        tracking.ConsumedDays += days;
        await db.SaveChangesAsync();
    }

    /// <summary>DF-65: seeds a PRIOR-year carry bucket (carried FROM fromYear INTO toYear) directly.</summary>
    private void SeedPriorBucket(
        Guid leaveTypeId, int fromYear, int toYear, decimal carried, DateOnly expiryDate)
    {
        using var db = CreateDbContext();
        db.LeaveCarryForwardTrackings.Add(new LeaveCarryForwardTracking
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EmployeeId = _employeeId,
            LeaveTypeId = leaveTypeId,
            FromYear = fromYear,
            ToYear = toYear,
            CarriedDays = carried,
            ConsumedDays = 0m,
            ExpiredDays = 0m,
            ExpiryDate = expiryDate,
            Status = CarryForwardTrackingStatus.Active,
        });
        db.SaveChanges();
    }

    /// <summary>DF-65: seeds the CarryForward credit row that a prior bucket wrote INTO the given year.</summary>
    private void SeedCarryIn(Guid leaveTypeId, int year, decimal days)
    {
        using var db = CreateDbContext();
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EntryType = LedgerEntryType.CarryForward,
            EmployeeId = _employeeId,
            LeaveTypeId = leaveTypeId,
            LeaveYear = year,
            Amount = days,
            BalanceAfter = days,
            OccurredAt = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        db.SaveChanges();
    }

    private async Task<List<LeaveLedger>> LedgerFor(Guid leaveTypeId, int year)
    {
        using var db = CreateDbContext();
        return await db.LeaveLedgerEntries
            .Where(l => l.EmployeeId == _employeeId && l.LeaveTypeId == leaveTypeId && l.LeaveYear == year)
            .ToListAsync();
    }

    private async Task<List<LeaveCarryForwardTracking>> TrackingFor(Guid leaveTypeId)
    {
        using var db = CreateDbContext();
        return await db.LeaveCarryForwardTrackings
            .Where(t => t.EmployeeId == _employeeId && t.LeaveTypeId == leaveTypeId)
            .ToListAsync();
    }

    // ── AC-1 / AC-2: 8 unused, 5 limit -> 5 carried + 3 expired ─────

    [Fact]
    public async Task YearEnd_8Unused_5Limit_Writes5CarryForwardAnd3Expired()
    {
        // 14 entitlement - 6 used = 8 unused; limit 5 -> carry 5, forfeit 3.
        var typeId = SeedLeaveType("Annual Leave", annual: 14m, carryLimit: 5m, expiryMonths: 3);
        SeedUsed(typeId, FromYear, 6m);

        var result = await CreateService().ProcessYearEndAsync(FromYear);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);

        var carry = (await LedgerFor(typeId, ToYear)).Single(l => l.EntryType == LedgerEntryType.CarryForward);
        carry.Amount.Should().Be(5m);
        carry.OccurredAt.Should().Be(new DateTime(ToYear, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var expired = (await LedgerFor(typeId, FromYear)).Single(l => l.EntryType == LedgerEntryType.Expired);
        expired.Amount.Should().Be(-3m);

        var tracking = (await TrackingFor(typeId)).Single();
        tracking.CarriedDays.Should().Be(5m);
        tracking.FromYear.Should().Be(FromYear);
        tracking.ToYear.Should().Be(ToYear);
        tracking.ExpiryDate.Should().Be(new DateOnly(ToYear, 4, 1));
        tracking.Status.Should().Be(CarryForwardTrackingStatus.Active);
    }

    // ── AC-4: zero limit -> all unused expired ──────────────────────

    [Fact]
    public async Task YearEnd_ZeroLimit_ExpiresAllUnused()
    {
        var typeId = SeedLeaveType("No-Carry Leave", annual: 10m, carryLimit: 0m, expiryMonths: null);
        SeedUsed(typeId, FromYear, 4m); // 10 - 4 = 6 unused

        await CreateService().ProcessYearEndAsync(FromYear);

        (await LedgerFor(typeId, ToYear)).Should().NotContain(l => l.EntryType == LedgerEntryType.CarryForward);
        var expired = (await LedgerFor(typeId, FromYear)).Single(l => l.EntryType == LedgerEntryType.Expired);
        expired.Amount.Should().Be(-6m);
    }

    // ── BR-6: non-applicable types are skipped ──────────────────────

    [Fact]
    public async Task YearEnd_NullLimitType_IsSkipped()
    {
        var typeId = SeedLeaveType("Unlimited", annual: 10m, carryLimit: null, expiryMonths: null);

        var result = await CreateService().ProcessYearEndAsync(FromYear);

        result.Value.Should().Be(0);
        (await TrackingFor(typeId)).Should().BeEmpty();
        (await LedgerFor(typeId, FromYear)).Should().BeEmpty();
    }

    [Fact]
    public async Task YearEnd_NegativeBalanceType_IsSkipped()
    {
        var typeId = SeedLeaveType("Unpaid", annual: 10m, carryLimit: 5m, expiryMonths: 3, negativeAllowed: true);

        await CreateService().ProcessYearEndAsync(FromYear);

        (await TrackingFor(typeId)).Should().BeEmpty();
    }

    // ── BR-5: encashable type encashes the forfeitable balance ──────

    [Fact]
    public async Task YearEnd_EncashableType_WritesEncashedInsteadOfExpired()
    {
        // 12 entitlement - 0 used = 12 unused; limit 5 -> carry 5, forfeit 7. Encashable, no cap.
        var typeId = SeedLeaveType("Encashable AL", annual: 12m, carryLimit: 5m, expiryMonths: 3, encashable: true);

        await CreateService().ProcessYearEndAsync(FromYear);

        var fromLedger = await LedgerFor(typeId, FromYear);
        fromLedger.Should().Contain(l => l.EntryType == LedgerEntryType.Encashed && l.Amount == -7m);
        fromLedger.Should().NotContain(l => l.EntryType == LedgerEntryType.Expired);
    }

    [Fact]
    public async Task YearEnd_EncashableWithCap_EncashesUpToCapAndExpiresResidue()
    {
        // 12 unused; limit 5 -> forfeit 7. Encashable but capped at 4 -> encash 4, expire 3.
        var typeId = SeedLeaveType("Capped Encash", annual: 12m, carryLimit: 5m, expiryMonths: 3,
            encashable: true, maxEncash: 4m);

        await CreateService().ProcessYearEndAsync(FromYear);

        var fromLedger = await LedgerFor(typeId, FromYear);
        fromLedger.Should().Contain(l => l.EntryType == LedgerEntryType.Encashed && l.Amount == -4m);
        fromLedger.Should().Contain(l => l.EntryType == LedgerEntryType.Expired && l.Amount == -3m);
    }

    // ── NFR-3 idempotency ───────────────────────────────────────────

    [Fact]
    public async Task YearEnd_RunTwice_NoDuplicateTrackingOrLedger()
    {
        var typeId = SeedLeaveType("Annual Leave", annual: 14m, carryLimit: 5m, expiryMonths: 3);
        SeedUsed(typeId, FromYear, 6m);

        await CreateService().ProcessYearEndAsync(FromYear);
        var secondRun = await CreateService().ProcessYearEndAsync(FromYear);

        secondRun.Value.Should().Be(0); // nothing new processed

        (await TrackingFor(typeId)).Should().ContainSingle();
        (await LedgerFor(typeId, ToYear)).Count(l => l.EntryType == LedgerEntryType.CarryForward).Should().Be(1);
        (await LedgerFor(typeId, FromYear)).Count(l => l.EntryType == LedgerEntryType.Expired).Should().Be(1);
    }

    // ── FR-3 / BR-4: expiry of remaining carried days after FIFO usage ─

    [Fact]
    public async Task Expiry_RemainingCarriedDaysAfterFifoUsage_AreExpired()
    {
        var typeId = SeedLeaveType("Annual Leave", annual: 14m, carryLimit: 5m, expiryMonths: 3);
        SeedUsed(typeId, FromYear, 6m); // 8 unused -> carry 5
        await CreateService().ProcessYearEndAsync(FromYear);

        // In the new year the employee uses 3 days FIFO against the 5 carried -> 2 remain. DF-19:
        // that consumption is now the PERSISTED ConsumedDays counter (what the deduction path writes
        // and what expiry reads), not a re-derived new-year Used ledger sum. The Used ledger row is
        // also written for realism, but expiry keys off ConsumedDays.
        SeedUsed(typeId, ToYear, 3m);
        await ConsumeCarried(typeId, 3m);

        // Expiry date is 2027-04-01; run the sweep after it.
        var result = await CreateService().ProcessExpiryAsync(new DateOnly(2027, 5, 1));
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);

        var expiredInNewYear = (await LedgerFor(typeId, ToYear))
            .Where(l => l.EntryType == LedgerEntryType.Expired).ToList();
        expiredInNewYear.Should().ContainSingle();
        expiredInNewYear[0].Amount.Should().Be(-2m); // the 2 remaining carried days

        var tracking = (await TrackingFor(typeId)).Single();
        tracking.ExpiredDays.Should().Be(2m);
        tracking.Status.Should().Be(CarryForwardTrackingStatus.Expired);
    }

    [Fact]
    public async Task Expiry_BeforeExpiryDate_DoesNothing()
    {
        var typeId = SeedLeaveType("Annual Leave", annual: 14m, carryLimit: 5m, expiryMonths: 3);
        SeedUsed(typeId, FromYear, 6m);
        await CreateService().ProcessYearEndAsync(FromYear);

        // Expiry date is 2027-04-01; sweep BEFORE it -> nothing expires.
        var result = await CreateService().ProcessExpiryAsync(new DateOnly(2027, 3, 1));

        result.Value.Should().Be(0);
        (await LedgerFor(typeId, ToYear)).Should().NotContain(l => l.EntryType == LedgerEntryType.Expired);
        (await TrackingFor(typeId)).Single().Status.Should().Be(CarryForwardTrackingStatus.Active);
    }

    [Fact]
    public async Task Expiry_RunTwice_DoesNotDoubleExpire()
    {
        var typeId = SeedLeaveType("Annual Leave", annual: 14m, carryLimit: 5m, expiryMonths: 3);
        SeedUsed(typeId, FromYear, 6m);
        await CreateService().ProcessYearEndAsync(FromYear);

        await CreateService().ProcessExpiryAsync(new DateOnly(2027, 5, 1));
        var second = await CreateService().ProcessExpiryAsync(new DateOnly(2027, 6, 1));

        second.Value.Should().Be(0);
        (await LedgerFor(typeId, ToYear)).Count(l => l.EntryType == LedgerEntryType.Expired).Should().Be(1);
    }

    [Fact]
    public async Task Expiry_AllCarriedConsumedByFifo_NoExpiryEntry()
    {
        var typeId = SeedLeaveType("Annual Leave", annual: 14m, carryLimit: 5m, expiryMonths: 3);
        SeedUsed(typeId, FromYear, 6m); // carry 5
        await CreateService().ProcessYearEndAsync(FromYear);

        // Use 5 days in the new year -> all carried consumed, nothing to expire. DF-19: expressed as
        // the PERSISTED ConsumedDays counter that the deduction path maintains and expiry reads.
        SeedUsed(typeId, ToYear, 5m);
        await ConsumeCarried(typeId, 5m);

        var result = await CreateService().ProcessExpiryAsync(new DateOnly(2027, 5, 1));

        result.Value.Should().Be(0);
        (await LedgerFor(typeId, ToYear)).Should().NotContain(l => l.EntryType == LedgerEntryType.Expired);
        (await TrackingFor(typeId)).Single().Status.Should().Be(CarryForwardTrackingStatus.Consumed);
    }

    // ── FR-5: preview matches the job ───────────────────────────────

    [Fact]
    public async Task Preview_MatchesYearEndJobOutput()
    {
        var typeId = SeedLeaveType("Annual Leave", annual: 14m, carryLimit: 5m, expiryMonths: 3);
        SeedUsed(typeId, FromYear, 6m); // 8 unused -> carry 5, forfeit 3

        var preview = await CreateService().PreviewYearEndAsync(FromYear);
        preview.IsSuccess.Should().BeTrue();
        var row = preview.Value!.Single();
        row.CarryForward.Should().Be(5m);
        row.Forfeited.Should().Be(3m);
        row.UnusedBalance.Should().Be(8m);
        row.FromYear.Should().Be(FromYear);
        row.ToYear.Should().Be(ToYear);

        // Now actually run the job and confirm the ledger matches the preview's numbers.
        await CreateService().ProcessYearEndAsync(FromYear);
        var carry = (await LedgerFor(typeId, ToYear)).Single(l => l.EntryType == LedgerEntryType.CarryForward);
        var expired = (await LedgerFor(typeId, FromYear)).Single(l => l.EntryType == LedgerEntryType.Expired);

        carry.Amount.Should().Be(row.CarryForward);
        Math.Abs(expired.Amount).Should().Be(row.Forfeited);
    }

    [Fact]
    public async Task Preview_CommitsNothing()
    {
        var typeId = SeedLeaveType("Annual Leave", annual: 14m, carryLimit: 5m, expiryMonths: 3);
        SeedUsed(typeId, FromYear, 6m);

        await CreateService().PreviewYearEndAsync(FromYear);

        (await TrackingFor(typeId)).Should().BeEmpty();
        (await LedgerFor(typeId, ToYear)).Should().NotContain(l => l.EntryType == LedgerEntryType.CarryForward);
    }

    // ── DF-65: year-end sweep must not leave a superseded prior-year bucket for expiry to re-forfeit ──

    [Fact]
    public async Task YearEndSweep_SupersedesPriorCarryBucket_SoExpiryDoesNotDoubleForfeit_DF65()
    {
        // A leave type whose carried days stay valid a FULL year (expiryMonths=12) — the reachable DF-65
        // config: a bucket carried INTO the closing year is still Active at that year's close (its expiry
        // lands 2027-01-01, AFTER the 2026 close), so no monthly expiry terminalized it first.
        var typeId = SeedLeaveType("Annual", annual: 3m, carryLimit: 5m, expiryMonths: 12, encashable: false);

        // B1: 5 days carried from 2025 INTO 2026 (ToYear == FromYear), un-consumed, expiring 2027-01-01,
        // plus the matching carry-in credit so the 2026 unused balance actually sees those 5 days.
        SeedPriorBucket(typeId, fromYear: 2025, toYear: FromYear, carried: 5m, expiryDate: new DateOnly(2027, 1, 1));
        SeedCarryIn(typeId, FromYear, 5m);

        // 2026 unused = entitlement 3 + carry-in 5 = 8; limit 5 → carry 5 into 2027, forfeit 3.
        await CreateService().ProcessYearEndAsync(FromYear);

        var b1 = (await TrackingFor(typeId)).Single(t => t.ToYear == FromYear);
        // (b) authoritative fix: the sweep terminalized the superseded prior-year bucket.
        b1.Status.Should().Be(CarryForwardTrackingStatus.Consumed,
            "the year-end sweep supersedes the prior-year carry bucket, so expiry must skip it");
        // (a) belt-and-suspenders: the pool-routed forfeit bumped its ConsumedDays by the forfeited carried portion.
        b1.ConsumedDays.Should().Be(3m,
            "the forfeit routed through PooledLeaveLedger drew the 3 forfeited days from the carried pool");

        var expiredBySweep = (await LedgerFor(typeId, FromYear))
            .Where(l => l.EntryType == LedgerEntryType.Expired).Sum(l => -l.Amount);
        expiredBySweep.Should().Be(3m, "the sweep forfeits exactly unused(8) - limit(5) = 3");

        // Monthly expiry runs AFTER the 2026 close (2027-02-01 >= B1.ExpiryDate 2027-01-01). WITHOUT the
        // supersede, B1 is Active and expiry re-forfeits its remaining days (total forfeit > 3 — 5 with (a)
        // still bumping ConsumedDays by 3, 8 with neither fix; the DF-65 double-forfeit, employee detriment).
        // WITH it, B1 is terminal → nothing to re-forfeit.
        var expiredCount = (await CreateService().ProcessExpiryAsync(new DateOnly(2027, 2, 1))).Value;
        expiredCount.Should().Be(0, "the superseded bucket is terminal, so expiry does not re-forfeit its days");

        var totalExpired = (await LedgerFor(typeId, FromYear))
            .Where(l => l.EntryType == LedgerEntryType.Expired).Sum(l => -l.Amount);
        totalExpired.Should().Be(3m, "the carried days are forfeited ONCE by the sweep, never a second time by expiry");
    }

    /// <summary>
    /// DF-65 — the case fix (a) CANNOT reach: everything carries forward, NOTHING is forfeited
    /// (unused &lt;= limit), so the pool-routed forfeit never runs. Only the (b) supersede stops the prior
    /// bucket from being wholly re-forfeited by expiry — a pure day-loss. This proves (b) is load-bearing
    /// on its own.
    /// </summary>
    [Fact]
    public async Task YearEndSweep_AllCarriedNothingForfeited_StillSupersedesPriorBucket_DF65()
    {
        // entitlement 3 + carry-in 5 = 8 unused; limit 10 -> carry 8, forfeit 0 (the (a) branch is skipped).
        // (A positive entitlement is required — LeaveCarryForwardCalculator.AppliesTo skips zero-entitlement types.)
        var typeId = SeedLeaveType("Annual", annual: 3m, carryLimit: 10m, expiryMonths: 12, encashable: false);
        SeedPriorBucket(typeId, fromYear: 2025, toYear: FromYear, carried: 5m, expiryDate: new DateOnly(2027, 1, 1));
        SeedCarryIn(typeId, FromYear, 5m);

        await CreateService().ProcessYearEndAsync(FromYear);

        var b1 = (await TrackingFor(typeId)).Single(t => t.ToYear == FromYear);
        b1.Status.Should().Be(CarryForwardTrackingStatus.Consumed, "(b) supersedes the prior bucket even with no forfeit");
        b1.ConsumedDays.Should().Be(0m, "nothing was forfeited, so the (a) pool-routed forfeit never ran");
        (await LedgerFor(typeId, FromYear)).Should().NotContain(l => l.EntryType == LedgerEntryType.Expired);

        // WITHOUT (b), B1 (Active, remaining 5) is re-forfeited here — the full 5 carried days are lost.
        var expiredCount = (await CreateService().ProcessExpiryAsync(new DateOnly(2027, 2, 1))).Value;
        expiredCount.Should().Be(0, "the superseded bucket is terminal — its carried days live on ONLY in the new-year bucket");
        (await LedgerFor(typeId, FromYear)).Should().NotContain(l => l.EntryType == LedgerEntryType.Expired);
    }

    /// <summary>
    /// DF-65 — the encashable forfeit path: (a) threads the running balance across TWO pooled draws
    /// (Encashed, then Expired residual over the cap), both from the same prior bucket, so its ConsumedDays
    /// is bumped by the full carried draw and neither portion is later re-forfeited by expiry.
    /// </summary>
    [Fact]
    public async Task YearEndSweep_EncashableForfeit_ThreadsTwoPooledDraws_NoDoubleForfeit_DF65()
    {
        // entitlement 3 + carry-in 5 = 8 unused; limit 5 -> carry 5, forfeit 3. Encashable, cap 1 -> encash 1,
        // expire residual 2. Both draws come from the prior bucket's carried pool.
        var typeId = SeedLeaveType(
            "Annual", annual: 3m, carryLimit: 5m, expiryMonths: 12, encashable: true, maxEncash: 1m);
        SeedPriorBucket(typeId, fromYear: 2025, toYear: FromYear, carried: 5m, expiryDate: new DateOnly(2027, 1, 1));
        SeedCarryIn(typeId, FromYear, 5m);

        await CreateService().ProcessYearEndAsync(FromYear);

        var yearLedger = await LedgerFor(typeId, FromYear);
        yearLedger.Where(l => l.EntryType == LedgerEntryType.Encashed).Sum(l => -l.Amount)
            .Should().Be(1m, "encashment is capped at MaxEncashDays=1");
        yearLedger.Where(l => l.EntryType == LedgerEntryType.Expired).Sum(l => -l.Amount)
            .Should().Be(2m, "the 2-day residual over the encashment cap expires");

        var b1 = (await TrackingFor(typeId)).Single(t => t.ToYear == FromYear);
        b1.ConsumedDays.Should().Be(3m, "both pooled draws (1 encashed + 2 expired) came from the carried pool");
        b1.Status.Should().Be(CarryForwardTrackingStatus.Consumed, "(b) supersedes the prior bucket");

        var expiredCount = (await CreateService().ProcessExpiryAsync(new DateOnly(2027, 2, 1))).Value;
        expiredCount.Should().Be(0, "the superseded bucket is terminal — no second forfeit of the encashed/expired days");
        (await LedgerFor(typeId, FromYear))
            .Where(l => l.EntryType == LedgerEntryType.Expired).Sum(l => -l.Amount)
            .Should().Be(2m, "expiry adds no further forfeiture");
    }
}
