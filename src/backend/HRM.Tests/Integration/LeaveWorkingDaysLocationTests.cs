// ============================================================================
// BUG-284 (CAL-2 / US-LV-003 FR-3, US-ATT-011 AC-2): leave day-counting and the half-day gate must use the
// employee's RESOLVED working-day set (the four-tier Employee → Location → Tenant → code-default chain), not a
// hardcoded Mon–Fri. Before the fix, LeaveRequestService ignored the shift system entirely: every production
// caller omitted the optional `workWeek` argument, so `WorkingDaysCalculator` fell back to Mon–Fri. A Gulf
// tenant on a Sun–Thu week therefore had Friday (their weekend) DEDUCTED as a leave day and Sunday (a workday)
// SKIPPED — wrong balances — while a half-day on Sunday was rejected and one on Friday wrongly accepted.
//
// WHY POSTGRES: this is an entitlement/money path and the repo rule is that calendar/money arms run on the real
// provider. The concrete provider-dependent bits are: Shift.WorkingDays round-trips as a PostgreSQL `integer[]`
// (ShiftConfiguration maps List<int> → integer[]); `employeeIds.Contains(...)` must translate to `= ANY(...)`;
// and Employee.UserId carries a real FK to the global `users` table that InMemory would not enforce.
//
// Honest scope (corrected after the CAL-2 authenticity audit): the ISO→DayOfWeek bridge and the four-tier
// resolution itself are pure in-memory C# and would behave identically on InMemory — this harness is NOT what
// proves them. EF global query filters are also provider-independent. The accurate claim is narrower: nothing
// here would silently pass on InMemory in a way that hides a BUG-284 regression.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.LeaveRequests.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class LeaveWorkingDaysLocationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private Guid _employeeId;
    private Guid _annualLeaveTypeId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── harness ────────────────────────────────────────────────────────

    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId { get; init; }
        public string Subdomain => "acme";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    // Mirrors ShiftNameTrimDuplicatePostgresTests exactly. UseSnakeCaseNamingConvention is NOT optional: the app
    // maps via EFCore.NamingConventions, so omitting it makes the model disagree with the migration snapshot and
    // MigrateAsync throws PendingModelChangesWarning. EnableRetryOnFailure is the BUG-068/ISSUE-277 rule.
    private AppDbContext Db()
    {
        var tc = new FixedTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(_userId);
        cu.Email.Returns("gulf@acme.test");

        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_postgres.GetConnectionString(), n =>
                {
                    n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    n.EnableRetryOnFailure(maxRetryCount: 3);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
                .Options,
            tc);
    }

    private LeaveRequestService Service(AppDbContext db)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(_userId);
        currentUser.Email.Returns("gulf@acme.test");

        // No public holidays anywhere in these ranges — the work-week is the ONLY thing under test.
        var holidays = Substitute.For<IHolidayProvider>();
        holidays.GetHolidaysAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<DateOnly>>(new HashSet<DateOnly>()));

        var ctx = new FixedTenantContext { TenantId = _tenantId };
        return new LeaveRequestService(
            db,
            ctx,
            currentUser,
            holidays,
            Substitute.For<ILeaveNotificationService>(),
            NullLogger<LeaveRequestService>.Instance,
            new TenantLeaveYearResolver(db, ctx));
    }

    // ── seeding ────────────────────────────────────────────────────────

    private Shift NewShift(string name, int[] isoWorkingDays, bool isDefault = false) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = _tenantId,
        Name = name,
        Type = ShiftType.Single,
        StartTime = new TimeOnly(9, 0),
        EndTime = new TimeOnly(17, 0),
        WorkingDays = isoWorkingDays.ToList(),
        IsDefault = isDefault,
        IsActive = true,
    };

    /// <summary>Seeds the leave type, org units, the employee (optionally at a Location) and a fat balance.</summary>
    private async Task SeedAsync(Guid? locationId, Shift[] shifts, Location[] locations)
    {
        await using var db = Db();

        // Employee.UserId carries a real FK to the global `users` table (fk_employees_users_user_id). Postgres
        // enforces it; InMemory would not — the reason this suite runs on the real provider.
        db.Users.Add(new User { Id = _userId, Email = "gulf@acme.test", DisplayName = "Gulf Worker" });

        db.Shifts.AddRange(shifts);
        db.Locations.AddRange(locations);

        var dept = new Department { Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "Ops", Code = "OPS" };
        var title = new JobTitle { Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, TitleName = "Analyst" };
        db.Departments.Add(dept);
        db.JobTitles.Add(title);

        db.LeaveTypes.Add(new LeaveType
        {
            Id = _annualLeaveTypeId = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            Name = "Annual Leave",
            AnnualEntitlement = 30,
            AccrualFrequency = AccrualFrequency.Upfront,
            HalfDayAllowed = true,
            ProbationEligible = true,
            Gender = LeaveTypeGender.All,
            IsActive = true,
        });

        db.Employees.Add(new Employee
        {
            Id = _employeeId = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            UserId = _userId,
            EmployeeNo = "EMP-0001",
            FirstName = "Gulf",
            LastName = "Worker",
            Email = "gulf@acme.test",
            Gender = Gender.Male,
            DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = dept.Id,
            JobTitleId = title.Id,
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
            LocationId = locationId,
        });

        await db.SaveChangesAsync();

        // Balance must be seeded AFTER the employee id is assigned.
        //
        // Seed EVERY year the arms can reach, not just one. LeaveRequestService looks the balance up by
        // `request.StartDate.Year` and each arm derives its dates from an independent NextDay(...) anchor, so
        // when the [today+3, today+13] window straddles 1 Jan (today ≈ 23–28 Dec) a single-year seed misses,
        // the balance reads 0, and the arm fails with "Insufficient leave balance" — a late-December-only red
        // whose error message points nowhere near this file.
        var years = new[] { DateOnly.FromDateTime(DateTime.UtcNow).Year, DateOnly.FromDateTime(DateTime.UtcNow).Year + 1 };

        await using var db2 = Db();
        foreach (var year in years)
        {
            db2.LeaveLedgerEntries.Add(new LeaveLedger
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                EntryType = LedgerEntryType.Accrual,
                EmployeeId = _employeeId,
                LeaveTypeId = _annualLeaveTypeId,
                LeaveYear = year,
                Amount = 30m,
                BalanceAfter = 30m,
                OccurredAt = DateTime.UtcNow,
            });
        }
        await db2.SaveChangesAsync();
    }

    private CreateLeaveRequestRequest Req(DateOnly start, DateOnly end, bool isHalfDay = false) => new()
    {
        LeaveTypeId = _annualLeaveTypeId,
        StartDate = start,
        EndDate = end,
        IsHalfDay = isHalfDay,
        HalfDaySession = isHalfDay ? "AM" : null,
        Reason = "Test",
    };

    /// <summary>
    /// The next <paramref name="dow"/> at least a few days out — inside the BR-1 past / BR-2 90-day future
    /// window, so these arms never depend on the calendar date the suite happens to run on.
    /// </summary>
    private static DateOnly NextDay(DayOfWeek dow)
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        while (d.DayOfWeek != dow)
            d = d.AddDays(1);
        return d;
    }

    // ══ TC-LV-262 — Gulf Sun–Thu: the BUG-284 regression ══════════════

    /// <summary>
    /// TC-LV-262 (BUG-284): a Gulf employee at a Sun–Thu Location. Leave Sun→Thu must deduct 5 days — every one
    /// of those days is a workday for them. Pre-fix this returned 4: Mon–Fri counted Mon/Tue/Wed/Thu and SKIPPED
    /// Sunday.
    ///
    /// <para>Note (audit correction): 5 discriminates Sun–Thu from the pre-fix Mon–Fri (4), but NOT from an
    /// "every calendar day" week — Sun→Thu is 5 calendar days, so a 7-day set also yields 5. The Gulf weekend
    /// being SKIPPED in the count path is proven by
    /// <see cref="Gulf_SunThu_Employee_ThursdayToFriday_CountsOnlyThursday"/>, not by this arm.</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-262")]
    public async Task Gulf_SunThu_Employee_LeaveDeducts5WorkingDays_SundayCounted()
    {
        var gulf = NewShift("Gulf Sun-Thu", [7, 1, 2, 3, 4]);
        var monFri = NewShift("General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true);
        var dubai = new Location
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "Dubai",
            TimeZone = "Asia/Dubai", DefaultShiftId = gulf.Id, IsActive = true,
        };
        await SeedAsync(dubai.Id, [monFri, gulf], [dubai]);

        var sunday = NextDay(DayOfWeek.Sunday);
        var thursday = sunday.AddDays(4);

        await using var db = Db();
        var result = await Service(db).CreateAsync(Req(sunday, thursday));

        result.IsSuccess.Should().BeTrue(because: result.Error);
        result.Value!.TotalDays.Should().Be(
            5m,
            "Sun–Thu are ALL working days for a Gulf employee — pre-fix Mon–Fri counted 4 and skipped Sunday");
    }

    /// <summary>
    /// TC-LV-262 steps 3–4: the half-day gate follows the SAME resolved week. Sunday (a Gulf workday) is
    /// accepted; Friday (their weekend) is rejected. Pre-fix this was exactly inverted.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-262")]
    public async Task Gulf_SunThu_Employee_HalfDay_SundayAccepted_FridayRejected()
    {
        var gulf = NewShift("Gulf Sun-Thu", [7, 1, 2, 3, 4]);
        var monFri = NewShift("General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true);
        var dubai = new Location
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "Dubai",
            TimeZone = "Asia/Dubai", DefaultShiftId = gulf.Id, IsActive = true,
        };
        await SeedAsync(dubai.Id, [monFri, gulf], [dubai]);

        var sunday = NextDay(DayOfWeek.Sunday);
        var friday = NextDay(DayOfWeek.Friday);

        await using (var db = Db())
        {
            var sun = await Service(db).CreateAsync(Req(sunday, sunday, isHalfDay: true));
            sun.IsSuccess.Should().BeTrue(
                because: "Sunday IS a working day for a Gulf employee — pre-fix Mon–Fri rejected it. "
                    + sun.Error);
            sun.Value!.TotalDays.Should().Be(0.5m);
        }

        await using (var db = Db())
        {
            var fri = await Service(db).CreateAsync(Req(friday, friday, isHalfDay: true));
            fri.IsFailure.Should().BeTrue("Friday is the Gulf weekend — pre-fix Mon–Fri wrongly accepted it");
            fri.Error.Should().Contain("non-working day");
        }
    }

    /// <summary>
    /// TC-LV-262 step 2: the balance PREVIEW must agree with the deduction. These are two separate call sites
    /// (LeaveRequestService :142 vs :353) and both were independently hardcoded to Mon–Fri — a preview that
    /// disagreed with the days actually deducted would be its own defect.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-262")]
    public async Task Gulf_SunThu_Employee_BalancePreview_AgreesWithTheDeduction()
    {
        var gulf = NewShift("Gulf Sun-Thu", [7, 1, 2, 3, 4]);
        var monFri = NewShift("General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true);
        var dubai = new Location
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "Dubai",
            TimeZone = "Asia/Dubai", DefaultShiftId = gulf.Id, IsActive = true,
        };
        await SeedAsync(dubai.Id, [monFri, gulf], [dubai]);

        var sunday = NextDay(DayOfWeek.Sunday);
        var thursday = sunday.AddDays(4);

        await using var db = Db();
        var preview = await Service(db).GetBalancePreviewAsync(
            _annualLeaveTypeId, sunday, thursday, isHalfDay: false);

        preview.IsSuccess.Should().BeTrue(because: preview.Error);
        preview.Value!.RequestedDays.Should().Be(
            5m, "the preview must show what the deduction will actually take — 5, not the Mon–Fri 4");
    }

    // ══ TC-LV-263 — single-branch control: no regression ══════════════

    /// <summary>
    /// TC-LV-263 (BUG-284 control): a single-branch tenant with NO location/shift override still counts Mon–Fri.
    /// Fri→Mon deducts 2 (Sat/Sun excluded). This is the regression that proves the fix is additive — existing
    /// Mon–Fri tenants must be untouched.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-263")]
    public async Task SingleBranch_MonFri_Unchanged_FridayToMonday_Deducts2()
    {
        var monFri = NewShift("General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true);
        await SeedAsync(locationId: null, [monFri], []);

        var friday = NextDay(DayOfWeek.Friday);
        var monday = friday.AddDays(3);

        await using var db = Db();
        var result = await Service(db).CreateAsync(Req(friday, monday));

        result.IsSuccess.Should().BeTrue(because: result.Error);
        result.Value!.TotalDays.Should().Be(2m, "Fri + Mon are working days; Sat/Sun are not — unchanged");
    }

    /// <summary>
    /// TC-LV-263 steps 2–3: the half-day gate for a single-branch tenant is unchanged — Saturday rejected,
    /// Wednesday accepted.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-263")]
    public async Task SingleBranch_MonFri_HalfDay_SaturdayRejected_WednesdayAccepted()
    {
        var monFri = NewShift("General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true);
        await SeedAsync(locationId: null, [monFri], []);

        await using (var db = Db())
        {
            var sat = await Service(db).CreateAsync(
                Req(NextDay(DayOfWeek.Saturday), NextDay(DayOfWeek.Saturday), isHalfDay: true));
            sat.IsFailure.Should().BeTrue("Saturday is not a Mon–Fri working day");
            sat.Error.Should().Contain("non-working day");
        }

        await using (var db = Db())
        {
            var wed = await Service(db).CreateAsync(
                Req(NextDay(DayOfWeek.Wednesday), NextDay(DayOfWeek.Wednesday), isHalfDay: true));
            wed.IsSuccess.Should().BeTrue(because: wed.Error);
            wed.Value!.TotalDays.Should().Be(0.5m);
        }
    }

    /// <summary>
    /// Tier 4 smoke arm: a tenant with NO shift rows at all resolves the Mon–Fri code default and does not NRE
    /// or return garbage.
    ///
    /// <para><b>What this does NOT prove</b> (corrected after the CAL-2 authenticity audit — the earlier
    /// docstring here claimed it guarded the empty-set seam; it cannot): the "empty = every calendar day"
    /// sentinel lives in <c>ShiftScheduleResolver.CountWorkingDays</c>, which the leave path never calls. Leave
    /// uses <c>WorkingDaysCalculator</c>, whose own <c>workWeek is { Count: &gt; 0 } ? workWeek :
    /// DefaultWorkWeek</c> maps empty → Mon–Fri. So an empty set reaching here yields 2 either way and this
    /// scenario cannot fail. The seam is genuinely covered by
    /// <see cref="LocationFlexibleShiftWithNoWorkingDays_FallsThroughToTenantDefault_NotMonFri"/>, which reaches
    /// it via the one production state that can: a Flexible shift with no declared WorkingDays.</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-263")]
    public async Task NoShiftConfiguredAtAll_LeaveStillCountsMonFri_NotEveryCalendarDay()
    {
        await SeedAsync(locationId: null, [], []);

        var friday = NextDay(DayOfWeek.Friday);
        var monday = friday.AddDays(3);

        await using var db = Db();
        var result = await Service(db).CreateAsync(Req(friday, monday));

        result.IsSuccess.Should().BeTrue(because: result.Error);
        result.Value!.TotalDays.Should().Be(
            2m, "the tier-4 Mon–Fri code default applies — 4 would mean the empty-set 'all days' sentinel leaked");
    }

    /// <summary>
    /// TC-LV-262: a Gulf range that SPANS their weekend, in the COUNT path. Thu→Fri must deduct **1** — Thursday
    /// is a Gulf workday, Friday is their weekend. This is the tight oracle the `Deducts5` arm cannot be:
    /// Mon–Fri would say 2 (both are workdays for it) and an "every calendar day" week would also say 2. Only a
    /// correctly-resolved Sun–Thu set yields 1, so this arm alone separates the fix from BOTH failure modes.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-262")]
    public async Task Gulf_SunThu_Employee_ThursdayToFriday_CountsOnlyThursday()
    {
        var gulf = NewShift("Gulf Sun-Thu", [7, 1, 2, 3, 4]);
        var monFri = NewShift("General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true);
        var dubai = new Location
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "Dubai",
            TimeZone = "Asia/Dubai", DefaultShiftId = gulf.Id, IsActive = true,
        };
        await SeedAsync(dubai.Id, [monFri, gulf], [dubai]);

        var thursday = NextDay(DayOfWeek.Thursday);
        var friday = thursday.AddDays(1);

        await using var db = Db();
        var result = await Service(db).CreateAsync(Req(thursday, friday));

        result.IsSuccess.Should().BeTrue(because: result.Error);
        result.Value!.TotalDays.Should().Be(
            1m,
            "only Thursday is a Gulf working day — Friday is their weekend. Mon–Fri would count 2, and so "
            + "would an 'every calendar day' week; 1 is reachable ONLY from a correct Sun–Thu set.");
    }

    /// <summary>
    /// The empty-set seam, reached the ONLY way production can reach it, and tier 3 in the same arm.
    ///
    /// <para>A <b>Flexible</b> shift may legally carry no <c>WorkingDays</c> (the validator requires them only
    /// for Single/Rotating — ISSUE-307). Wired as a Location's <c>DefaultShiftId</c>, tier 2's
    /// <c>TryGetValue</c> SUCCEEDS on it, so without <c>ShiftScheduleResolver.ToCalendar</c>'s
    /// <c>Count &gt; 0</c> guard an EMPTY set escapes and shadows tiers 3/4.</para>
    ///
    /// <para>The tenant default is deliberately <b>Sun–Thu</b>, not Mon–Fri, so this arm kills TWO mutations at
    /// once: (a) remove the empty guard → empty leaks → the service/calculator fallbacks force Mon–Fri → 4, RED;
    /// (b) delete tier 3 entirely → the Mon–Fri code default → 4, RED. Before this arm, <b>both</b> mutations
    /// survived the whole suite, because every other tenant default here is Mon–Fri — identical to the code
    /// default — which makes tiers 3 and 4 indistinguishable.</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-263")]
    public async Task LocationFlexibleShiftWithNoWorkingDays_FallsThroughToTenantDefault_NotMonFri()
    {
        // Tenant default = Sun-Thu: the ONLY tier that can produce 5 for a Sun->Thu request.
        var tenantDefaultSunThu = NewShift("Tenant Sun-Thu", [7, 1, 2, 3, 4], isDefault: true);

        var flexible = NewShift("Flexible - no declared days", []);
        flexible.Type = ShiftType.Flexible;

        var hub = new Location
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "Remote Hub",
            TimeZone = "Asia/Dubai", DefaultShiftId = flexible.Id, IsActive = true,
        };
        await SeedAsync(hub.Id, [tenantDefaultSunThu, flexible], [hub]);

        var sunday = NextDay(DayOfWeek.Sunday);
        var thursday = sunday.AddDays(4);

        await using var db = Db();
        var result = await Service(db).CreateAsync(Req(sunday, thursday));

        result.IsSuccess.Should().BeTrue(because: result.Error);
        result.Value!.TotalDays.Should().Be(
            5m,
            "a shift declaring no working days is not a calendar: tier 2 must NOT resolve, and the Sun-Thu "
            + "TENANT default (tier 3) must supply the week. 4 would mean either the empty set leaked and was "
            + "coerced to Mon-Fri, or tier 3 was skipped for the Mon-Fri code default.");
    }

    /// <summary>
    /// Pins the OTHER risky end of the ISO→DayOfWeek bridge in the COUNT path. The resolver speaks ISO
    /// (1=Mon..7=Sun); WorkingDaysCalculator speaks DayOfWeek (Sun=0..Sat=6), and the whole conversion is
    /// <c>(DayOfWeek)(iso % 7)</c>. ISO 7→Sunday(0) is covered by the Gulf arms; this covers **ISO 6→Saturday(6)**.
    ///
    /// <para>The range is <b>Fri→Sat</b> deliberately. The obvious Fri→Mon range does NOT discriminate: mismap
    /// ISO 6 to Sunday and you lose Saturday but gain Sunday, so the count stays 3 and the mutation survives.
    /// Fri→Sat contains no Sunday to compensate — correct Mon–Sat gives <b>2</b>, while BOTH the pre-fix Mon–Fri
    /// hardcode AND an ISO-6 mismap give 1. (Verified by mutation: <c>iso == 6 ? 0 : iso % 7</c> reddens this.)</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-262")]
    public async Task MonSat_Employee_SaturdayIsAWorkingDay_FridayToSaturdayDeducts2()
    {
        var monSat = NewShift("Retail Mon-Sat", [1, 2, 3, 4, 5, 6]);
        var monFri = NewShift("General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true);
        var store = new Location
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "Colombo Store",
            TimeZone = "Asia/Colombo", DefaultShiftId = monSat.Id, IsActive = true,
        };
        await SeedAsync(store.Id, [monFri, monSat], [store]);

        var friday = NextDay(DayOfWeek.Friday);
        var saturday = friday.AddDays(1);

        await using var db = Db();
        var result = await Service(db).CreateAsync(Req(friday, saturday));

        result.IsSuccess.Should().BeTrue(because: result.Error);
        result.Value!.TotalDays.Should().Be(
            2m,
            "Fri AND Sat are working days on a Mon–Sat week — ISO 6 must map to DayOfWeek.Saturday. Both the "
            + "pre-fix Mon–Fri hardcode and an ISO-6 mismap would give 1.");
    }

    /// <summary>
    /// The half-day gate on the same Mon–Sat week: Saturday accepted (it IS their working day), Sunday rejected.
    /// Directly inverts the single-branch control, so the two together prove the gate follows the resolved week
    /// rather than any constant.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-262")]
    public async Task MonSat_Employee_HalfDay_SaturdayAccepted_SundayRejected()
    {
        var monSat = NewShift("Retail Mon-Sat", [1, 2, 3, 4, 5, 6]);
        var monFri = NewShift("General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true);
        var store = new Location
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "Colombo Store",
            TimeZone = "Asia/Colombo", DefaultShiftId = monSat.Id, IsActive = true,
        };
        await SeedAsync(store.Id, [monFri, monSat], [store]);

        await using (var db = Db())
        {
            var sat = await Service(db).CreateAsync(
                Req(NextDay(DayOfWeek.Saturday), NextDay(DayOfWeek.Saturday), isHalfDay: true));
            sat.IsSuccess.Should().BeTrue(
                because: "Saturday IS a working day on a Mon–Sat week — the tenant Mon–Fri default would "
                    + "reject it. " + sat.Error);
            sat.Value!.TotalDays.Should().Be(0.5m);
        }

        await using (var db = Db())
        {
            var sun = await Service(db).CreateAsync(
                Req(NextDay(DayOfWeek.Sunday), NextDay(DayOfWeek.Sunday), isHalfDay: true));
            sun.IsFailure.Should().BeTrue("Sunday is not a working day on a Mon–Sat week");
            sun.Error.Should().Contain("non-working day");
        }
    }

    /// <summary>
    /// BR-3 precedence through the leave path: a personal EmployeeShift beats the Location default. The Mon–Wed
    /// assignee at the Gulf location must count Mon–Wed, proving leave consumes the FULL four-tier chain rather
    /// than only the location tier.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-262")]
    public async Task PersonalShiftAssignment_BeatsLocationDefault_InLeaveCounting()
    {
        var gulf = NewShift("Gulf Sun-Thu", [7, 1, 2, 3, 4]);
        var monWed = NewShift("Part-time Mon-Wed", [1, 2, 3]);
        var tenantDefault = NewShift("General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true);
        var dubai = new Location
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "Dubai",
            TimeZone = "Asia/Dubai", DefaultShiftId = gulf.Id, IsActive = true,
        };
        await SeedAsync(dubai.Id, [tenantDefault, monWed, gulf], [dubai]);

        await using (var seed = Db())
        {
            seed.EmployeeShifts.Add(new EmployeeShift
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                EmployeeId = _employeeId,
                ShiftId = monWed.Id,
                EffectiveFrom = new DateOnly(2020, 1, 1),
                EffectiveTo = null,
            });
            await seed.SaveChangesAsync();
        }

        var monday = NextDay(DayOfWeek.Monday);
        var friday = monday.AddDays(4);

        await using var db = Db();
        var result = await Service(db).CreateAsync(Req(monday, friday));

        result.IsSuccess.Should().BeTrue(because: result.Error);
        result.Value!.TotalDays.Should().Be(
            3m,
            "the personal Mon–Wed assignment (tier 1) wins: Mon/Tue/Wed only — the Gulf location default would "
            + "have counted 4 (Mon–Thu) and the tenant default 5");
    }
}
