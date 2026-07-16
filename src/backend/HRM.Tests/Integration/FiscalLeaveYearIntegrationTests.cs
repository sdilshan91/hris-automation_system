// ============================================================================
// CAL-8 / ISSUE-305: an Apr-Mar tenant's leave year, end to end.
//
// The unit arms (LeaveYearTests, ProcessLeaveYearEndJobWindowTests) pin the ARITHMETIC. They cannot catch the
// failure that actually shipped: `Tenant.FiscalYearStartMonth` existed, was settable in the org-profile UI,
// and was read by NOTHING — every unit test of a pure function would still have passed. So these arms drive
// the real service against a real tenant ROW and assert the persisted result moved.
//
// Both tenants are seeded IDENTICALLY except for FiscalYearStartMonth. Any difference in the output is
// therefore attributable to that column alone — which is the whole claim of this change.
//
// Provider note (matches LeaveCarryForwardIntegrationTests): EF InMemory through the real DbContext/service;
// PostgreSQL schema is covered by the migrations CI job.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Leave;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using HRM.Application.Features.LeaveRequests.DTOs;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class FiscalLeaveYearIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    // Identical in every respect EXCEPT FiscalYearStartMonth.
    private readonly Guid _calendarTenant = Guid.NewGuid();
    private readonly Guid _fiscalTenant = Guid.NewGuid();

    private Guid _calLeaveType, _calEmployee, _fisLeaveType, _fisEmployee;

    private const int FromYear = 2026;
    private const int ToYear = 2027;
    private const int ExpiryMonths = 3;

    public FiscalLeaveYearIntegrationTests()
    {
        _calLeaveType = Guid.NewGuid(); _calEmployee = Guid.NewGuid();
        _fisLeaveType = Guid.NewGuid(); _fisEmployee = Guid.NewGuid();

        Seed(_calendarTenant, fiscalYearStartMonth: 1, _calLeaveType, _calEmployee, "CAL-1");
        Seed(_fiscalTenant, fiscalYearStartMonth: 4, _fisLeaveType, _fisEmployee, "FIS-1");
    }

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

    private ServiceProvider BuildProvider(Guid tenantId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.Email.Returns("hr@test.com");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(new MutableTenantContext { TenantId = tenantId });
        services.AddSingleton(currentUser);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        // ISSUE-305: the real resolver, mirroring DependencyInjection.AddInfrastructure. This fixture seeds
        // BOTH a calendar (month 1) and a fiscal (month 4) tenant row, so the resolver returns a genuinely
        // different month per tenant — that difference is what every arm here is measuring.
        services.AddScoped<ITenantLeaveYearResolver, TenantLeaveYearResolver>();
        services.AddScoped<ILeaveEntitlementService, LeaveEntitlementService>();
        services.AddScoped<ILeaveCarryForwardService, LeaveCarryForwardService>();
        return services.BuildServiceProvider();
    }

    private AppDbContext Db(Guid tenant) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options,
        new MutableTenantContext { TenantId = tenant });

    private void Seed(Guid tenant, int fiscalYearStartMonth, Guid leaveType, Guid employee, string empNo)
    {
        using var db = Db(tenant);

        // The Tenant ROW is the point of the whole test: the column has to be READ, not merely exist.
        db.Tenants.Add(new Tenant
        {
            Id = tenant,
            Subdomain = empNo.ToLowerInvariant(),
            Name = empNo,
            Status = TenantStatus.Active,
            FiscalYearStartMonth = fiscalYearStartMonth,
        });

        db.LeaveTypes.Add(new LeaveType
        {
            Id = leaveType, TenantId = tenant, Name = "Annual Leave",
            AnnualEntitlement = 14m, AccrualFrequency = AccrualFrequency.Upfront,
            CarryForwardLimit = 5m, CarryForwardExpiryMonths = ExpiryMonths,
            Gender = LeaveTypeGender.All, IsActive = true,
        });

        db.Employees.Add(new Employee
        {
            Id = employee, TenantId = tenant, UserId = Guid.NewGuid(),
            EmployeeNo = empNo, FirstName = "Emp", LastName = empNo, Email = $"{empNo}@x.com",
            DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });

        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenant,
            EntryType = LedgerEntryType.Used, EmployeeId = employee, LeaveTypeId = leaveType,
            LeaveYear = FromYear, Amount = -6m, BalanceAfter = 8m,
            OccurredAt = new DateTime(FromYear, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        db.SaveChanges();
    }

    private async Task<LeaveCarryForwardTracking> RunYearEndAndGetTracking(Guid tenant, Guid employee)
    {
        using var provider = BuildProvider(tenant);
        var result = await provider.GetRequiredService<ILeaveCarryForwardService>().ProcessYearEndAsync(FromYear);
        result.IsSuccess.Should().BeTrue();

        using var db = Db(tenant);
        return await db.LeaveCarryForwardTrackings.SingleAsync(t => t.EmployeeId == employee);
    }

    /// <summary>
    /// Credits a ledger balance under an explicit leave-year LABEL — i.e. exactly what the (already-converted)
    /// accrual/carry-forward CREDIT side writes for this tenant. The debit-side arms then assert a real service
    /// can find it.
    /// </summary>
    private void SeedLedgerBalance(Guid tenant, Guid employee, Guid leaveType, int leaveYear, decimal balance)
    {
        using var db = Db(tenant);
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenant,
            EntryType = LedgerEntryType.Accrual, EmployeeId = employee, LeaveTypeId = leaveType,
            LeaveYear = leaveYear, Amount = balance, BalanceAfter = balance,
            OccurredAt = new DateTime(leaveYear, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        db.SaveChanges();
    }

    /// <summary>
    /// Drives the REAL <see cref="LeaveRequestService"/> — a ledger DEBIT consumer — through its balance
    /// preview, which resolves the leave-year label and then reads the ledger by it
    /// (<c>LeaveYearForAsync</c> -> <c>GetLedgerBalanceAsync</c>). This is the production path a leave request
    /// takes; nothing here re-implements the label.
    /// </summary>
    private async Task<LeaveBalancePreviewDto> GetBalancePreviewAsync(
        Guid tenant, Guid employee, Guid leaveType, DateOnly startDate)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenant };

        using var db = Db(tenant);
        var userId = (await db.Employees.Where(e => e.Id == employee).Select(e => e.UserId).SingleAsync())!.Value;

        // The acting employee is resolved via Employee.UserId == ICurrentUser.UserId.
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var service = new LeaveRequestService(
            db, tenantContext, currentUser,
            new NoOpHolidayProvider(),
            new LogOnlyLeaveNotificationService(NullLogger<LogOnlyLeaveNotificationService>.Instance),
            NullLogger<LeaveRequestService>.Instance,
            leaveYearResolver: new TenantLeaveYearResolver(db, tenantContext));

        var result = await service.GetBalancePreviewAsync(leaveType, startDate, startDate, isHalfDay: false);
        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value!;
    }

    /// <summary>
    /// TC-LV-264 (ISSUE-305), the headline arm: carried days expire 3 months after the tenant's OWN leave year
    /// opens. The Apr-Mar tenant must expire on 2027-07-01 (1 Apr + 3mo), NOT 2027-04-01 (1 Jan + 3mo) — a
    /// three-month error in when real employees lose real leave days.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public async Task FiscalTenant_CarryForwardExpiresFromTheirOwn1April()
    {
        var tracking = await RunYearEndAndGetTracking(_fiscalTenant, _fisEmployee);

        tracking.ExpiryDate.Should().Be(
            new DateOnly(2027, 7, 1),
            "leave year 2027 opens 2027-04-01 for an Apr-Mar tenant, +3 months = 2027-07-01");
    }

    /// <summary>
    /// CONTROL: the calendar tenant is unchanged — 2027-01-01 + 3 months = 2027-04-01, exactly what shipped
    /// before ISSUE-305. This arm is what proves the fix is additive rather than a shifted-for-everyone bug.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public async Task CalendarTenant_CarryForwardExpiryIsUnchanged()
    {
        var tracking = await RunYearEndAndGetTracking(_calendarTenant, _calEmployee);

        tracking.ExpiryDate.Should().Be(
            new DateOnly(2027, 4, 1),
            "an unconfigured/January tenant must behave byte-identically to the pre-ISSUE-305 code");
    }

    /// <summary>
    /// The two tenants differ ONLY by FiscalYearStartMonth, so the expiry dates MUST differ. This is the arm
    /// that fails if the column is read by nothing — i.e. it reproduces the original defect directly, and no
    /// amount of correct arithmetic in a pure helper can make it pass.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public async Task TheOnlyDifferenceIsTheColumn_SoTheExpiryDatesMustDiffer()
    {
        var calendar = await RunYearEndAndGetTracking(_calendarTenant, _calEmployee);
        var fiscal = await RunYearEndAndGetTracking(_fiscalTenant, _fisEmployee);

        fiscal.ExpiryDate.Should().NotBe(
            calendar.ExpiryDate,
            "identical tenants apart from FiscalYearStartMonth must not produce an identical leave year — " +
            "if these match, the column is being ignored again (ISSUE-305)");

        fiscal.ExpiryDate!.Value.Should().Be(calendar.ExpiryDate!.Value.AddMonths(3),
            "the fiscal tenant's whole leave year is shifted 3 months later");
    }

    /// <summary>
    /// The carried amount itself must NOT change with the fiscal month — only the DATES move. A change here
    /// would mean the fix had altered what employees are owed, not just when it lapses.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public async Task TheCarriedAmountIsUnaffectedByTheFiscalMonth()
    {
        var calendar = await RunYearEndAndGetTracking(_calendarTenant, _calEmployee);
        var fiscal = await RunYearEndAndGetTracking(_fiscalTenant, _fisEmployee);

        fiscal.CarriedDays.Should().Be(calendar.CarriedDays);
        fiscal.CarriedDays.Should().Be(5m, "MIN(8 unused, 5 limit)");
        // (An `ExpiredDays == ExpiredDays` assert lived here and was deleted: both sides are the initializer
        // constant 0m, so it was Assert.Equal(0, 0) — it could not fail for any bug.)
    }

    /// <summary>
    /// The carry-forward CREDIT must land in the new leave year's ledger, and the closing entries in the old
    /// one — the year LABEL is unchanged by this work (only its bounds move), so a fiscal tenant's rows must
    /// still be numbered exactly like a calendar tenant's. This pins the "additive, no ledger renumbering"
    /// claim in LeaveYear's docstring.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public async Task TheLedgerYearLabelsAreUnchangedForAFiscalTenant()
    {
        await RunYearEndAndGetTracking(_fiscalTenant, _fisEmployee);

        using var db = Db(_fiscalTenant);
        var carried = await db.LeaveLedgerEntries
            .Where(l => l.EmployeeId == _fisEmployee && l.EntryType == LedgerEntryType.CarryForward)
            .ToListAsync();

        carried.Should().ContainSingle().Which.LeaveYear.Should().Be(
            ToYear, "the credit belongs to the OPENING year, still labelled by the calendar year it starts in");

        // The DATES must move even though the LABEL does not. A hardcoded 1 Jan here would file the
        // carry-forward three months outside the leave year it belongs to — the service's own comment warns
        // about exactly this, and nothing asserted it until now.
        carried.Single().OccurredAt.Should().Be(
            new DateTime(2027, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            "the credit is stamped at the fiscal year's OPENING, 1 Apr 2027 — not 1 Jan");

        var expired = await db.LeaveLedgerEntries
            .Where(l => l.EmployeeId == _fisEmployee && l.EntryType == LedgerEntryType.Expired)
            .ToListAsync();
        expired.Should().ContainSingle().Which.OccurredAt.Should().Be(
            new DateTime(2027, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            "the forfeiture is stamped at the fiscal year's CLOSE, 31 Mar 2027 — not 31 Dec 2026");
    }

    /// <summary>
    /// THE regression arm for the credit/debit ledger split (the CRIT the @integration-enforcer caught).
    ///
    /// <para>CAL-8's first cut converted the CREDIT side (accrual/carry-forward) to the tenant's leave year
    /// while the DEBIT side (leave requests, LOP, encashment, F&amp;F) still derived the label from a raw
    /// <c>.Year</c>. Both key the same <c>LeaveLedger.LeaveYear</c> int, so for an Apr-Mar tenant every
    /// January-March the accrual credited leave year 2026 while a request read 2027 — balance 0, every
    /// request blocked "insufficient balance". STRICTLY WORSE than the pre-fix code, which was wrong for
    /// fiscal tenants but agreed with itself.</para>
    ///
    /// <para><b>This drives the REAL <c>LeaveRequestService</c></b> (a debit-side consumer) against a seeded
    /// month-4 tenant and asserts it can SEE a balance credited under the fiscal label. An earlier version of
    /// this arm compared the resolver's answer to <c>LeaveYear.LabelFor</c> — which the resolver calls by
    /// construction — so it was a tautology that stayed green with the debit side fully regressed. Reverting
    /// <c>LeaveRequestService.LeaveYearForAsync</c> to <c>date.Year</c> now fails this arm.</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public async Task DebitSide_SeesTheBalanceCreditedUnderTheFiscalLeaveYear_InTheJanToMarWindow()
    {
        // Credit: 10 days under the Apr-Mar tenant's leave year 2026 (what the accrual job now writes).
        SeedLedgerBalance(_fiscalTenant, _fisEmployee, _fisLeaveType, leaveYear: 2026, balance: 10m);

        // Debit: a request STARTING 2027-02-10 — calendar year 2027, but leave year 2026 for this tenant.
        // This is the exact window where a raw `.Year` reads the wrong bucket.
        var preview = await GetBalancePreviewAsync(
            _fiscalTenant, _fisEmployee, _fisLeaveType, new DateOnly(2027, 2, 10));

        preview.CurrentBalance.Should().Be(
            10m,
            "the debit side must read the SAME leave year the credit side wrote (2026). A raw .Year would "
            + "read leave year 2027, find nothing, and report 0 — blocking every request for a quarter of "
            + "the year");
    }

    /// <summary>
    /// CONTROL for the arm above: the identical flow on a CALENDAR tenant is unchanged — proving the 10-site
    /// debit conversion is a no-op for every tenant that never configured a fiscal year.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public async Task DebitSide_CalendarTenantIsUnchanged()
    {
        SeedLedgerBalance(_calendarTenant, _calEmployee, _calLeaveType, leaveYear: 2027, balance: 10m);

        var preview = await GetBalancePreviewAsync(
            _calendarTenant, _calEmployee, _calLeaveType, new DateOnly(2027, 2, 10));

        preview.CurrentBalance.Should().Be(10m, "for a January tenant the leave year IS the calendar year");
    }


    /// <summary>
    /// GAP M13 (found by the @test-authenticator): the SERVICE→ENGINE wire.
    ///
    /// <para>My pro-rata arms in <c>LeaveYearTests</c> call <c>LeaveEntitlementEngine.CalculateProRata</c>
    /// DIRECTLY with an explicit month. They prove the arithmetic and nothing about the wiring: dropping
    /// `fiscalYearStartMonth:` from the service's call sites left the whole suite green, because the engine's
    /// parameter was defaulted and silently yielded calendar. (That default is now REMOVED, so the mutant is a
    /// compile error — this arm is the behavioural half of the same guard.)</para>
    ///
    /// <para>Drives the real <c>LeaveEntitlementService</c>. A 1-Oct joiner has ~6 of 12 months left in an
    /// Apr–Mar leave year but only ~3 in a Jan–Dec one, so the pro-rated entitlement MUST differ. It is a
    /// money number: the accrual job credits it to the ledger.</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public async Task Service_PassesTheTenantsBasisToTheProRataEngine()
    {
        var octoberJoiner = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        SetDateOfJoining(_fiscalTenant, _fisEmployee, octoberJoiner);
        SetDateOfJoining(_calendarTenant, _calEmployee, octoberJoiner);

        var fiscal = await ComputeProRataAsync(_fiscalTenant, _fisEmployee, _fisLeaveType, leaveYear: 2026);
        var calendar = await ComputeProRataAsync(_calendarTenant, _calEmployee, _calLeaveType, leaveYear: 2026);

        fiscal.Should().BeGreaterThan(
            calendar,
            "the SERVICE must hand the engine the tenant's own basis: an Apr–Mar leave year 2026 runs to "
            + "2027-03-31, leaving a 1-Oct joiner ~6 months; a Jan–Dec 2026 year leaves them ~3. If these "
            + "match, the service is not passing fiscalYearStartMonth and every fiscal tenant is credited a "
            + "wrong entitlement");
    }

    private void SetDateOfJoining(Guid tenant, Guid employee, DateTime dateOfJoining)
    {
        using var db = Db(tenant);
        var e = db.Employees.Single(x => x.Id == employee);
        e.DateOfJoining = dateOfJoining;
        db.SaveChanges();
    }

    /// <summary>Pro-rated entitlement via the REAL service (which resolves the month and calls the engine).</summary>
    private async Task<decimal> ComputeProRataAsync(Guid tenant, Guid employee, Guid leaveType, int leaveYear)
    {
        using var provider = BuildProvider(tenant);
        var result = await provider.GetRequiredService<ILeaveEntitlementService>()
            .ComputeEffectiveEntitlementAsync(employee, leaveType, leaveYear);
        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value!.ProratedEntitlementDays;
    }

    /// <summary>
    /// A tenant row that never configured the column resolves to calendar.
    ///
    /// <para><b>Honest scope</b> (an earlier docstring here overclaimed, and the test-authenticator audit
    /// proved it): this does NOT guard the entity initializer. Deleting <c>= 1</c> from
    /// <c>Tenant.FiscalYearStartMonth</c> leaves this arm GREEN, because <c>LeaveYear</c> maps the resulting
    /// <c>0</c> to calendar anyway — months 0 and 1 are behaviourally identical, so no test on this path can
    /// detect a missing default. The real guarantee is the migration's <c>defaultValue: 1</c>, which InMemory
    /// never executes. What this arm DOES pin is that the no-row/unconfigured path degrades to calendar rather
    /// than throwing or picking an arbitrary month — which is what the background sweeps depend on.</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public async Task ATenantThatNeverConfiguredTheColumn_IsCalendar()
    {
        var tenant = Guid.NewGuid();
        var leaveType = Guid.NewGuid();
        var employee = Guid.NewGuid();

        using (var db = Db(tenant))
        {
            // Deliberately does NOT set FiscalYearStartMonth.
            db.Tenants.Add(new Tenant
            {
                Id = tenant, Subdomain = "unset", Name = "Unset", Status = TenantStatus.Active,
            });
            db.SaveChanges();
        }
        Seed_LeaveTypeAndEmployee(tenant, leaveType, employee, "UNSET-1");

        var tracking = await RunYearEndAndGetTracking(tenant, employee);

        tracking.ExpiryDate.Should().Be(new DateOnly(2027, 4, 1), "an unconfigured tenant is calendar");
    }

    private void Seed_LeaveTypeAndEmployee(Guid tenant, Guid leaveType, Guid employee, string empNo)
    {
        using var db = Db(tenant);
        db.LeaveTypes.Add(new LeaveType
        {
            Id = leaveType, TenantId = tenant, Name = "Annual Leave",
            AnnualEntitlement = 14m, AccrualFrequency = AccrualFrequency.Upfront,
            CarryForwardLimit = 5m, CarryForwardExpiryMonths = ExpiryMonths,
            Gender = LeaveTypeGender.All, IsActive = true,
        });
        db.Employees.Add(new Employee
        {
            Id = employee, TenantId = tenant, UserId = Guid.NewGuid(),
            EmployeeNo = empNo, FirstName = "Emp", LastName = empNo, Email = $"{empNo}@x.com",
            DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenant,
            EntryType = LedgerEntryType.Used, EmployeeId = employee, LeaveTypeId = leaveType,
            LeaveYear = FromYear, Amount = -6m, BalanceAfter = 8m,
            OccurredAt = new DateTime(FromYear, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        db.SaveChanges();
    }
}
