// ============================================================================
// CAL-3 wiring: BUG-285 + BUG-286 through the real OvertimeService.
//
// BUG-285 (HIGH, money): OvertimeMultiplierResolver decided weekend-vs-weekday with a hardcoded
//   `date.DayOfWeek is Saturday or Sunday`, ignoring Shift.WorkingDays. A Gulf tenant's FRIDAY OT (their
//   weekend) was paid at the WEEKDAY multiplier and SUNDAY OT (a workday) at the WEEKEND multiplier — the
//   wrong rate flowing straight into payroll earnings.
// BUG-286 (MED, money): OvertimeService.IsPublicHolidayAsync matched a holiday by Date/IsActive/Type with NO
//   LocationId filter, bypassing the location-aware IHolidayProvider that leave already used. A New-York-only
//   holiday granted a London employee the holiday OT multiplier, and vice-versa.
//
// The pure multiplier logic is unit-tested in OvertimeCalculatorTests. THIS suite proves the WIRING: that the
// service actually resolves the employee's four-tier work-week and actually scopes the holiday lookup to their
// location. A green unit test proves neither — CAL-1 shipped a feature that was entirely dead code behind a
// green build.
//
// WHY POSTGRES: Shift.WorkingDays round-trips as `integer[]`; Employee.UserId/LocationId carry real FKs
// Postgres enforces and InMemory does not; and HolidayProvider's location filter is a real query.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.DTOs;
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

