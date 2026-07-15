// ============================================================================
// CAL-6 / US-CHR-013 — closes US-LV-002 AC-K1 ("FTE proration", carried as "not built" since the story shipped).
//
// LeaveEntitlementEngine.CalculateProRata has ALWAYS accepted `decimal fte = 1.0m` and always multiplied by it.
// The entitlement was never FTE-aware because all THREE callers passed the literal `fte: 1.0m`:
//   LeaveEntitlementService :415 (ComputeEffectiveEntitlementAsync)
//                           :522 (ComputeProratedEntitlementsBatchAsync)
//                           :603 (ProcessAccrualsAsync)
// The engine was never the bug; the wiring was. So these arms drive all THREE public entry points — a fix at
// one site with two left hardcoded MUST fail here.
//
// Fte defaults to 1.00 ⇒ × 1.0 ⇒ every existing employee's entitlement is unchanged. The `FullTime_` control
// arm pins that; it is the no-regression contract, not a formality.
//
// WHY POSTGRES: Fte maps to `numeric(3,2)`. InMemory keeps a C# decimal verbatim and would not round-trip the
// column's real precision, so a scale bug (0.333 → 0.33) would be invisible. Employee.UserId also carries a
// real FK Postgres enforces.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
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

public sealed class FteProrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();

    private const int LeaveYear = 2026;
    private const decimal FullYearEntitlement = 20m;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db();
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

    private AppDbContext Db()
    {
        var tc = new FixedTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.Email.Returns("hr@acme.test");

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

    private LeaveEntitlementService Service(AppDbContext db)
    {
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.Email.Returns("hr@acme.test");
        return new LeaveEntitlementService(
            db, new FixedTenantContext { TenantId = _tenantId }, cu,
            NullLogger<LeaveEntitlementService>.Instance);
    }

    // ── seeding ────────────────────────────────────────────────────────

    /// <summary>An employee employed for the WHOLE leave year, so the date ratio is 1.0 and FTE is the only variable.</summary>
    private Guid SeedEmployee(AppDbContext db, string no, decimal fte)
    {
        var dept = new Department { Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = $"D{no}", Code = no };
        var title = new JobTitle { Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, TitleName = $"T{no}" };
        db.Departments.Add(dept);
        db.JobTitles.Add(title);

        var id = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = id, TenantId = _tenantId, EmployeeNo = no, FirstName = no, LastName = "W",
            Email = $"{no}@acme.test",
            DateOfJoining = new DateTime(2020, 1, 1),   // long before the leave year → ratio 1.0
            DepartmentId = dept.Id, JobTitleId = title.Id,
            Status = EmployeeStatus.Active, IsActive = true,
            Fte = fte,
        });
        return id;
    }

    private Guid SeedLeaveType(AppDbContext db)
    {
        var id = BaseEntity.NewUuidV7();
        db.LeaveTypes.Add(new LeaveType
        {
            Id = id, TenantId = _tenantId, Name = "Annual Leave",
            AnnualEntitlement = FullYearEntitlement,
            AccrualFrequency = AccrualFrequency.Upfront,
            Gender = LeaveTypeGender.All, IsActive = true,
        });
        return id;
    }

    // ══ SITE 1 — ComputeEffectiveEntitlementAsync (:415) ══

    /// <summary>
    /// CONTROL: a 1.00-FTE employee (the default, and every existing employee) gets the FULL entitlement.
    /// Pins the no-regression contract — CAL-6 must not move anyone already on the books.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-326")]
    public async Task FullTimeEmployee_EntitlementIsUnchanged()
    {
        Guid empId, typeId;
        await using (var seed = Db())
        {
            empId = SeedEmployee(seed, "FT1", fte: 1.00m);
            typeId = SeedLeaveType(seed);
            await seed.SaveChangesAsync();
        }

        await using var db = Db();
        var result = await Service(db).ComputeEffectiveEntitlementAsync(empId, typeId, LeaveYear);

        result.IsSuccess.Should().BeTrue(because: result.Error);
        result.Value!.ProratedEntitlementDays.Should().Be(
            20m, "a full-time employee employed all year gets the full 20 — FTE 1.00 multiplies by 1");
    }

    /// <summary>
    /// US-LV-002 AC-K1 (site 1): a 0.50-FTE employee employed the whole year gets EXACTLY half — 20 → 10.
    /// Pre-fix this returned 20: the engine multiplied by a hardcoded 1.0.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-326")]
    public async Task HalfFteEmployee_GetsExactlyHalfTheEntitlement_ComputeEffectivePath()
    {
        Guid empId, typeId;
        await using (var seed = Db())
        {
            empId = SeedEmployee(seed, "PT1", fte: 0.50m);
            typeId = SeedLeaveType(seed);
            await seed.SaveChangesAsync();
        }

        await using var db = Db();
        var result = await Service(db).ComputeEffectiveEntitlementAsync(empId, typeId, LeaveYear);

        result.IsSuccess.Should().BeTrue(because: result.Error);
        result.Value!.ProratedEntitlementDays.Should().Be(
            10m, "0.5 FTE of a 20-day entitlement is 10 — pre-fix the hardcoded fte: 1.0m returned 20");
    }

    // ══ SITE 2 — ComputeProratedEntitlementsBatchAsync (:522) ══

    /// <summary>
    /// US-LV-002 AC-K1 (site 2): the BATCH path must honour FTE too. This is a SEPARATE call site from site 1 —
    /// fixing one and leaving this hardcoded would pass the arm above and still ship the bug.
    /// Both employees are asserted in one call so the batch cannot pass by ignoring one of them.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-326")]
    public async Task BatchPath_HonoursFte_PerEmployee()
    {
        Guid fullId, halfId, typeId;
        await using (var seed = Db())
        {
            fullId = SeedEmployee(seed, "BFT", fte: 1.00m);
            halfId = SeedEmployee(seed, "BPT", fte: 0.50m);
            typeId = SeedLeaveType(seed);
            await seed.SaveChangesAsync();
        }

        await using var db = Db();
        var employees = await db.Employees.AsNoTracking().ToListAsync();
        var types = await db.LeaveTypes.AsNoTracking().ToListAsync();

        var result = await Service(db).ComputeProratedEntitlementsBatchAsync(employees, types, LeaveYear);

        result[(fullId, typeId)].Should().Be(20m, "the full-timer is unchanged");
        result[(halfId, typeId)].Should().Be(
            10m, "the batch path must pass employee.Fte — pre-fix it hardcoded fte: 1.0m");
    }

    // ══ SITE 3 — ProcessAccrualsAsync (:603) ══

    /// <summary>
    /// US-LV-002 AC-K1 (site 3): the ACCRUAL job path must honour FTE. The third independent call site — the
    /// one that actually writes the employee's ledger, so a miss here silently over-credits a part-timer's
    /// balance every accrual run.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-326")]
    public async Task AccrualPath_HonoursFte_WhenCreditingTheLedger()
    {
        Guid halfId, typeId;
        await using (var seed = Db())
        {
            halfId = SeedEmployee(seed, "APT", fte: 0.50m);
            typeId = SeedLeaveType(seed);
            await seed.SaveChangesAsync();
        }

        await using (var run = Db())
        {
            await Service(run).ProcessAccrualsAsync(LeaveYear);
        }

        await using var db = Db();
        var entries = await db.LeaveLedgerEntries.AsNoTracking()
            .Where(l => l.EmployeeId == halfId && l.LeaveTypeId == typeId && l.LeaveYear == LeaveYear)
            .ToListAsync();

        entries.Should().NotBeEmpty("the accrual job must credit the part-timer");
        entries.Sum(e => e.Amount).Should().Be(
            10m, "0.5 FTE of 20 days — pre-fix the accrual path hardcoded fte: 1.0m and credited 20");
    }

    // ══ The numeric(3,2) column ══

    /// <summary>
    /// Fte maps to `numeric(3,2)`. This pins the column's real scale on the real provider: 0.25 round-trips
    /// exactly and prorates 20 → 5. InMemory would keep any decimal verbatim and hide a scale mismatch.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-326")]
    public async Task Fte_RoundTripsAtNumeric3_2_AndProratesExactly()
    {
        Guid empId, typeId;
        await using (var seed = Db())
        {
            empId = SeedEmployee(seed, "QPT", fte: 0.25m);
            typeId = SeedLeaveType(seed);
            await seed.SaveChangesAsync();
        }

        await using var db = Db();
        var reloaded = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == empId);
        reloaded.Fte.Should().Be(0.25m, "numeric(3,2) round-trip");

        var result = await Service(db).ComputeEffectiveEntitlementAsync(empId, typeId, LeaveYear);
        result.Value!.ProratedEntitlementDays.Should().Be(5m, "0.25 * 20");
    }

    /// <summary>
    /// The column default is 1.00, so an employee row written WITHOUT an explicit Fte (every pre-CAL-6 row, and
    /// any insert path that doesn't set it) prorates unchanged. This is what makes the migration safe on live data.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-326")]
    public async Task EmployeeWrittenWithoutAnFte_DefaultsTo1_00_AndIsUnchanged()
    {
        Guid empId, typeId;
        await using (var seed = Db())
        {
            var dept = new Department { Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "DD", Code = "DD" };
            var title = new JobTitle { Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, TitleName = "TT" };
            seed.Departments.Add(dept);
            seed.JobTitles.Add(title);

            empId = BaseEntity.NewUuidV7();
            seed.Employees.Add(new Employee
            {
                Id = empId, TenantId = _tenantId, EmployeeNo = "DEF", FirstName = "Def", LastName = "W",
                Email = "def@acme.test", DateOfJoining = new DateTime(2020, 1, 1),
                DepartmentId = dept.Id, JobTitleId = title.Id,
                Status = EmployeeStatus.Active, IsActive = true,
                // Fte deliberately NOT set — the entity initializer + column default must supply 1.00.
            });
            typeId = SeedLeaveType(seed);
            await seed.SaveChangesAsync();
        }

        await using var db = Db();
        var reloaded = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == empId);
        reloaded.Fte.Should().Be(1.00m);

        var result = await Service(db).ComputeEffectiveEntitlementAsync(empId, typeId, LeaveYear);
        result.Value!.ProratedEntitlementDays.Should().Be(20m, "an un-set FTE must never shrink an entitlement");
    }
}
