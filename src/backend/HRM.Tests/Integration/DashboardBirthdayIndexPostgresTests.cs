// ============================================================================
// DF-49 / US-RPT-005 / ISSUE-285(a): upcoming-birthdays index optimization — real-Postgres tests.
//
// The birthday widget was reshaped in #390 to ride a (tenant_id, birth_month_day) index instead of
// materializing every active employee. Three things about that change are only provable on real Postgres
// (an existing InMemory suite, DashboardIntegrationTests, covers the widget's LOGICAL window on InMemory):
//
//   1. The migration BACKFILL. For rows that existed before the column was added, the interceptor never
//      fires — the migration's UPDATE … EXTRACT(MONTH …)*100+EXTRACT(DAY …) is the ONLY thing that sets
//      birth_month_day. That EXTRACT arithmetic is Postgres SQL; InMemory can't run it.
//   2. windowKeys.Contains(e.BirthMonthDay.Value) must TRANSLATE to SQL on Npgsql. If it can't, the query
//      throws at runtime; InMemory evaluates the same Contains in the CLR and so can never prove translation.
//   3. The interceptor keeps birth_month_day in sync through a real numeric round-trip on a `date` DOB column.
//
// Harness copied from AttendanceSettingsCrudPostgresTests. UseSnakeCaseNamingConvention() is NOT optional.
// windowKeys are built with the SHIPPED DashboardService.BuildRecurringWindowKeys (internal, visible via
// InternalsVisibleTo "HRM.Tests"), so arm 2 exercises the real production key set — not a hand-rolled copy.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class DashboardBirthdayIndexPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db(Guid.NewGuid());
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

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

    /// <param name="withBirthInterceptor">
    /// When false the EmployeeBirthMonthDayInterceptor is NOT wired, so a seeded employee keeps whatever
    /// birth_month_day it was given (used by arm 1 to fake a pre-migration row the interceptor never touched).
    /// </param>
    private AppDbContext Db(Guid tenantId, bool withBirthInterceptor = true)
    {
        var tc = new FixedTenantContext { TenantId = tenantId };
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
            .UseSnakeCaseNamingConvention();

        builder = withBirthInterceptor
            ? builder.AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu), new EmployeeBirthMonthDayInterceptor())
            : builder.AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu));

        return new AppDbContext(builder.Options, tc);
    }

    // ── seeding ────────────────────────────────────────────────────────

    // A joining date far from every window used below, so the anniversary arm never pollutes the birthday set.
    private static readonly DateTime JoinFarFromWindow = new(2020, 8, 15);

    /// <summary>
    /// Seeds Department + JobTitle (both required FKs on real Postgres) + one Employee, and returns the id.
    /// Does not SaveChanges — the caller saves. birthMonthDayOverride lets arm 1 stamp a stale key.
    /// </summary>
    private static Guid NewEmployee(
        AppDbContext db, Guid tenantId, string no, DateTime? dob,
        EmployeeStatus status = EmployeeStatus.Active, int? birthMonthDayOverride = null)
    {
        var dept = new Department { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = $"D{no}", Code = no };
        var title = new JobTitle { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, TitleName = $"T{no}" };
        db.Departments.Add(dept);
        db.JobTitles.Add(title);

        var id = BaseEntity.NewUuidV7();
        var emp = new Employee
        {
            Id = id, TenantId = tenantId, EmployeeNo = no, FirstName = no, LastName = "W",
            Email = $"{no}@acme.test", DepartmentId = dept.Id, JobTitleId = title.Id,
            EmploymentType = EmploymentType.FullTime, Status = status,
            IsActive = status is EmployeeStatus.Active or EmployeeStatus.Probation,
            DateOfJoining = JoinFarFromWindow, DateOfBirth = dob,
        };
        if (birthMonthDayOverride is { } k)
            emp.BirthMonthDay = k;
        db.Employees.Add(emp);
        return id;
    }

    // ══ Arm 1 — the migration's EXTRACT backfill is what fixes pre-existing rows ══

    /// <summary>
    /// A row that predates the birth_month_day column never triggers the interceptor, so its key is stale
    /// (here explicitly 0). Only the migration's UPDATE … EXTRACT(MONTH …)*100 + EXTRACT(DAY …) sets it. This
    /// arm seeds such a row with the interceptor OFF (key stays 0), runs the EXACT backfill statement the
    /// migration runs, and asserts the key becomes month*100+day. The EXTRACT arithmetic is Postgres SQL —
    /// unrunnable on InMemory.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-RPT-005-49")]
    public async Task MigrationBackfill_Extract_SetsBirthMonthDay_ForPreExistingRows()
    {
        var tenantId = Guid.NewGuid();
        Guid empId;

        // Interceptor OFF → the stale key of 0 survives the save (simulates a pre-migration row).
        await using (var seed = Db(tenantId, withBirthInterceptor: false))
        {
            empId = NewEmployee(seed, tenantId, "OLD", dob: new DateTime(1990, 3, 5), birthMonthDayOverride: 0);
            await seed.SaveChangesAsync();
        }

        await using (var pre = Db(tenantId))
        {
            var row = await pre.Employees.AsNoTracking().SingleAsync(e => e.Id == empId);
            row.BirthMonthDay.Should().Be(0, "with the interceptor off, the stale pre-migration key is untouched");
        }

        // The EXACT statement from 20260719162127_AddEmployeeBirthMonthDayIndex.
        await using (var run = Db(tenantId))
        {
            await run.Database.ExecuteSqlRawAsync(
                "UPDATE employees " +
                "SET birth_month_day = (EXTRACT(MONTH FROM date_of_birth)::int * 100 + EXTRACT(DAY FROM date_of_birth)::int) " +
                "WHERE date_of_birth IS NOT NULL;");
        }

        await using (var verify = Db(tenantId))
        {
            var row = await verify.Employees.AsNoTracking().SingleAsync(e => e.Id == empId);
            row.BirthMonthDay.Should().Be(305, "the backfill EXTRACT(MONTH)*100+EXTRACT(DAY) sets Mar 5 → 305");
        }
    }

    // ══ Arm 2 — windowKeys.Contains(e.BirthMonthDay.Value) TRANSLATES to SQL ══

    /// <summary>
    /// Seeds employees with DOBs inside and outside a 7-day window and runs the widget's index-backed birthday
    /// query — built from the SHIPPED DashboardService.BuildRecurringWindowKeys — directly against Npgsql. Only
    /// the in-window employees come back, proving windowKeys.Contains(e.BirthMonthDay.Value) translates to SQL
    /// (if it couldn't, the query would throw). InMemory evaluates the same Contains in the CLR and can never
    /// prove translation.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-RPT-005-49")]
    public async Task BirthMonthDayWindowQuery_TranslatesToSql_ReturnsOnlyInWindow()
    {
        var tenantId = Guid.NewGuid();
        var today = new DateOnly(2026, 6, 17); // window = [06-17 .. 06-24] inclusive

        await using (var seed = Db(tenantId))
        {
            NewEmployee(seed, tenantId, "TODAY", dob: new DateTime(1985, 6, 17)); // 0617 — boundary in
            NewEmployee(seed, tenantId, "MID", dob: new DateTime(1990, 6, 20));   // 0620 — in
            NewEmployee(seed, tenantId, "DAY7", dob: new DateTime(1980, 6, 24));  // 0624 — inclusive in
            NewEmployee(seed, tenantId, "DAY8", dob: new DateTime(1990, 6, 25));  // 0625 — out
            NewEmployee(seed, tenantId, "PAST", dob: new DateTime(1990, 6, 10));  // 0610 — out
            NewEmployee(seed, tenantId, "NODOB", dob: null);                       // null key — out
            NewEmployee(seed, tenantId, "TERM", dob: new DateTime(1990, 6, 18), status: EmployeeStatus.Terminated); // in-window but wrong status
            await seed.SaveChangesAsync();
        }

        await using (var db = Db(tenantId))
        {
            var names = await RunBirthdayWindowQuery(db, today);
            names.Should().BeEquivalentTo("TODAY", "MID", "DAY7");
        }
    }

    /// <summary>
    /// The window keys wrap the year boundary (Dec 30 + 7d → 1230, 1231, 0101 … 0106); those wrapped keys must
    /// translate and match too. Only provable that the wrapped-int set survives SQL translation on Npgsql.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-RPT-005-49")]
    public async Task BirthMonthDayWindowQuery_WrapsYearEnd_OnRealPostgres()
    {
        var tenantId = Guid.NewGuid();
        var today = new DateOnly(2026, 12, 30); // window = [12-30, 12-31, 01-01 .. 01-06]

        await using (var seed = Db(tenantId))
        {
            NewEmployee(seed, tenantId, "DEC31", dob: new DateTime(1980, 12, 31)); // 1231 — in
            NewEmployee(seed, tenantId, "JAN2", dob: new DateTime(1990, 1, 2));    // 0102 — wraps in
            NewEmployee(seed, tenantId, "JAN6", dob: new DateTime(1995, 1, 6));    // 0106 — inclusive in
            NewEmployee(seed, tenantId, "JAN10", dob: new DateTime(1990, 1, 10));  // 0110 — out
            NewEmployee(seed, tenantId, "DEC20", dob: new DateTime(1990, 12, 20)); // 1220 — out
            await seed.SaveChangesAsync();
        }

        await using (var db = Db(tenantId))
        {
            var names = await RunBirthdayWindowQuery(db, today);
            names.Should().BeEquivalentTo("DEC31", "JAN2", "JAN6");
        }
    }

    /// <summary>
    /// The widget's exact index-backed birthday query: the same Active/Probation status predicate and the same
    /// windowKeys.Contains(...) filter the shipped UpcomingBirthdaysWidgetAsync uses, over the SHIPPED
    /// BuildRecurringWindowKeys. Executed against Npgsql, so it fails if the translation regresses.
    /// </summary>
    private static async Task<List<string>> RunBirthdayWindowQuery(AppDbContext db, DateOnly today)
    {
        var windowKeys = DashboardService.BuildRecurringWindowKeys(today, 7);

        return await db.Employees.AsNoTracking()
            .Where(e => e.Status == EmployeeStatus.Active || e.Status == EmployeeStatus.Probation)
            .Where(e => e.BirthMonthDay != null && windowKeys.Contains(e.BirthMonthDay.Value))
            .Select(e => e.FirstName)
            .ToListAsync();
    }

    // ══ Arm 3 — the interceptor keeps birth_month_day in sync on real Postgres ══

    /// <summary>
    /// With the interceptor wired, creating an employee stamps birth_month_day = month*100+day on the real
    /// `date` DOB column; changing the DOB recomputes it; clearing it nulls the key. Proves the interceptor's
    /// numeric round-trip survives Npgsql, not just the CLR change tracker.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-RPT-005-49")]
    public async Task BirthMonthDayInterceptor_KeepsKeyInSync_OnRealPostgres()
    {
        var tenantId = Guid.NewGuid();
        Guid empId;

        await using (var seed = Db(tenantId))
        {
            empId = NewEmployee(seed, tenantId, "BMD", dob: new DateTime(1990, 3, 5)); // Mar 5 → 305
            await seed.SaveChangesAsync();
        }

        await using (var db = Db(tenantId))
            (await db.Employees.AsNoTracking().SingleAsync(e => e.Id == empId))
                .BirthMonthDay.Should().Be(305);

        // Change DOB → the interceptor recomputes on update.
        await using (var db = Db(tenantId))
        {
            var e = await db.Employees.SingleAsync(x => x.Id == empId);
            e.DateOfBirth = new DateTime(1990, 11, 20); // Nov 20 → 1120
            await db.SaveChangesAsync();
        }

        await using (var db = Db(tenantId))
            (await db.Employees.AsNoTracking().SingleAsync(e => e.Id == empId))
                .BirthMonthDay.Should().Be(1120);

        // Clear DOB → key becomes null.
        await using (var db = Db(tenantId))
        {
            var e = await db.Employees.SingleAsync(x => x.Id == empId);
            e.DateOfBirth = null;
            await db.SaveChangesAsync();
        }

        await using (var db = Db(tenantId))
            (await db.Employees.AsNoTracking().SingleAsync(e => e.Id == empId))
                .BirthMonthDay.Should().BeNull();
    }
}
