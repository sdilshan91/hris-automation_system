// ============================================================================
// BUG-079: leave-encashment must enforce a BR-6 balance/carry-forward eligibility gate AND draw down the
// leave ledger so the same days cannot be paid twice (HR encashment + year-end auto-encashment).
//
// The gate (only when a LeaveType is supplied): the requested days may not exceed the FORFEITABLE balance —
// the portion of the current ledger balance that lies OVER the type's CarryForwardLimit, computed with the
// SAME LeaveCarryForwardCalculator the year-end job uses. Accepted days are then capped at MaxEncashDays.
// On acceptance an `Encashed` ledger draw-down is written in the same unit of work as the payroll adjustment.
//
// PROVIDER: InMemory through the real LeaveEncashmentService + AppDbContext (money-critical → concrete balances
// asserted). The adjustment collaborator is faked; its CreateAsync flushes the SHARED DbContext exactly like the
// production PayrollAdjustmentService (line 142), so the tracked draw-down persists atomically with it.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class LeaveEncashmentEligibilityTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenant = Guid.NewGuid();
    private const int PayYear = 2025;
    private const int PayMonth = 9; // September 2025: Mon-Fri shift => 22 working days => 22000/22 = 1000/day.

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

    /// <summary>
    /// Faithful adjustment collaborator: captures the created input AND flushes the SHARED DbContext (like the
    /// real PayrollAdjustmentService.CreateAsync), so the encashment's tracked ledger draw-down is persisted in
    /// the SAME SaveChanges — proving atomicity. When failWithoutSaving is set it returns a failure and does NOT
    /// save (the "adjustment rejected => draw-down not persisted" arm).
    /// </summary>
    private sealed class FakeAdjustmentService : IPayrollAdjustmentService
    {
        private readonly AppDbContext _sharedDb;
        private readonly bool _failWithoutSaving;
        public CreateAdjustmentInput? Captured { get; private set; }
        public AppDbContext SharedDb => _sharedDb; // exposed so a test can flush the shared context after a failure.

        public FakeAdjustmentService(AppDbContext sharedDb, bool failWithoutSaving = false)
        {
            _sharedDb = sharedDb;
            _failWithoutSaving = failWithoutSaving;
        }

        public async Task<Result<CreatePayrollAdjustmentResult>> CreateAsync(
            CreateAdjustmentInput input, CancellationToken cancellationToken = default)
        {
            Captured = input;
            if (_failWithoutSaving)
                return Result<CreatePayrollAdjustmentResult>.Failure("Simulated adjustment failure.", 400, "sim_fail");

            // Mirror production: the adjustment service's SaveChanges on the shared scoped context flushes the
            // encashment service's tracked draw-down too (one unit of work).
            await _sharedDb.SaveChangesAsync(cancellationToken);

            var dto = new PayrollAdjustmentDto(
                Id: Guid.NewGuid(), EmployeeId: input.EmployeeId, EmployeeNo: "E1", EmployeeName: "E One",
                AdjustmentType: input.AdjustmentType, Amount: input.Amount, Description: input.Description,
                ApplicablePayMonth: input.ApplicablePayMonth, ApplicablePayYear: input.ApplicablePayYear,
                IsTaxable: input.IsTaxable, IsRecurring: false, RecurrenceEndMonth: null, RecurrenceEndYear: null,
                Status: "Pending", AppliedInPayrollRunId: null, ReferencePayrollSlipId: null,
                HasSupportingDocument: false, RecurringSeriesId: null, CreatedAt: DateTime.UtcNow,
                NegativeNetWarning: false);
            return Result<CreatePayrollAdjustmentResult>.Success(
                new CreatePayrollAdjustmentResult(dto, 0, false, null, null));
        }

        public Task<Result> CancelAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<PayrollAdjustmentPageDto>> ListAsync(AdjustmentListFilter f, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<PayrollAdjustmentDto>> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<BulkAdjustmentResultDto>> BulkCreateAsync(int m, int y, Stream s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<string>> UploadDocumentAsync(Guid id, Stream c, string fn, string ct2, long len, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<AdjustmentDocumentDto>> DownloadDocumentAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private AppDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, new MutableTenantContext { TenantId = _tenant });
    }

    /// <summary>Seeds employee + BASIC salary (22000/mo) + tenant Mon-Fri shift so the daily-rate math reaches
    /// the draw-down/adjustment write (22000/22 = 1000/day in September 2025).</summary>
    private async Task<Guid> SeedEmployee(decimal monthlyBasic = 22_000m)
    {
        using var db = Db();
        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = _tenant, EmployeeNo = "E1", FirstName = "E", LastName = "One",
            Email = "e1@t.com", DateOfJoining = new DateTime(2019, 1, 1),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });

        var componentId = BaseEntity.NewUuidV7();
        db.SalaryComponents.Add(new SalaryComponent
        {
            Id = componentId, TenantId = _tenant, Name = "Basic Salary", Code = "BASIC",
            Type = SalaryComponentType.Earning, CalculationMethod = CalculationMethod.Fixed,
            IsActive = true, ProcessingOrder = 1,
        });
        db.EmployeeSalaryComponents.Add(new EmployeeSalaryComponent
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenant, EmployeeId = empId,
            SalaryStructureId = BaseEntity.NewUuidV7(), SalaryComponentId = componentId,
            AnnualAmount = monthlyBasic * 12m, MonthlyAmount = monthlyBasic,
            EffectiveFrom = new DateOnly(2019, 1, 1), EffectiveTo = null,
        });
        db.Shifts.Add(new Shift
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenant, Name = "Standard Mon-Fri",
            Type = ShiftType.Single, WorkingDays = new List<int> { 1, 2, 3, 4, 5 },
            IsDefault = true, IsActive = true,
        });

        await db.SaveChangesAsync();
        return empId;
    }

    private async Task<Guid> SeedLeaveType(
        decimal annual, decimal? carryForwardLimit, bool encashable = true, decimal? maxEncashDays = null)
    {
        using var db = Db();
        var id = BaseEntity.NewUuidV7();
        db.LeaveTypes.Add(new LeaveType
        {
            Id = id, TenantId = _tenant, Name = "Annual Leave", AnnualEntitlement = annual,
            AccrualFrequency = AccrualFrequency.Upfront, CarryForwardLimit = carryForwardLimit,
            CarryForwardExpiryMonths = null, Encashable = encashable, MaxEncashDays = maxEncashDays,
            NegativeBalanceAllowed = false, Gender = LeaveTypeGender.All, IsActive = true,
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>Seeds a starting ledger entry establishing the employee's available balance for the pay year.</summary>
    private async Task SeedLedgerBalance(Guid empId, Guid leaveTypeId, decimal balance)
    {
        using var db = Db();
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenant, EntryType = LedgerEntryType.Accrual,
            EmployeeId = empId, LeaveTypeId = leaveTypeId, LeaveYear = PayYear,
            Amount = balance, BalanceAfter = balance,
            OccurredAt = new DateTime(PayYear, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<LeaveLedger>> LedgerFor(Guid empId, Guid leaveTypeId, int year)
    {
        using var db = Db();
        return await db.LeaveLedgerEntries
            .Where(l => l.EmployeeId == empId && l.LeaveTypeId == leaveTypeId && l.LeaveYear == year)
            .ToListAsync();
    }

    private (LeaveEncashmentService service, FakeAdjustmentService adjustments) BuildService(
        bool failAdjustment = false)
    {
        var db = Db();
        var adjustments = new FakeAdjustmentService(db, failWithoutSaving: failAdjustment);
        var service = new LeaveEncashmentService(
            db, new MutableTenantContext { TenantId = _tenant }, adjustments,
            NullLogger<LeaveEncashmentService>.Instance);
        return (service, adjustments);
    }

    // ── Test 1: within the forfeitable ceiling => success + adjustment amount + Encashed draw-down ──

    [Fact]
    [Trait("TC", "TC-PAY-010-09")]
    [Trait("Bug", "BUG-079")]
    public async Task Encash_WithinForfeitableCeiling_Succeeds_AndDrawsDownLedger()
    {
        var empId = await SeedEmployee();
        var typeId = await SeedLeaveType(annual: 12m, carryForwardLimit: 5m, encashable: true, maxEncashDays: 10m);
        await SeedLedgerBalance(empId, typeId, balance: 10m); // forfeitable = 10 - 5 = 5.

        var (service, adjustments) = BuildService();
        var result = await service.ProcessAsync(
            new LeaveEncashmentInput(empId, typeId, EligibleDays: 5m, PayMonth, PayYear, IsTaxable: true));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.EncashedDays.Should().Be(5m);
        result.Value.DailyRate.Should().Be(1_000m);         // 22000 / 22 shift working-days.
        result.Value.Amount.Should().Be(5_000m);            // 5 * 1000.
        adjustments.Captured!.Amount.Should().Be(5_000m);   // amount reached the payroll adjustment.

        var encashed = (await LedgerFor(empId, typeId, PayYear))
            .Single(l => l.EntryType == LedgerEntryType.Encashed);
        encashed.Amount.Should().Be(-5m);                   // ledger drawn down by the encashed days.
        encashed.BalanceAfter.Should().Be(5m);              // 10 prior - 5 encashed = 5.
    }

    // ── Test 2: more than the available forfeitable balance => 422, no draw-down (fails on pre-fix code) ──

    [Fact]
    [Trait("TC", "TC-PAY-010-09")]
    [Trait("Bug", "BUG-079")]
    public async Task Encash_MoreThanForfeitableBalance_Rejected422_NoDrawDown()
    {
        var empId = await SeedEmployee();
        var typeId = await SeedLeaveType(annual: 12m, carryForwardLimit: 5m, encashable: true, maxEncashDays: 10m);
        await SeedLedgerBalance(empId, typeId, balance: 10m); // forfeitable ceiling = 5.

        var (service, _) = BuildService();
        var result = await service.ProcessAsync(
            new LeaveEncashmentInput(empId, typeId, EligibleDays: 6m, PayMonth, PayYear, IsTaxable: true));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("encashment_exceeds_balance");

        (await LedgerFor(empId, typeId, PayYear))
            .Should().NotContain(l => l.EntryType == LedgerEntryType.Encashed); // nothing drawn down.
    }

    // ── Test 3: carry-forward interaction — balance 10, limit 5 => ceiling 5; 6 rejected, 5 accepted ──

    [Fact]
    [Trait("TC", "TC-PAY-010-09")]
    [Trait("Bug", "BUG-079")]
    public async Task Encash_CarryForwardCeiling_RejectsOverLimit_AcceptsAtLimit()
    {
        var empId = await SeedEmployee();
        var typeId = await SeedLeaveType(annual: 12m, carryForwardLimit: 5m, encashable: true, maxEncashDays: null);
        await SeedLedgerBalance(empId, typeId, balance: 10m); // 10 - 5 limit = 5 forfeitable.

        // Requesting 6 (> forfeitable 5) is rejected.
        var (rejectSvc, _) = BuildService();
        var over = await rejectSvc.ProcessAsync(
            new LeaveEncashmentInput(empId, typeId, EligibleDays: 6m, PayMonth, PayYear, IsTaxable: true));
        over.IsFailure.Should().BeTrue();
        over.ErrorCode.Should().Be("encashment_exceeds_balance");

        // Requesting 5 (== forfeitable ceiling) is accepted and draws the ledger down to the carry-forward limit.
        var (okSvc, _) = BuildService();
        var ok = await okSvc.ProcessAsync(
            new LeaveEncashmentInput(empId, typeId, EligibleDays: 5m, PayMonth, PayYear, IsTaxable: true));
        ok.IsSuccess.Should().BeTrue(ok.Error);

        var encashed = (await LedgerFor(empId, typeId, PayYear)).Single(l => l.EntryType == LedgerEntryType.Encashed);
        encashed.Amount.Should().Be(-5m);
        encashed.BalanceAfter.Should().Be(5m); // reduced to the carry-forward limit; nothing left to forfeit.
    }

    // ── Test 4: double-pay prevention — after HR draw-down the year-end job encashes only the RESIDUAL ──

    [Fact]
    [Trait("TC", "TC-PAY-010-09")]
    [Trait("Bug", "BUG-079")]
    public async Task Encash_ThenYearEnd_DoesNotDoublePayTheSameDays()
    {
        var empId = await SeedEmployee();
        var typeId = await SeedLeaveType(annual: 12m, carryForwardLimit: 5m, encashable: true, maxEncashDays: null);
        await SeedLedgerBalance(empId, typeId, balance: 12m); // forfeitable-over-limit = 12 - 5 = 7 total.

        // HR encashes 6 of the 7 forfeitable days (draws the ledger down to 6).
        var (service, _) = BuildService();
        var hr = await service.ProcessAsync(
            new LeaveEncashmentInput(empId, typeId, EligibleDays: 6m, PayMonth, PayYear, IsTaxable: true));
        hr.IsSuccess.Should().BeTrue(hr.Error);
        (await LedgerFor(empId, typeId, PayYear))
            .Single(l => l.EntryType == LedgerEntryType.Encashed).Amount.Should().Be(-6m);

        // Now run the year-end auto-encashment for the same year. It recomputes forfeitable from the CURRENT
        // ledger (which folds the HR Encashed -6 into consumption): unused = 12 entitlement - 6 encashed = 6,
        // Compute(6, limit 5) => forfeited 1. So it encashes only the residual 1 day, NOT another 7.
        var entitlement = Substitute.For<ILeaveEntitlementService>();
        entitlement.ComputeEffectiveEntitlementAsync(empId, typeId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<EffectiveEntitlementDto>.Success(new EffectiveEntitlementDto
            {
                EmployeeId = empId, LeaveTypeId = typeId, LeaveYear = PayYear,
                BaseEntitlementDays = 12m, ProratedEntitlementDays = 12m, Source = "leave_type_default",
            }));
        var yearEnd = new LeaveCarryForwardService(
            Db(), new MutableTenantContext { TenantId = _tenant }, entitlement,
            NullLogger<LeaveCarryForwardService>.Instance);
        var yeResult = await yearEnd.ProcessYearEndAsync(PayYear);
        yeResult.IsSuccess.Should().BeTrue();

        var encashedEntries = (await LedgerFor(empId, typeId, PayYear))
            .Where(l => l.EntryType == LedgerEntryType.Encashed)
            .ToList();
        encashedEntries.Should().HaveCount(2);
        encashedEntries.Select(l => l.Amount).Should().BeEquivalentTo(new[] { -6m, -1m });
        // HR paid 6; the year-end job saw the REDUCED balance and encashed only the residual 1 — not another 7.
        encashedEntries.Should().Contain(l => l.Amount == -1m);

        // Total encashed across both paths = the original 7 forfeitable days, never more (no double-pay).
        encashedEntries.Sum(l => l.Amount).Should().Be(-7m);
    }

    // ── Test 4b: MaxEncashDays cap fires — paid days AND the ledger draw-down are BOTH capped below the ceiling ──

    [Fact]
    [Trait("TC", "TC-PAY-010-09")]
    [Trait("Bug", "BUG-079")]
    public async Task Encash_WithinCeiling_ButOverMaxEncashDays_CapsBothPaymentAndDrawDown()
    {
        var empId = await SeedEmployee();
        // Forfeitable ceiling = 10 - 5 = 5, but the type caps encashment at 3 days.
        var typeId = await SeedLeaveType(annual: 12m, carryForwardLimit: 5m, encashable: true, maxEncashDays: 3m);
        await SeedLedgerBalance(empId, typeId, balance: 10m);

        var (service, adjustments) = BuildService();
        // Request 5 (== ceiling, so the balance gate passes) but MaxEncashDays 3 must cap the PAID days to 3.
        var result = await service.ProcessAsync(
            new LeaveEncashmentInput(empId, typeId, EligibleDays: 5m, PayMonth, PayYear, IsTaxable: true));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.EncashedDays.Should().Be(3m);            // capped at MaxEncashDays, not the requested 5.
        result.Value.Amount.Should().Be(3_000m);               // 3 * 1000, not 5000.
        adjustments.Captured!.Amount.Should().Be(3_000m);

        // The draw-down must use the CAPPED days too (not the raw request) — else the ledger would over-deduct.
        var encashed = (await LedgerFor(empId, typeId, PayYear)).Single(l => l.EntryType == LedgerEntryType.Encashed);
        encashed.Amount.Should().Be(-3m);
        encashed.BalanceAfter.Should().Be(7m);                 // 10 - 3, not 10 - 5.
    }

    // ── Test 5: LeaveTypeId null (HR manual override) => gate skipped, no draw-down (existing behaviour) ──

    [Fact]
    [Trait("TC", "TC-PAY-010-09")]
    [Trait("Bug", "BUG-079")]
    public async Task Encash_NullLeaveType_SkipsGateAndDrawDown()
    {
        var empId = await SeedEmployee();

        var (service, adjustments) = BuildService();
        var result = await service.ProcessAsync(
            new LeaveEncashmentInput(empId, LeaveTypeId: null, EligibleDays: 5m, PayMonth, PayYear, IsTaxable: true));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Amount.Should().Be(5_000m);
        adjustments.Captured!.Amount.Should().Be(5_000m);

        // No leave type => no ledger to draw against; the manual-override branch writes no Encashed entry.
        using var db = Db();
        (await db.LeaveLedgerEntries.AnyAsync(l => l.EntryType == LedgerEntryType.Encashed)).Should().BeFalse();
    }

    // ── Atomicity: adjustment rejected => the tracked draw-down is NOT persisted (both-or-neither) ──

    [Fact]
    [Trait("TC", "TC-PAY-010-09")]
    [Trait("Bug", "BUG-079")]
    public async Task Encash_AdjustmentFails_DrawDownNotPersisted()
    {
        var empId = await SeedEmployee();
        var typeId = await SeedLeaveType(annual: 12m, carryForwardLimit: 5m, encashable: true, maxEncashDays: null);
        await SeedLedgerBalance(empId, typeId, balance: 10m);

        var (service, adjustments) = BuildService(failAdjustment: true);
        var result = await service.ProcessAsync(
            new LeaveEncashmentInput(empId, typeId, EligibleDays: 5m, PayMonth, PayYear, IsTaxable: true));

        result.IsFailure.Should().BeTrue();

        // Force an UNRELATED flush of the same shared context the service tracked the draw-down on. This exercises
        // the impl's detach-on-failure: if the draw-down were left tracked (Added), this SaveChanges would persist
        // the orphan. It must not.
        await adjustments.SharedDb.SaveChangesAsync();

        (await LedgerFor(empId, typeId, PayYear))
            .Should().NotContain(l => l.EntryType == LedgerEntryType.Encashed);
    }
}