public sealed class OvertimeWorkWeekAndHolidayScopeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();

    private const decimal WeekdayMultiplier = 1.5m;
    private const decimal WeekendMultiplier = 2.0m;
    private const decimal HolidayMultiplier = 2.5m;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db(Guid.NewGuid());
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

    private static ICurrentUser User(Guid userId)
    {
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(userId);
        cu.Email.Returns($"{userId:N}@acme.test");
        return cu;
    }

    private AppDbContext Db(Guid userId)
    {
        var tc = new FixedTenantContext { TenantId = _tenantId };
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_postgres.GetConnectionString(), n =>
                {
                    n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    n.EnableRetryOnFailure(maxRetryCount: 3);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(User(userId)))
                .Options,
            tc);
    }

    /// <summary>The service as DI composes it — with the real, location-aware HolidayProvider (BUG-286).</summary>
    private OvertimeService Service(AppDbContext db, Guid userId)
    {
        var tc = new FixedTenantContext { TenantId = _tenantId };
        var cu = User(userId);
        return new OvertimeService(
            db, tc, cu, NullLogger<OvertimeService>.Instance,
            workflowRuntime: null,
            notifications: null,
            holidayProvider: new HolidayProvider(db));
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

    private Location NewLocation(string name, Guid? defaultShiftId = null) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = _tenantId,
        Name = name,
        TimeZone = "UTC",
        DefaultShiftId = defaultShiftId,
        IsActive = true,
    };

    private (Guid EmployeeId, Guid UserId) NewEmployee(AppDbContext db, string no, Guid? locationId)
    {
        var userId = Guid.NewGuid();
        db.Users.Add(new User { Id = userId, Email = $"{no}@acme.test", DisplayName = no });

        var dept = new Department { Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = $"D-{no}", Code = no };
        var title = new JobTitle { Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, TitleName = $"T-{no}" };
        db.Departments.Add(dept);
        db.JobTitles.Add(title);

        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId,
            TenantId = _tenantId,
            UserId = userId,
            EmployeeNo = no,
            FirstName = no,
            LastName = "Worker",
            Email = $"{no}@acme.test",
            DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = dept.Id,
            JobTitleId = title.Id,
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
            LocationId = locationId,
        });
        return (empId, userId);
    }

    private void SeedSettings(AppDbContext db) =>
        db.AttendanceSettings.Add(new AttendanceSettings
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            StandardWorkMinutes = 480,
            WeekdayOvertimeMultiplier = WeekdayMultiplier,
            WeekendOvertimeMultiplier = WeekendMultiplier,
            HolidayOvertimeMultiplier = HolidayMultiplier,
        });

    private static OvertimePreApprovalRequest Req(DateOnly date) => new()
    {
        Date = date,
        ExpectedHours = 2m,
        Reason = "Month-end close cover",
    };

    /// <summary>Next <paramref name="dow"/> at least 3 days out — pre-approval rejects past dates.</summary>
    private static DateOnly NextDay(DayOfWeek dow)
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        while (d.DayOfWeek != dow)
            d = d.AddDays(1);
        return d;
    }

    // ══ TC-ATT-153 — BUG-285: OT weekend basis follows the resolved work-week ══

    /// <summary>
    /// TC-ATT-153 (BUG-285) through the REAL service: a Gulf employee at a Sun–Thu Location.
    /// FRIDAY OT → weekend multiplier (2.0×); SUNDAY OT → weekday multiplier (1.5×). Pre-fix these were
    /// exactly inverted, and the wrong rate was persisted onto the OvertimeRecord that payroll reads.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-ATT-153")]
    // Only the multiplier is asserted here: OvertimeDto exposes Multiplier but NOT the basis string, and
    // the multiplier is the observable that actually reaches payroll. The WEEKEND/WEEKDAY *basis* is
    // asserted at the unit layer (OvertimeCalculatorTests). Carrying an expectedBasis parameter here and
    // never asserting it would be a claim the arm does not honour (xUnit1026).
    [InlineData(DayOfWeek.Friday, 2.0)]
    [InlineData(DayOfWeek.Sunday, 1.5)]
    public async Task Gulf_PreApproval_UsesResolvedWorkWeekForTheWeekendBasis(
        DayOfWeek dow, double expectedMultiplier)
    {
        Guid userId;
        await using (var seed = Db(Guid.NewGuid()))
        {
            var gulf = NewShift("Gulf Sun-Thu", [7, 1, 2, 3, 4]);
            var monFri = NewShift("General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true);
            seed.Shifts.AddRange(monFri, gulf);

            var dubai = NewLocation("Dubai", gulf.Id);
            seed.Locations.Add(dubai);

            SeedSettings(seed);
            (_, userId) = NewEmployee(seed, "GULF-1", dubai.Id);
            await seed.SaveChangesAsync();
        }

        await using var db = Db(userId);
        var result = await Service(db, userId).SubmitPreApprovalAsync(Req(NextDay(dow)));

        result.IsSuccess.Should().BeTrue(because: result.Error);
        result.Value!.Multiplier.Should().Be(
            (decimal)expectedMultiplier,
            "the Gulf week is Sun-Thu: Friday is their WEEKEND and Sunday a WORKDAY. The pre-fix hardcoded "
            + "Sat/Sun check inverted both and paid the wrong rate into payroll earnings.");
    }

    /// <summary>
    /// Control: a single-branch Mon–Fri employee is unaffected — Saturday OT still pays the weekend rate and
    /// Monday OT the weekday rate. Proves CAL-3 is additive for existing tenants.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-ATT-153")]
    [InlineData(DayOfWeek.Saturday, 2.0)]
    [InlineData(DayOfWeek.Monday, 1.5)]
    public async Task SingleBranch_MonFri_WeekendBasisUnchanged(
        DayOfWeek dow, double expectedMultiplier)
    {
        Guid userId;
        await using (var seed = Db(Guid.NewGuid()))
        {
            seed.Shifts.Add(NewShift("General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true));
            SeedSettings(seed);
            (_, userId) = NewEmployee(seed, "HQ-1", locationId: null);
            await seed.SaveChangesAsync();
        }

        await using var db = Db(userId);
        var result = await Service(db, userId).SubmitPreApprovalAsync(Req(NextDay(dow)));

        result.IsSuccess.Should().BeTrue(because: result.Error);
        result.Value!.Multiplier.Should().Be((decimal)expectedMultiplier, "Mon-Fri behaviour is unchanged");
    }

    // ══ TC-ATT-154 — BUG-286: the holiday lookup is location-scoped ══

    /// <summary>
    /// TC-ATT-154 (BUG-286) steps 1–2: a New-York-ONLY holiday must grant the holiday multiplier to the NY
    /// employee and NOT to the London employee on the very same date. Pre-fix the unfiltered inline query
    /// matched the holiday for BOTH — London got 2.5× for a day that is an ordinary working day there.
    ///
    /// <para>Both employees are on the tenant Mon–Fri default and the date is a WEEKDAY, so the only thing
    /// that can move the multiplier off 1.5× is the holiday scope — the arm cannot pass for an unrelated
    /// reason.</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-154")]
    public async Task NewYorkOnlyHoliday_GrantsHolidayRateToNyEmployee_ButNotToLondonEmployee()
    {
        var holidayDate = NextDay(DayOfWeek.Wednesday);   // a weekday for both
        Guid nyUser, londonUser;

        await using (var seed = Db(Guid.NewGuid()))
        {
            seed.Shifts.Add(NewShift("General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true));

            var ny = NewLocation("New York");
            var london = NewLocation("London");
            seed.Locations.AddRange(ny, london);

            SeedSettings(seed);
            (_, nyUser) = NewEmployee(seed, "NY-1", ny.Id);
            (_, londonUser) = NewEmployee(seed, "LON-1", london.Id);

            // A holiday scoped to New York ONLY.
            seed.Holidays.Add(new Holiday
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                Name = "Thanksgiving (NY only)",
                Date = holidayDate,
                Type = HolidayType.Public,
                LocationId = ny.Id,
                IsActive = true,
            });

            await seed.SaveChangesAsync();
        }

        await using (var db = Db(nyUser))
        {
            var ny = await Service(db, nyUser).SubmitPreApprovalAsync(Req(holidayDate));
            ny.IsSuccess.Should().BeTrue(because: ny.Error);
            ny.Value!.Multiplier.Should().Be(
                HolidayMultiplier, "the date IS a holiday at the NY employee's location");
        }

        await using (var db = Db(londonUser))
        {
            var london = await Service(db, londonUser).SubmitPreApprovalAsync(Req(holidayDate));
            london.IsSuccess.Should().BeTrue(because: london.Error);
            london.Value!.Multiplier.Should().Be(
                WeekdayMultiplier,
                "a New-York-only holiday must NOT grant a London employee the holiday multiplier — pre-fix "
                + "the unfiltered query gave them 2.5x for an ordinary working day");
        }
    }

    /// <summary>
    /// TC-ATT-154 step 3: the symmetry. A London-only holiday must not reach a New York employee either —
    /// proving the scoping is a real filter rather than a one-way special case.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-154")]
    public async Task LondonOnlyHoliday_DoesNotGrantHolidayRateToNewYorkEmployee()
    {
        var holidayDate = NextDay(DayOfWeek.Wednesday);
        Guid nyUser, londonUser;

        await using (var seed = Db(Guid.NewGuid()))
        {
            seed.Shifts.Add(NewShift("General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true));

            var ny = NewLocation("New York");
            var london = NewLocation("London");
            seed.Locations.AddRange(ny, london);

            SeedSettings(seed);
            (_, nyUser) = NewEmployee(seed, "NY-2", ny.Id);
            (_, londonUser) = NewEmployee(seed, "LON-2", london.Id);

            seed.Holidays.Add(new Holiday
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                Name = "Spring Bank Holiday (London only)",
                Date = holidayDate,
                Type = HolidayType.Public,
                LocationId = london.Id,
                IsActive = true,
            });

            await seed.SaveChangesAsync();
        }

        await using (var db = Db(londonUser))
        {
            var london = await Service(db, londonUser).SubmitPreApprovalAsync(Req(holidayDate));
            london.Value!.Multiplier.Should().Be(HolidayMultiplier, "it IS a London holiday");
        }

        await using (var db = Db(nyUser))
        {
            var ny = await Service(db, nyUser).SubmitPreApprovalAsync(Req(holidayDate));
            ny.Value!.Multiplier.Should().Be(
                WeekdayMultiplier, "a London-only holiday must not reach New York (symmetry)");
        }
    }

    /// <summary>
    /// A TENANT-WIDE holiday (no LocationId) must still reach every employee regardless of location — the
    /// BUG-286 fix narrows scope, and this proves it did not narrow it too far and silently drop the ordinary
    /// company-wide holiday case.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-154")]
    public async Task TenantWideHoliday_StillReachesEveryLocation()
    {
        var holidayDate = NextDay(DayOfWeek.Wednesday);
        Guid nyUser;

        await using (var seed = Db(Guid.NewGuid()))
        {
            seed.Shifts.Add(NewShift("General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true));

            var ny = NewLocation("New York");
            seed.Locations.Add(ny);

            SeedSettings(seed);
            (_, nyUser) = NewEmployee(seed, "NY-3", ny.Id);

            seed.Holidays.Add(new Holiday
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                Name = "New Year (company-wide)",
                Date = holidayDate,
                Type = HolidayType.Public,
                LocationId = null,           // tenant-wide
                IsActive = true,
            });

            await seed.SaveChangesAsync();
        }

        await using var db = Db(nyUser);
        var result = await Service(db, nyUser).SubmitPreApprovalAsync(Req(holidayDate));

        result.IsSuccess.Should().BeTrue(because: result.Error);
        result.Value!.Multiplier.Should().Be(
            HolidayMultiplier, "a tenant-wide holiday applies to every location — scope narrowed, not broken");
    }
}
