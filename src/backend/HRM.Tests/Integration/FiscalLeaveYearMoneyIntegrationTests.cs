// ============================================================================
// ISSUE-313: the two MONEY fiscal-leave-year sites that CAL-8 (#318) routed through
// ITenantLeaveYearResolver but left with ZERO mutation resistance.
//
//   • LeaveEncashmentService.ProcessAsync  (leave-year resolved from the pay PERIOD, not input.PayYear)
//   • RealPayrollFnFIntegration.TriggerFinalSettlementAsync (leave-year resolved from the LWD, not lwd.Year)
//
// Both are believed correct by inspection, and the @integration-enforcer swept all 13 sites. What was missing is
// a guard against the RETURN of the defect: reverting either site to a raw `.Year` left the FULL suite green,
// because every existing arm exercises a CALENDAR tenant (or an Apr-Dec date), where the tenant's leave year IS
// the calendar year and the two forms agree.
//
// The failure mode is SILENT and it is money:
//   • Encashment: a Jan-Mar pay period for an Apr-Mar tenant reads a leave year that has not started yet
//     ⇒ balance 0 ⇒ a valid encashment is REJECTED 422 outright.
//   • F&F: a Jan-Mar last-working-day for an Apr-Mar tenant reads the same empty bucket ⇒ the leave-encashment
//     lines VANISH from the final settlement (under-payment on termination — no error, no zero line).
//
// Each fiscal arm below is the KILLER: it drives the REAL service against a month-4 tenant in the Jan-Mar
// window with the balance credited under the fiscal leave-year LABEL, and injects a REAL TenantLeaveYearResolver
// (NOT the ctor's trailing-optional null fallback, which would silently re-key off the calendar year and hide
// the very read under test). Reverting the site to `input.PayYear` / `lwd.Year` now fails the fiscal arm. The
// calendar control proves the conversion is a no-op for every tenant that never configured a fiscal year.
//
// PROVIDER: EF InMemory through the real service/DbContext (mirrors LeaveEncashmentEligibilityTests +
// FinalSettlementIntegrationTests); the Postgres schema is covered by the migrations CI job.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-LV-264")]
[Trait("Issue", "ISSUE-313")]
public sealed class FiscalLeaveYearMoneyIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    // A Jan-Mar period: for a month-4 tenant this is calendar year 2027 but leave year 2026 — the exact window
    // where a raw `.Year` reads the wrong ledger bucket. For a month-1 (calendar) tenant it is leave year 2027.
    private const int PayYear = 2027;
    private const int PayMonth = 2; // February — inside the Jan-Mar window.
    private static readonly DateTime Lwd = new(2027, 2, 10);

    private const int FiscalLeaveYear = 2026;   // an Apr-Mar tenant's leave year for a Feb-2027 date.
    private const int CalendarLeaveYear = 2027;  // a Jan-Dec tenant's leave year for the same date.

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
    /// Faithful adjustment collaborator (copied from LeaveEncashmentEligibilityTests): flushes the SHARED
    /// DbContext exactly like the production PayrollAdjustmentService, so the encashment's tracked
    /// <see cref="LedgerEntryType.Encashed"/> draw-down persists in the SAME SaveChanges and can be asserted.
    /// </summary>
    private sealed class FakeAdjustmentService : IPayrollAdjustmentService
    {
        private readonly AppDbContext _sharedDb;
        public FakeAdjustmentService(AppDbContext sharedDb) => _sharedDb = sharedDb;

        public async Task<Result<CreatePayrollAdjustmentResult>> CreateAsync(
            CreateAdjustmentInput input, CancellationToken cancellationToken = default)
        {
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

    private AppDbContext Db(Guid tenant) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options,
        new MutableTenantContext { TenantId = tenant });

    /// <summary>Seeds the tenant ROW carrying the FiscalYearStartMonth the resolver has to READ.</summary>
    private void SeedTenant(Guid tenant, int fiscalYearStartMonth)
    {
        using var db = Db(tenant);
        db.Tenants.Add(new Tenant
        {
            Id = tenant, Subdomain = $"t{fiscalYearStartMonth}", Name = "T",
            Status = TenantStatus.Active, FiscalYearStartMonth = fiscalYearStartMonth,
        });
        db.SaveChanges();
    }

    /// <summary>Employee + BASIC salary (22000/mo) + Mon-Fri default shift, so the daily-rate math produces a
    /// non-zero encashment amount (the exact figure depends on the month's working days and is not asserted).</summary>
    private void SeedEmployeeWithBasic(Guid tenant, Guid empId, decimal monthlyBasic = 22_000m)
    {
        using var db = Db(tenant);
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = tenant, EmployeeNo = "E1", FirstName = "E", LastName = "One",
            Email = "e1@t.com", DateOfJoining = new DateTime(2019, 1, 1),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });
        var componentId = BaseEntity.NewUuidV7();
        db.SalaryComponents.Add(new SalaryComponent
        {
            Id = componentId, TenantId = tenant, Name = "Basic Salary", Code = "BASIC",
            Type = SalaryComponentType.Earning, CalculationMethod = CalculationMethod.Fixed,
            IsActive = true, ProcessingOrder = 1,
        });
        db.EmployeeSalaryComponents.Add(new EmployeeSalaryComponent
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenant, EmployeeId = empId,
            SalaryStructureId = BaseEntity.NewUuidV7(), SalaryComponentId = componentId,
            AnnualAmount = monthlyBasic * 12m, MonthlyAmount = monthlyBasic,
            EffectiveFrom = new DateOnly(2019, 1, 1), EffectiveTo = null,
        });
        db.Shifts.Add(new Shift
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenant, Name = "Standard Mon-Fri",
            Type = ShiftType.Single, WorkingDays = [1, 2, 3, 4, 5], IsDefault = true, IsActive = true,
        });
        db.SaveChanges();
    }

    /// <summary>An encashable Annual-leave type + a ledger balance credited under an explicit leave-year LABEL —
    /// i.e. exactly what the (already-converted) accrual credit side writes for this tenant.</summary>
    private void SeedEncashableLeave(
        Guid tenant, Guid empId, Guid leaveTypeId, int leaveYear, decimal balance,
        decimal? carryForwardLimit = 5m, decimal? maxEncashDays = 10m)
    {
        using var db = Db(tenant);
        db.LeaveTypes.Add(new LeaveType
        {
            Id = leaveTypeId, TenantId = tenant, Name = "Annual", Encashable = true, IsActive = true,
            AnnualEntitlement = 20m, AccrualFrequency = AccrualFrequency.Upfront,
            CarryForwardLimit = carryForwardLimit, MaxEncashDays = maxEncashDays,
            Gender = LeaveTypeGender.All,
        });
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenant, EntryType = LedgerEntryType.Accrual,
            EmployeeId = empId, LeaveTypeId = leaveTypeId, LeaveYear = leaveYear,
            Amount = balance, BalanceAfter = balance,
            OccurredAt = new DateTime(leaveYear, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        db.SaveChanges();
    }

    // ── Site 1: LeaveEncashmentService — leave year from the pay PERIOD, not input.PayYear ──

    /// <summary>
    /// KILLER for <c>LeaveEncashmentService.cs:84-87</c>. Month-4 tenant, balance credited under fiscal leave
    /// year 2026, a Feb-2027 pay period. Correct code resolves leave year 2026 (balance 20 ⇒ forfeitable 15) and
    /// accepts a 5-day encashment, drawing the ledger down under 2026. Reverting the site to <c>input.PayYear</c>
    /// resolves 2027 ⇒ balance 0 ⇒ forfeitable 0 ⇒ the request is REJECTED 422 <c>encashment_exceeds_balance</c>.
    /// </summary>
    [Fact]
    public async Task Encashment_FiscalTenant_ReadsThePayPeriodsLeaveYear_NotThePayYear()
    {
        var tenant = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        SeedTenant(tenant, fiscalYearStartMonth: 4);
        SeedEmployeeWithBasic(tenant, empId);
        SeedEncashableLeave(tenant, empId, typeId, leaveYear: FiscalLeaveYear, balance: 20m);

        using var db = Db(tenant);
        var tenantContext = new MutableTenantContext { TenantId = tenant };
        var service = new LeaveEncashmentService(
            db, tenantContext, new FakeAdjustmentService(db),
            NullLogger<LeaveEncashmentService>.Instance,
            leaveYearResolver: new TenantLeaveYearResolver(db, tenantContext));

        var result = await service.ProcessAsync(
            new LeaveEncashmentInput(empId, typeId, EligibleDays: 5m, PayMonth, PayYear, IsTaxable: true));

        result.IsSuccess.Should().BeTrue(
            result.Error + " — a raw input.PayYear reads leave year 2027 (empty), rejecting a valid "
            + "encashment 422 for a fiscal tenant in the Jan-Mar window");
        result.Value!.EncashedDays.Should().Be(5m);

        // The draw-down must land in the SAME (fiscal) bucket the accrual credited — leave year 2026, not 2027 —
        // and reduce THAT bucket's balance (20 credited − 5 encashed = 15), not a wrong-year zero balance.
        var drawDown = await db.LeaveLedgerEntries
            .SingleAsync(l => l.EmployeeId == empId && l.EntryType == LedgerEntryType.Encashed);
        drawDown.LeaveYear.Should().Be(FiscalLeaveYear, "the encashment draws down the fiscal leave year it read");
        drawDown.Amount.Should().Be(-5m, "5 days encashed");
        drawDown.BalanceAfter.Should().Be(15m, "20 credited under leave year 2026 − 5 encashed = 15");
    }

    /// <summary>
    /// CONTROL: the identical flow on a CALENDAR (month-1) tenant is unchanged — the leave year IS the pay year,
    /// so encashment succeeds exactly as it did before ISSUE-305. Proves the conversion is additive.
    /// </summary>
    [Fact]
    public async Task Encashment_CalendarTenant_IsUnchanged()
    {
        var tenant = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        SeedTenant(tenant, fiscalYearStartMonth: 1);
        SeedEmployeeWithBasic(tenant, empId);
        SeedEncashableLeave(tenant, empId, typeId, leaveYear: CalendarLeaveYear, balance: 20m);

        using var db = Db(tenant);
        var tenantContext = new MutableTenantContext { TenantId = tenant };
        var service = new LeaveEncashmentService(
            db, tenantContext, new FakeAdjustmentService(db),
            NullLogger<LeaveEncashmentService>.Instance,
            leaveYearResolver: new TenantLeaveYearResolver(db, tenantContext));

        var result = await service.ProcessAsync(
            new LeaveEncashmentInput(empId, typeId, EligibleDays: 5m, PayMonth, PayYear, IsTaxable: true));

        result.IsSuccess.Should().BeTrue(result.Error);
        var drawDown = await db.LeaveLedgerEntries
            .SingleAsync(l => l.EmployeeId == empId && l.EntryType == LedgerEntryType.Encashed);
        drawDown.LeaveYear.Should().Be(CalendarLeaveYear, "for a January tenant the leave year is the pay year");
    }

    // ── Site 2: RealPayrollFnFIntegration — leave year from the LWD, not lwd.Year ──

    /// <summary>
    /// KILLER for <c>RealPayrollFnFIntegration.cs:239-241</c>. Month-4 tenant, balance credited under fiscal
    /// leave year 2026, a leaver with a Feb-2027 last-working-day. Correct code resolves leave year 2026, finds
    /// the balance, and emits a leave-encashment line. Reverting the site to <c>lwd.Year</c> resolves 2027 ⇒
    /// balance 0 ⇒ the encashment line SILENTLY VANISHES from the settlement (under-payment on termination).
    /// </summary>
    [Fact]
    public async Task FnF_FiscalTenant_ReadsTheLwdsLeaveYear_NotTheLwdCalendarYear()
    {
        var tenant = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        SeedTenant(tenant, fiscalYearStartMonth: 4);
        SeedEmployeeWithBasic(tenant, empId);
        SeedEncashableLeave(tenant, empId, typeId, leaveYear: FiscalLeaveYear, balance: 20m);

        var settlement = await TriggerFnFAsync(tenant, empId);

        settlement.LeaveEncashmentTotal.Should().BeGreaterThan(
            0m,
            "the terminating employee's balance sits in fiscal leave year 2026; a raw lwd.Year reads 2027 "
            + "(empty) and the encashment silently drops out of the settlement — an under-payment on exit");
        // Pin the DAY COUNT (deterministic: forfeitable 20−5 = 15, capped at MaxEncashDays 10) via the line
        // label, so the arm can't stay green-but-wrong on a seeding drift. The money AMOUNT is left unpinned
        // because it scales with the LWD month's working days (dailyRate = basic / working-days).
        settlement.Lines.Should().Contain(
            l => l.Type == FinalSettlementLineType.Encashment && l.Label.Contains("10 day(s)"),
            "10 forfeitable days (from the fiscal-2026 balance) must be encashed into the settlement");
    }

    /// <summary>
    /// CONTROL: the identical F&amp;F on a CALENDAR (month-1) tenant is unchanged — the leave year is the LWD's
    /// calendar year, so the encashment line is present exactly as before ISSUE-305.
    /// </summary>
    [Fact]
    public async Task FnF_CalendarTenant_IsUnchanged()
    {
        var tenant = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        SeedTenant(tenant, fiscalYearStartMonth: 1);
        SeedEmployeeWithBasic(tenant, empId);
        SeedEncashableLeave(tenant, empId, typeId, leaveYear: CalendarLeaveYear, balance: 20m);

        var settlement = await TriggerFnFAsync(tenant, empId);

        settlement.LeaveEncashmentTotal.Should().BeGreaterThan(
            0m, "for a January tenant the LWD's calendar year IS the leave year — encashment is unchanged");
        settlement.Lines.Should().Contain(l => l.Type == FinalSettlementLineType.Encashment);
    }

    /// <summary>Drives the REAL <see cref="RealPayrollFnFIntegration"/> with a real
    /// <see cref="TenantLeaveYearResolver"/> injected, then reads back the persisted settlement + its lines. The
    /// statutory resolver is real but never invoked (no location/country ⇒ statutory is skipped).</summary>
    private async Task<FinalSettlement> TriggerFnFAsync(Guid tenant, Guid empId)
    {
        Guid settlementId;
        using (var db = Db(tenant))
        {
            var tenantContext = new MutableTenantContext { TenantId = tenant };
            var service = new RealPayrollFnFIntegration(
                db, tenantContext,
                new StatutoryDeductionResolver(db, tenantContext, NullLogger<StatutoryDeductionResolver>.Instance),
                NullLogger<RealPayrollFnFIntegration>.Instance,
                leaveYearResolver: new TenantLeaveYearResolver(db, tenantContext));
            settlementId = await service.TriggerFinalSettlementAsync(tenant, empId, Guid.NewGuid(), Lwd);
        }
        using var read = Db(tenant);
        return await read.FinalSettlements.AsNoTracking().Include(s => s.Lines)
            .FirstAsync(s => s.Id == settlementId);
    }
}
