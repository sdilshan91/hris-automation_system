// ============================================================================
// DF-62-parity (user decision: divergence-guard + document, NO money-math change): the two forfeiture
// ceilings in the leave subsystem derive from DIFFERENT balance bases and agree only when
// Σaccruals == engine ProratedEntitlementDays.
//
//   * Encashment gate  (LeaveEncashmentService.cs:104-107): forfeitableCeiling =
//       LeaveCarryForwardCalculator.Compute(availableBalance, CarryForwardLimit).ForfeitedDays,
//       where availableBalance = the latest ledger BalanceAfter (the running total = Σ of ALL ledger
//       amounts, incl. the ACTUAL accrual rows).
//   * Year-end forfeiture (LeaveCarryForwardService.cs:150-152): Compute(unused, CarryForwardLimit),
//       where unused = ComputeUnusedBalanceAsync = entitlement + carry − used − expired + adjusted, and
//       entitlement = ILeaveEntitlementService.ComputeEffectiveEntitlementAsync ProratedEntitlementDays
//       (the ENGINE value — accrual ledger rows are intentionally NOT summed in; see the service's
//       "Accrual realises the engine entitlement; not re-added" comment).
//
// Both end in the SAME LeaveCarryForwardCalculator.Compute(base, limit).ForfeitedDays with the SAME limit,
// so the two ceilings agree iff their bases agree — i.e. iff availableBalance == unused, i.e. iff
//   Σaccruals == ProratedEntitlementDays.
// When they diverge the difference is EXACTLY (ProratedEntitlementDays − Σaccruals). That is a KNOWN,
// ACCEPTED employee-detriment edge (it erodes carried days; it is NOT double-pay — the BUG-079 draw-down
// keeps HR encashment and year-end from ever both paying the same day). The user DEFERRED unifying the two
// derivations (DF-62 clause 3 / DF-62-parity). These arms PIN that behaviour so a future change that makes
// one derivation drift from the other under ALIGNED conditions reds visibly, and the accepted divergence is
// characterized rather than silently altered.
//
// NOTE — why NOT a manual "Adjusted" ledger row as the divergence lever: an Adjusted entry appears in BOTH
// bases (the running BalanceAfter AND ComputeUnusedBalanceAsync's `adjusted`), so it cancels and creates no
// divergence. The ONLY lever is Σaccruals ≠ ProratedEntitlementDays (e.g. a mid-year entitlement change
// applied before re-accrual, or an under/over-accrued ledger) — modelled below by an accrual ledger whose
// sum differs from the stubbed engine entitlement.
//
// PROVIDER: InMemory through the real LeaveCarryForwardService (year-end path via PreviewYearEndAsync) + the
// real LeaveEncashmentService gate (boundary-probed) + the production LeaveCarryForwardCalculator expression.
// No production money math is touched.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-LV-269")]
[Trait("DF", "DF-62-parity")]
public sealed class LeaveForfeitureParityTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _empId = BaseEntity.NewUuidV7();
    private const int LeaveYearN = 2026;
    private const decimal Tolerance = 0.001m;

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "test";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options,
            new MutableTenantContext { TenantId = _tenant });

    private async Task SeedEmployeeAsync()
    {
        using var db = Db();
        db.Employees.Add(new Employee
        {
            Id = _empId, TenantId = _tenant, EmployeeNo = "E1", FirstName = "E", LastName = "One",
            Email = "e1@t.com", DateOfJoining = new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedLeaveTypeAsync(decimal annual, decimal carryLimit)
    {
        using var db = Db();
        var id = BaseEntity.NewUuidV7();
        db.LeaveTypes.Add(new LeaveType
        {
            Id = id, TenantId = _tenant, Name = "Annual Leave", AnnualEntitlement = annual,
            AccrualFrequency = AccrualFrequency.Upfront, CarryForwardLimit = carryLimit,
            CarryForwardExpiryMonths = null, Encashable = true, MaxEncashDays = null,
            NegativeBalanceAllowed = false, Gender = LeaveTypeGender.All, IsActive = true,
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>Seeds an Accrual then a Used ledger row with a correctly chained BalanceAfter running total.</summary>
    private async Task SeedLedgerAsync(Guid leaveTypeId, decimal accrual, decimal used)
    {
        using var db = Db();
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenant, EntryType = LedgerEntryType.Accrual,
            EmployeeId = _empId, LeaveTypeId = leaveTypeId, LeaveYear = LeaveYearN,
            Amount = accrual, BalanceAfter = accrual,
            OccurredAt = new DateTime(LeaveYearN, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenant, EntryType = LedgerEntryType.Used,
            EmployeeId = _empId, LeaveTypeId = leaveTypeId, LeaveYear = LeaveYearN,
            Amount = -used, BalanceAfter = accrual - used,
            OccurredAt = new DateTime(LeaveYearN, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();
    }

    /// <summary>The employee's available balance = latest ledger BalanceAfter (the encashment gate's base).</summary>
    private async Task<decimal> AvailableBalanceAsync(Guid leaveTypeId)
    {
        using var db = Db();
        var last = await db.LeaveLedgerEntries.AsNoTracking()
            .Where(l => l.EmployeeId == _empId && l.LeaveTypeId == leaveTypeId && l.LeaveYear == LeaveYearN)
            .OrderByDescending(l => l.OccurredAt).ThenByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync();
        return last?.BalanceAfter ?? 0m;
    }

    /// <summary>The year-end forfeitable ceiling via the REAL LeaveCarryForwardService preview (its production path).</summary>
    private async Task<decimal> YearEndForfeitableAsync(Guid leaveTypeId, decimal engineEntitlement)
    {
        var db = Db();
        var tc = new MutableTenantContext { TenantId = _tenant };
        var engine = Substitute.For<ILeaveEntitlementService>();
        engine.ComputeEffectiveEntitlementAsync(_empId, leaveTypeId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<EffectiveEntitlementDto>.Success(new EffectiveEntitlementDto
            {
                EmployeeId = _empId, LeaveTypeId = leaveTypeId, LeaveYear = LeaveYearN,
                BaseEntitlementDays = engineEntitlement, ProratedEntitlementDays = engineEntitlement,
                Source = "leave_type_default",
            }));
        var svc = new LeaveCarryForwardService(
            db, tc, engine, NullLogger<LeaveCarryForwardService>.Instance, new TenantLeaveYearResolver(db, tc));
        var preview = await svc.PreviewYearEndAsync(LeaveYearN);
        preview.IsSuccess.Should().BeTrue(preview.Error);
        return preview.Value!.Single(r => r.LeaveTypeId == leaveTypeId).Forfeited;
    }

    /// <summary>The encashment gate's forfeitable ceiling — the exact production expression at
    /// LeaveEncashmentService.cs:105-107, using the same availableBalance the service reads.</summary>
    private static decimal EncashmentCeiling(decimal availableBalance, decimal carryLimit) =>
        LeaveCarryForwardCalculator.Compute(availableBalance, carryLimit).ForfeitedDays;

    private LeaveEncashmentService BuildEncashmentService() =>
        new(Db(), new MutableTenantContext { TenantId = _tenant },
            Substitute.For<IPayrollAdjustmentService>(), NullLogger<LeaveEncashmentService>.Instance);

    /// <summary>Cross-checks the LIVE encashment gate: requesting > ceiling is rejected 422
    /// encashment_exceeds_balance; requesting exactly the ceiling clears the balance gate (it fails later on
    /// no_active_salary, a DIFFERENT code — no salary seeded). Proves the live gate's boundary == the ceiling.</summary>
    private async Task AssertLiveGateBoundaryAsync(Guid leaveTypeId, decimal ceiling)
    {
        var over = await BuildEncashmentService().ProcessAsync(
            new LeaveEncashmentInput(_empId, leaveTypeId, ceiling + 1m, 6, LeaveYearN, IsTaxable: true));
        over.IsFailure.Should().BeTrue();
        over.StatusCode.Should().Be(422);
        over.ErrorCode.Should().Be("encashment_exceeds_balance",
            "requesting more than the forfeitable ceiling is rejected by the live gate");

        var at = await BuildEncashmentService().ProcessAsync(
            new LeaveEncashmentInput(_empId, leaveTypeId, ceiling, 6, LeaveYearN, IsTaxable: true));
        at.ErrorCode.Should().NotBe("encashment_exceeds_balance",
            "requesting exactly the ceiling clears the balance gate (it then stops on no_active_salary)");
    }

    // ── Aligned: Σaccruals == ProratedEntitlementDays -> the two ceilings AGREE (parity guard) ─────────
    [Fact]
    public async Task Aligned_AccrualsEqualEntitlement_EncashmentAndYearEndForfeitable_Match()
    {
        await SeedEmployeeAsync();
        var typeId = await SeedLeaveTypeAsync(annual: 14m, carryLimit: 5m);
        // Accrual 14 == engine entitlement 14; used 2. availableBalance = 12, unused = 14 − 2 = 12. Both bases agree.
        await SeedLedgerAsync(typeId, accrual: 14m, used: 2m);

        var availableBalance = await AvailableBalanceAsync(typeId);
        availableBalance.Should().Be(12m);

        var encashmentCeiling = EncashmentCeiling(availableBalance, carryLimit: 5m);   // Compute(12,5).Forfeited = 7
        var yearEndForfeitable = await YearEndForfeitableAsync(typeId, engineEntitlement: 14m); // Compute(12,5).Forfeited = 7

        encashmentCeiling.Should().BeApproximately(yearEndForfeitable, Tolerance,
            "when Σaccruals == ProratedEntitlementDays the encashment gate and the year-end job forfeit identically");
        yearEndForfeitable.Should().Be(7m);

        // The live encashment gate honours exactly that ceiling.
        await AssertLiveGateBoundaryAsync(typeId, encashmentCeiling);
    }

    // ── Divergent: Σaccruals != ProratedEntitlementDays -> the ceilings DIFFER by exactly the gap ──────
    //    ACCEPTED employee-detriment edge (no double-pay), user-deferred (DF-62 clause 3). Characterized,
    //    NOT "fixed": this test PINS the divergence so a silent change to either derivation is visible.
    [Fact]
    public async Task Divergent_AccrualsBelowEntitlement_CeilingsDiffer_ByExactlyTheAccrualEntitlementGap()
    {
        await SeedEmployeeAsync();
        var typeId = await SeedLeaveTypeAsync(annual: 14m, carryLimit: 5m);
        // Under-accrued ledger: Σaccruals = 10 != engine entitlement 14 (e.g. a mid-year entitlement bump not yet
        // re-accrued). used 2. availableBalance = 8 (encashment base); unused = 14 − 2 = 12 (year-end base).
        const decimal accrual = 10m, engineEntitlement = 14m, used = 2m;
        await SeedLedgerAsync(typeId, accrual: accrual, used: used);

        var availableBalance = await AvailableBalanceAsync(typeId);
        availableBalance.Should().Be(8m);

        var encashmentCeiling = EncashmentCeiling(availableBalance, carryLimit: 5m);          // Compute(8,5).Forfeited = 3
        var yearEndForfeitable = await YearEndForfeitableAsync(typeId, engineEntitlement);     // Compute(12,5).Forfeited = 7

        encashmentCeiling.Should().Be(3m);
        yearEndForfeitable.Should().Be(7m);

        // The two ceilings DIFFER, and the difference is EXACTLY the accrual-vs-entitlement gap.
        Math.Abs(yearEndForfeitable - encashmentCeiling).Should().BeGreaterThan(Tolerance,
            "under-accrual makes the encashment gate (ledger base) and the year-end job (engine base) diverge");
        (yearEndForfeitable - encashmentCeiling).Should().BeApproximately(engineEntitlement - accrual, Tolerance,
            "the divergence is exactly ProratedEntitlementDays − Σaccruals — the ACCEPTED, user-deferred edge " +
            "(employee-detriment only, no double-pay). This arm characterizes it; it is NOT a bug to be fixed here.");

        // The live encashment gate still honours its (lower, ledger-based) ceiling — the accepted behaviour.
        await AssertLiveGateBoundaryAsync(typeId, encashmentCeiling);
    }
}
