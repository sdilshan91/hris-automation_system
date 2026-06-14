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
        => new(CreateDbContext(), _tenantContext, _entitlementService, _logger);

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

        // In the new year the employee uses 3 days (FIFO against the 5 carried) -> 2 remain.
        SeedUsed(typeId, ToYear, 3m);

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

        // Use 5+ days in the new year -> all carried consumed, nothing to expire.
        SeedUsed(typeId, ToYear, 5m);

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
}
