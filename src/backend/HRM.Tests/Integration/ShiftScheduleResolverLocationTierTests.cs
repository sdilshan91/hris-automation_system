// ============================================================================
// US-ATT-011 AC-2 (CAL-1 / plan Task 1.2) — ShiftScheduleResolver four-tier working-day resolution:
//   Employee (EmployeeShift) → Location (Location.DefaultShiftId) → Tenant (Shift.IsDefault) → code default.
//
// Covers TC-ATT-147 (Gulf Sun-Thu via the Location tier: Sunday IS a workday, Friday is NOT),
// TC-ATT-148 (EU 4-day week {1,2,3,4} → exactly 4 working days per calendar week),
// TC-ATT-149 (single-branch tenant: empty Location tier falls through to the tenant Mon-Fri default,
// and with no tenant default at all, to the Mon-Fri CODE default), plus the tier-1 precedence arm
// (a personal EmployeeShift BEATS the Location default) and a batching (no-N+1) arm for FR-3.
//
// WHY POSTGRES (not InMemory): these arms depend on real FK behaviour (Location.DefaultShiftId → shift), on
// the migrated `default_shift_id` column, and — for the batching arm — on real command counts against a real
// provider (the InMemory provider does not issue comparable DbCommands, so the FR-3 query-count bound is
// unmeasurable there). NOTE (corrected 2026-07-15): EF global query filters ARE provider-independent and do
// work on InMemory — that is NOT a reason for this harness, and must not be cited to skip a Postgres arm
// elsewhere. Uses the project's Testcontainers/Postgres harness (mirrors ShiftNameTrimDuplicatePostgresTests).
//
// EACH TEST USES ITS OWN TENANT ID: the resolver runs entirely under the global tenant query filter, so a
// per-test tenant keeps the scenarios (which differ in whether a tenant default shift exists at all)
// hermetically separated inside one container.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class ShiftScheduleResolverLocationTierTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    // The as-of / count window: Mon 2026-07-06 .. Sun 2026-07-12 — one full ISO calendar week.
    private static readonly DateOnly WeekMonday = new(2026, 7, 6);
    private static readonly DateOnly WeekSunday = new(2026, 7, 12);

    private const int Fri = 5, Sat = 6, Sun = 7;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db(Guid.NewGuid());
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── Harness ────────────────────────────────────────────────────────

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
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
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    private AppDbContext Db(Guid tenantId, DbCommandInterceptor? spy = null)
    {
        var tc = new MutableTenantContext { TenantId = tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.Email.Returns("hr@acme.test");

        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu));

        if (spy is not null) builder.AddInterceptors(spy);

        return new AppDbContext(builder.Options, tc);
    }

    /// <summary>Counts the SQL commands EF actually executes — the FR-3 "no N+1" assertion.</summary>
    private sealed class CommandCountingInterceptor : DbCommandInterceptor
    {
        public int Count;

        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    // ── Seeding ────────────────────────────────────────────────────────

    private static Shift NewShift(Guid tenantId, string name, int[] workingDays, bool isDefault = false) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = tenantId,
        Name = name,
        Type = ShiftType.Single,
        StartTime = new TimeOnly(9, 0),
        EndTime = new TimeOnly(17, 0),
        WorkingDays = workingDays.ToList(),
        IsDefault = isDefault,
        IsActive = true,
    };

    private static Location NewLocation(Guid tenantId, string name, Guid? defaultShiftId) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = tenantId,
        Name = name,
        TimeZone = "Asia/Dubai",
        DefaultShiftId = defaultShiftId,
        IsActive = true,
    };

    /// <summary>Seeds the Department + JobTitle every Employee row requires, returning their ids.</summary>
    private static (Department dept, JobTitle title) NewOrgUnits(Guid tenantId)
    {
        var dept = new Department
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "Ops", Code = "OPS",
        };
        var title = new JobTitle
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, TitleName = "Analyst",
        };
        return (dept, title);
    }

    private static Employee NewEmployee(
        Guid tenantId, Guid deptId, Guid titleId, string employeeNo, Guid? locationId) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = tenantId,
        EmployeeNo = employeeNo,
        FirstName = "Test",
        LastName = employeeNo,
        Email = $"{employeeNo.ToLowerInvariant()}@acme.test",
        Status = EmployeeStatus.Active,
        DepartmentId = deptId,
        JobTitleId = titleId,
        LocationId = locationId,
    };

    // ══ TC-ATT-147 — Location tier: Gulf Sun-Thu ═══════════════════════

    /// <summary>
    /// TC-ATT-147 (AC-2a): an employee with NO personal shift at a Gulf Location whose DefaultShiftId is a
    /// Sun-Thu shift resolves {7,1,2,3,4} from the LOCATION tier — Sunday is a working day, Friday is not —
    /// overriding the tenant Mon-Fri default.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-147")]
    public async Task Resolver_LocationTier_GulfSunThu_SundayIsWorkday_FridayIsNot()
    {
        var tenantId = Guid.NewGuid();
        Guid empId;

        await using (var db = Db(tenantId))
        {
            var tenantDefault = NewShift(tenantId, "General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true);
            var gulf = NewShift(tenantId, "Gulf Sun-Thu", [7, 1, 2, 3, 4]);
            db.Shifts.AddRange(tenantDefault, gulf);

            var dubai = NewLocation(tenantId, "Dubai Branch", gulf.Id);
            db.Locations.Add(dubai);

            var (dept, title) = NewOrgUnits(tenantId);
            db.Departments.Add(dept);
            db.JobTitles.Add(title);

            var emp = NewEmployee(tenantId, dept.Id, title.Id, "GULF-1", dubai.Id);
            db.Employees.Add(emp);
            empId = emp.Id;

            await db.SaveChangesAsync();
        }

        await using var read = Db(tenantId);
        var sets = await ShiftScheduleResolver.ResolveWorkingDaySetsAsync(
            read, [empId], WeekMonday, CancellationToken.None);

        // Step 1: the set came from the Location shift, NOT the tenant Mon-Fri default.
        sets[empId].Should().BeEquivalentTo(new[] { 7, 1, 2, 3, 4 });
        // Steps 2-3: the load-bearing assertions — Sunday works, Friday does not.
        sets[empId].Should().Contain(Sun, "Sunday (ISO 7) is a working day on a Sun-Thu calendar");
        sets[empId].Should().NotContain(Fri, "Friday (ISO 5) is the Gulf weekend");

        // The count over a Mon-Sun week is 5 (Sun + Mon-Thu), not the Mon-Fri 5 by coincidence of the
        // same size: Friday is excluded and Sunday included.
        ShiftScheduleResolver.CountWorkingDays(sets[empId], WeekMonday, WeekSunday).Should().Be(5);
    }

    // ══ TC-ATT-148 — Location tier: EU 4-day week ══════════════════════

    /// <summary>
    /// TC-ATT-148 (AC-2b): an EU Location with a 4-day shift {1,2,3,4} resolves exactly 4 working days over a
    /// full Mon-Sun week — proving the resolver honours an arbitrary set, not just Mon-Fri.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-148")]
    public async Task Resolver_LocationTier_EuFourDayWeek_CountsExactlyFourWorkingDays()
    {
        var tenantId = Guid.NewGuid();
        Guid empId;

        await using (var db = Db(tenantId))
        {
            var tenantDefault = NewShift(tenantId, "General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true);
            var fourDay = NewShift(tenantId, "EU Mon-Thu", [1, 2, 3, 4]);
            db.Shifts.AddRange(tenantDefault, fourDay);

            var amsterdam = NewLocation(tenantId, "Amsterdam Branch", fourDay.Id);
            db.Locations.Add(amsterdam);

            var (dept, title) = NewOrgUnits(tenantId);
            db.Departments.Add(dept);
            db.JobTitles.Add(title);

            var emp = NewEmployee(tenantId, dept.Id, title.Id, "EU-1", amsterdam.Id);
            db.Employees.Add(emp);
            empId = emp.Id;

            await db.SaveChangesAsync();
        }

        await using var read = Db(tenantId);
        var sets = await ShiftScheduleResolver.ResolveWorkingDaySetsAsync(
            read, [empId], WeekMonday, CancellationToken.None);

        sets[empId].Should().BeEquivalentTo(new[] { 1, 2, 3, 4 });
        ShiftScheduleResolver.CountWorkingDays(sets[empId], WeekMonday, WeekSunday)
            .Should().Be(4, "Fri/Sat/Sun are all non-working on a 4-day calendar");
        sets[empId].Should().NotContain(Fri);
        sets[empId].Should().NotContain(Sat);
        sets[empId].Should().NotContain(Sun);
    }

    // ══ TC-ATT-149 — fall-through ══════════════════════════════════════

    /// <summary>
    /// TC-ATT-149 step 1-2 (AC-2c / BR-4): a single-branch tenant whose Location has NO DefaultShiftId falls
    /// through the empty Location tier to the TENANT default shift — backward compatibility.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-149")]
    public async Task Resolver_EmptyLocationTier_FallsThroughToTenantDefault()
    {
        var tenantId = Guid.NewGuid();
        Guid empId;

        await using (var db = Db(tenantId))
        {
            // The tenant default is deliberately Mon-SAT, NOT Mon-Fri: the code default (tier 4) is
            // Mon-Fri, so a Mon-Fri tenant default would make tiers 3 and 4 indistinguishable and this
            // arm would still pass with tier 3 deleted entirely. Saturday is the discriminator — only
            // the TENANT default can put Sat in the set.
            db.Shifts.Add(NewShift(tenantId, "General Mon-Sat", [1, 2, 3, 4, 5, 6], isDefault: true));

            // Location tier deliberately EMPTY (null DefaultShiftId).
            var colombo = NewLocation(tenantId, "Colombo HQ", defaultShiftId: null);
            db.Locations.Add(colombo);

            var (dept, title) = NewOrgUnits(tenantId);
            db.Departments.Add(dept);
            db.JobTitles.Add(title);

            var emp = NewEmployee(tenantId, dept.Id, title.Id, "SB-1", colombo.Id);
            db.Employees.Add(emp);
            empId = emp.Id;

            await db.SaveChangesAsync();
        }

        await using var read = Db(tenantId);
        var sets = await ShiftScheduleResolver.ResolveWorkingDaySetsAsync(
            read, [empId], WeekMonday, CancellationToken.None);

        sets[empId].Should().BeEquivalentTo(
            new[] { 1, 2, 3, 4, 5, 6 }, "tier 3 (the tenant default) supplied the set, not tier 4");
        sets[empId].Should().Contain(
            Sat, "Saturday proves the TENANT default was used — the Mon-Fri code default excludes it");
        sets[empId].Should().Contain(Fri, "Friday IS a working day on Mon-Sat");
        sets[empId].Should().NotContain(Sun);
        ShiftScheduleResolver.CountWorkingDays(sets[empId], WeekMonday, WeekSunday).Should().Be(6);
    }

    /// <summary>
    /// TC-ATT-149 step 3 (AC-2c) — **THE BEHAVIOUR-CHANGE REGRESSION ARM**. A tenant with NO default shift,
    /// no location default and no personal assignment previously resolved to an EMPTY set, which every caller
    /// treats as "every calendar day is a working day" (7/week — weekends paid/counted as work; reachable in
    /// production because ShiftService.DeleteAsync has no IsDefault guard, BUG-287). It must now resolve to
    /// the Mon-Fri CODE DEFAULT {1,2,3,4,5} = 5 working days per week.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-149")]
    public async Task Resolver_NoShiftConfiguredAtAll_ResolvesCodeDefaultMonFri_NotEmptySet()
    {
        var tenantId = Guid.NewGuid();
        Guid empId;

        await using (var db = Db(tenantId))
        {
            // NO shifts at all for this tenant: no IsDefault row, no location default, no assignment.
            var (dept, title) = NewOrgUnits(tenantId);
            db.Departments.Add(dept);
            db.JobTitles.Add(title);

            var emp = NewEmployee(tenantId, dept.Id, title.Id, "BARE-1", locationId: null);
            db.Employees.Add(emp);
            empId = emp.Id;

            await db.SaveChangesAsync();
        }

        await using var read = Db(tenantId);
        var sets = await ShiftScheduleResolver.ResolveWorkingDaySetsAsync(
            read, [empId], WeekMonday, CancellationToken.None);

        sets[empId].Should().NotBeEmpty(
            "the resolver must never return an empty set — an empty set means 'every calendar day is a "
            + "working day' to every caller, which is the defect this tier closes");
        sets[empId].Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 });
        sets[empId].Should().BeEquivalentTo(ShiftScheduleResolver.CodeDefaultWorkingDays);
        ShiftScheduleResolver.CountWorkingDays(sets[empId], WeekMonday, WeekSunday)
            .Should().Be(5, "the code default is Mon-Fri — NOT 7 calendar days");
    }

    // ══ Tier-1 precedence ══════════════════════════════════════════════

    /// <summary>
    /// AC-2 / BR-3 precedence: a personal effective-dated EmployeeShift BEATS the Location default (tier 1
    /// over tier 2). The Gulf-located employee is personally assigned the Mon-Fri shift and must resolve
    /// Mon-Fri, not the location's Sun-Thu.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-147")]
    public async Task Resolver_PersonalAssignment_BeatsLocationDefault()
    {
        var tenantId = Guid.NewGuid();
        Guid empId;

        await using (var db = Db(tenantId))
        {
            // The personal shift is deliberately Mon-WED — distinct from BOTH the location default (Sun-Thu)
            // AND the tenant default (Mon-Fri). If the personal shift were itself the tenant default, a
            // mutation where tier 1 "resolves" but returns `defaultDays` would survive this arm.
            var tenantDefault = NewShift(tenantId, "General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true);
            var monWed = NewShift(tenantId, "Part-time Mon-Wed", [1, 2, 3]);
            var gulf = NewShift(tenantId, "Gulf Sun-Thu", [7, 1, 2, 3, 4]);
            db.Shifts.AddRange(tenantDefault, monWed, gulf);

            var dubai = NewLocation(tenantId, "Dubai Branch", gulf.Id);
            db.Locations.Add(dubai);

            var (dept, title) = NewOrgUnits(tenantId);
            db.Departments.Add(dept);
            db.JobTitles.Add(title);

            var emp = NewEmployee(tenantId, dept.Id, title.Id, "EXPAT-1", dubai.Id);
            db.Employees.Add(emp);
            empId = emp.Id;

            // Personal assignment effective before the as-of date, still open.
            db.EmployeeShifts.Add(new EmployeeShift
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                EmployeeId = emp.Id,
                ShiftId = monWed.Id,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                EffectiveTo = null,
            });

            await db.SaveChangesAsync();
        }

        await using var read = Db(tenantId);
        var sets = await ShiftScheduleResolver.ResolveWorkingDaySetsAsync(
            read, [empId], WeekMonday, CancellationToken.None);

        sets[empId].Should().BeEquivalentTo(new[] { 1, 2, 3 },
            "the personal EmployeeShift is tier 1 and outranks BOTH the Dubai location default and the "
            + "tenant default — Mon-Wed is produced by no other tier");
        sets[empId].Should().NotContain(Fri, "Mon-Wed excludes Friday — the tenant default would include it");
        sets[empId].Should().NotContain(Sun, "the Gulf location default would include Sunday; tier 1 wins");
    }

    // ══ FR-3 — batching / no N+1 ═══════════════════════════════════════

    /// <summary>
    /// FR-3: resolution is batched — the query count is FLAT in employee count. Resolves 60 employees spread
    /// across 2 locations (+ an unlocated group) and asserts both correctness at scale and that EF issued at
    /// most 5 SELECTs total (default shift, assignments, employees' locations, locations' default shifts,
    /// referenced shifts) — an N+1 would be ~120+.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-147")]
    public async Task Resolver_ResolvesManyEmployeesAcrossLocations_WithoutNPlusOne()
    {
        var tenantId = Guid.NewGuid();
        var gulfEmpIds = new List<Guid>();
        var euEmpIds = new List<Guid>();
        var bareEmpIds = new List<Guid>();

        await using (var db = Db(tenantId))
        {
            var monFri = NewShift(tenantId, "General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true);
            var gulf = NewShift(tenantId, "Gulf Sun-Thu", [7, 1, 2, 3, 4]);
            var fourDay = NewShift(tenantId, "EU Mon-Thu", [1, 2, 3, 4]);
            db.Shifts.AddRange(monFri, gulf, fourDay);

            var dubai = NewLocation(tenantId, "Dubai Branch", gulf.Id);
            var amsterdam = NewLocation(tenantId, "Amsterdam Branch", fourDay.Id);
            db.Locations.AddRange(dubai, amsterdam);

            var (dept, title) = NewOrgUnits(tenantId);
            db.Departments.Add(dept);
            db.JobTitles.Add(title);

            for (int i = 0; i < 20; i++)
            {
                var g = NewEmployee(tenantId, dept.Id, title.Id, $"G-{i}", dubai.Id);
                var e = NewEmployee(tenantId, dept.Id, title.Id, $"E-{i}", amsterdam.Id);
                var b = NewEmployee(tenantId, dept.Id, title.Id, $"B-{i}", locationId: null);
                db.Employees.AddRange(g, e, b);
                gulfEmpIds.Add(g.Id);
                euEmpIds.Add(e.Id);
                bareEmpIds.Add(b.Id);
            }

            await db.SaveChangesAsync();
        }

        var allIds = gulfEmpIds.Concat(euEmpIds).Concat(bareEmpIds).ToList();
        allIds.Count.Should().Be(60);

        var spy = new CommandCountingInterceptor();
        await using var read = Db(tenantId, spy);
        var sets = await ShiftScheduleResolver.ResolveWorkingDaySetsAsync(
            read, allIds, WeekMonday, CancellationToken.None);

        // Correctness at scale: each cohort resolved from its own tier.
        sets.Should().HaveCount(60);
        gulfEmpIds.Should().OnlyContain(id => sets[id].SetEquals(new[] { 7, 1, 2, 3, 4 }));
        euEmpIds.Should().OnlyContain(id => sets[id].SetEquals(new[] { 1, 2, 3, 4 }));
        bareEmpIds.Should().OnlyContain(id => sets[id].SetEquals(new[] { 1, 2, 3, 4, 5 }));

        // FR-3: flat query count regardless of the 60 employees.
        spy.Count.Should().BeLessThanOrEqualTo(5,
            "resolution must be batched — a per-employee round-trip would issue dozens of SELECTs");
    }

    // ══ Empty-WorkingDays shifts must NOT resolve (integration-enforcer HOLE 1) ════

    /// <summary>
    /// A Location wired to a FLEXIBLE shift that declares NO WorkingDays must fall through to the tenant
    /// default — it must NOT resolve to an empty set. This is the whole point of US-ATT-011: an empty set is
    /// the <c>CountWorkingDays</c> sentinel for "every calendar day is a working day", so returning one from
    /// tier 2 would silently give this Location's employees a 7-DAY working week and inflate every payroll
    /// pro-rata denominator (~22 → ~30). Reachable in production: ShiftRequestValidator only enforces
    /// WorkingDays.NotEmpty for Single/Rotating shifts, so a Flexible shift legitimately has none, and
    /// LocationService only validates that the shift is active — not that it declares a calendar.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-149")]
    public async Task Resolver_LocationDefaultShiftWithNoWorkingDays_FallsThroughToTenantDefault_NotEmptySet()
    {
        var tenantId = Guid.NewGuid();
        Guid empId;

        await using (var db = Db(tenantId))
        {
            // Mon-SAT tenant default so this arm also proves it landed on tier 3, not tier 4 (both would
            // otherwise be Mon-Fri and indistinguishable).
            db.Shifts.Add(NewShift(tenantId, "General Mon-Sat", [1, 2, 3, 4, 5, 6], isDefault: true));

            var flexible = NewShift(tenantId, "Flexible - no declared days", []);
            flexible.Type = ShiftType.Flexible;
            db.Shifts.Add(flexible);

            var remoteHub = NewLocation(tenantId, "Remote Hub", flexible.Id);
            db.Locations.Add(remoteHub);

            var (dept, title) = NewOrgUnits(tenantId);
            db.Departments.Add(dept);
            db.JobTitles.Add(title);

            var emp = NewEmployee(tenantId, dept.Id, title.Id, "FLEX-1", remoteHub.Id);
            db.Employees.Add(emp);
            empId = emp.Id;

            await db.SaveChangesAsync();
        }

        await using var read = Db(tenantId);
        var sets = await ShiftScheduleResolver.ResolveWorkingDaySetsAsync(
            read, [empId], WeekMonday, CancellationToken.None);

        sets[empId].Should().NotBeEmpty(
            "an empty set means 'every calendar day is a working day' — the exact 7-day-week bug this "
            + "feature exists to remove");
        sets[empId].Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5, 6 },
            "a shift declaring no working days is not a calendar, so tier 2 must NOT resolve and the "
            + "tenant default (tier 3) supplies the set");
        sets[empId].Should().Contain(Sat, "Saturday proves tier 3 (Mon-Sat) supplied it, not tier 4 (Mon-Fri)");
        ShiftScheduleResolver.CountWorkingDays(sets[empId], WeekMonday, WeekSunday).Should().Be(6,
            "6 working days — NOT 7, which is what an empty set would have counted");
    }

    /// <summary>
    /// The same guard on tier 3: a FLEXIBLE tenant default declaring no WorkingDays must fall through to the
    /// Mon-Fri code default (tier 4) rather than yielding an empty set (a 7-day week).
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-149")]
    public async Task Resolver_TenantDefaultShiftWithNoWorkingDays_FallsThroughToCodeDefault_NotEmptySet()
    {
        var tenantId = Guid.NewGuid();
        Guid empId;

        await using (var db = Db(tenantId))
        {
            var flexibleDefault = NewShift(tenantId, "Flexible default - no days", [], isDefault: true);
            flexibleDefault.Type = ShiftType.Flexible;
            db.Shifts.Add(flexibleDefault);

            var (dept, title) = NewOrgUnits(tenantId);
            db.Departments.Add(dept);
            db.JobTitles.Add(title);

            var emp = NewEmployee(tenantId, dept.Id, title.Id, "FLEXD-1", locationId: null);
            db.Employees.Add(emp);
            empId = emp.Id;

            await db.SaveChangesAsync();
        }

        await using var read = Db(tenantId);
        var sets = await ShiftScheduleResolver.ResolveWorkingDaySetsAsync(
            read, [empId], WeekMonday, CancellationToken.None);

        sets[empId].Should().NotBeEmpty("a no-days tenant default must not become a 7-day working week");
        sets[empId].Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 }, "tier 4, the Mon-Fri code default");
        ShiftScheduleResolver.CountWorkingDays(sets[empId], WeekMonday, WeekSunday).Should().Be(5);
    }

    // ══ Tier-1 effective-dating (test-authenticator: previously ZERO coverage) ════

    /// <summary>
    /// An EXPIRED personal assignment (<c>EffectiveTo</c> before the as-of date) must NOT apply — the
    /// employee falls back to the Location tier. Without this arm the <c>EffectiveTo &gt;= asOf</c> clause
    /// (<c>ShiftScheduleResolver</c> tier-1 query) is a fully surviving mutant on a payroll path.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-147")]
    public async Task Resolver_ExpiredPersonalAssignment_FallsBackToLocationTier()
    {
        var tenantId = Guid.NewGuid();
        Guid empId;

        await using (var db = Db(tenantId))
        {
            var tenantDefault = NewShift(tenantId, "General Mon-Fri", [1, 2, 3, 4, 5], isDefault: true);
            var monWed = NewShift(tenantId, "Part-time Mon-Wed", [1, 2, 3]);
            var gulf = NewShift(tenantId, "Gulf Sun-Thu", [7, 1, 2, 3, 4]);
            db.Shifts.AddRange(tenantDefault, monWed, gulf);

            var dubai = NewLocation(tenantId, "Dubai Branch", gulf.Id);
            db.Locations.Add(dubai);

            var (dept, title) = NewOrgUnits(tenantId);
            db.Departments.Add(dept);
            db.JobTitles.Add(title);

            var emp = NewEmployee(tenantId, dept.Id, title.Id, "EXPIRED-1", dubai.Id);
            db.Employees.Add(emp);
            empId = emp.Id;

            // Expired the day BEFORE the as-of date → must not apply.
            db.EmployeeShifts.Add(new EmployeeShift
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                EmployeeId = emp.Id,
                ShiftId = monWed.Id,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                EffectiveTo = WeekMonday.AddDays(-1),
            });

            // Future-dated → must not apply either (EffectiveFrom > asOf).
            db.EmployeeShifts.Add(new EmployeeShift
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                EmployeeId = emp.Id,
                ShiftId = monWed.Id,
                EffectiveFrom = WeekSunday.AddDays(1),
                EffectiveTo = null,
            });

            await db.SaveChangesAsync();
        }

        await using var read = Db(tenantId);
        var sets = await ShiftScheduleResolver.ResolveWorkingDaySetsAsync(
            read, [empId], WeekMonday, CancellationToken.None);

        sets[empId].Should().BeEquivalentTo(new[] { 7, 1, 2, 3, 4 },
            "neither the expired nor the future-dated assignment is in effect at asOf, so tier 2 (the Gulf "
            + "location default) supplies the set");
        sets[empId].Should().Contain(Sun, "Sunday is a Gulf workday — proves tier 2 applied");
        sets[empId].Should().NotContain(Fri, "Friday is the Gulf weekend");
    }

    /// <summary>
    /// When two personal assignments are both in effect at the as-of date, the one with the LATEST
    /// <c>EffectiveFrom</c> wins. Pins the <c>OrderByDescending(a =&gt; a.EffectiveFrom)</c> tie-break —
    /// mutating it to <c>OrderBy</c> otherwise breaks nothing.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-147")]
    public async Task Resolver_StackedAssignments_LatestEffectiveFromWins()
    {
        var tenantId = Guid.NewGuid();
        Guid empId;

        await using (var db = Db(tenantId))
        {
            var older = NewShift(tenantId, "Old Mon-Sat", [1, 2, 3, 4, 5, 6], isDefault: true);
            var newer = NewShift(tenantId, "New Mon-Wed", [1, 2, 3]);
            db.Shifts.AddRange(older, newer);

            var (dept, title) = NewOrgUnits(tenantId);
            db.Departments.Add(dept);
            db.JobTitles.Add(title);

            var emp = NewEmployee(tenantId, dept.Id, title.Id, "STACK-1", locationId: null);
            db.Employees.Add(emp);
            empId = emp.Id;

            // Both OPEN and both in effect at asOf — only EffectiveFrom separates them.
            db.EmployeeShifts.Add(new EmployeeShift
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = emp.Id,
                ShiftId = older.Id, EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = null,
            });
            db.EmployeeShifts.Add(new EmployeeShift
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = emp.Id,
                ShiftId = newer.Id, EffectiveFrom = new DateOnly(2026, 6, 1), EffectiveTo = null,
            });

            await db.SaveChangesAsync();
        }

        await using var read = Db(tenantId);
        var sets = await ShiftScheduleResolver.ResolveWorkingDaySetsAsync(
            read, [empId], WeekMonday, CancellationToken.None);

        sets[empId].Should().BeEquivalentTo(new[] { 1, 2, 3 },
            "the assignment with the latest EffectiveFrom (2026-06-01, Mon-Wed) wins");
        sets[empId].Should().NotContain(Sat, "Saturday would mean the OLDER assignment was picked");
    }
}
