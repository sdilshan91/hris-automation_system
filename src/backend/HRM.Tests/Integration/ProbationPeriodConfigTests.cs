// ============================================================================
// CAL-7 / ISSUE-304 — US-CHR-009 BR-6: the probation period is CONFIGURABLE.
//
// EmployeeStatusService.CheckProbationEndDatesAsync hardcoded `DateOfJoining.AddDays(90)` — inlined straight
// into the SQL predicate — so BR-6's "configured per tenant" was never honoured. The effective period is now
// Location.ProbationPeriodDays ?? Tenant.ProbationPeriodDays (code default 90).
//
// The reminder DISPATCH is the observable: the sweep notifies employees whose probation ends within 7 days.
// So "anchored at DOJ + N" is asserted as "notified exactly when DOJ+N lands in the window, and not otherwise".
//
// Tenant default is 90 ⇒ every existing tenant is unchanged. The `TenantDefault90_` arm is that control.
//
// WHY POSTGRES: this is a CROSS-TENANT sweep (IgnoreQueryFilters) that now joins tenants + locations; the
// column defaults (tenants NOT NULL 90, locations NULL) are the migration-safety contract and only a real
// provider proves them.
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

public sealed class ProbationPeriodConfigTests : IAsyncLifetime
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

    private AppDbContext Db(Guid tenantId)
    {
        var tc = new FixedTenantContext { TenantId = tenantId };
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

    private EmployeeStatusService Service(AppDbContext db, Guid tenantId, ICoreHrNotificationService notifications)
        => new(db, new FixedTenantContext { TenantId = tenantId }, Substitute.For<ICurrentUser>(),
            NullLogger<EmployeeStatusService>.Instance, notifications);

    // ── seeding ────────────────────────────────────────────────────────

    /// <summary>
    /// An employee whose probation ends in 3 days IF the effective period is <paramref name="assumedDays"/> —
    /// i.e. DOJ = today - (assumedDays - 3). Inside the sweep's 7-day reminder window for that period, and
    /// outside it for any materially different one.
    /// </summary>
    private static Employee NewEmployee(Guid tenantId, Guid deptId, Guid titleId, string no,
        Guid? locationId, int assumedDays)
        => new()
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeNo = no,
            FirstName = no, LastName = "P", Email = $"{no}@acme.test",
            DateOfJoining = DateTime.UtcNow.Date.AddDays(-(assumedDays - 3)),
            DepartmentId = deptId, JobTitleId = titleId,
            Status = EmployeeStatus.Probation, IsActive = true,
            LocationId = locationId,
        };

    private static (Guid dept, Guid title) SeedOrgUnits(AppDbContext db, Guid tenantId, string tag)
    {
        var dept = new Department { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = $"D{tag}", Code = tag };
        var title = new JobTitle { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, TitleName = $"T{tag}" };
        db.Departments.Add(dept);
        db.JobTitles.Add(title);
        return (dept.Id, title.Id);
    }

    private static Tenant NewTenant(Guid id, string sub, int probationDays)
        => new() { Id = id, Subdomain = sub, Name = sub, ProbationPeriodDays = probationDays };

    // ══ TC-CHR-330 ══

    /// <summary>
    /// CONTROL: a tenant that has never configured a probation period keeps the 90-day behaviour the service
    /// used to hardcode. This is the no-regression contract — the column default is 90 for every live row.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-330")]
    public async Task TenantDefault90_IsUnchanged_AndNotifiesAtDoj90()
    {
        var tenantId = Guid.NewGuid();
        Guid empId;

        await using (var seed = Db(tenantId))
        {
            seed.Tenants.Add(NewTenant(tenantId, "t90", 90));
            var (d, t) = SeedOrgUnits(seed, tenantId, "C90");
            var emp = NewEmployee(tenantId, d, t, "P90", locationId: null, assumedDays: 90);
            empId = emp.Id;
            seed.Employees.Add(emp);
            await seed.SaveChangesAsync();
        }

        var notifications = Substitute.For<ICoreHrNotificationService>();
        await using var db = Db(tenantId);
        await Service(db, tenantId, notifications).CheckProbationEndDatesAsync();

        await notifications.Received(1).NotifyProbationEndingAsync(
            tenantId, empId, Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// TC-CHR-330 step 1 (ISSUE-304): a tenant configured to 180 days anchors the reminder at DOJ+180.
    /// The employee is positioned so DOJ+180 is 3 days away — and DOJ+90 is long past. Pre-fix the hardcoded
    /// 90 would have found nothing, so this arm fails against the old code.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-330")]
    public async Task TenantConfigured180_AnchorsTheReminderAtDoj180_NotDoj90()
    {
        var tenantId = Guid.NewGuid();
        Guid empId;

        await using (var seed = Db(tenantId))
        {
            seed.Tenants.Add(NewTenant(tenantId, "t180", 180));
            var (d, t) = SeedOrgUnits(seed, tenantId, "C180");
            var emp = NewEmployee(tenantId, d, t, "P180", locationId: null, assumedDays: 180);
            empId = emp.Id;
            seed.Employees.Add(emp);
            await seed.SaveChangesAsync();
        }

        var notifications = Substitute.For<ICoreHrNotificationService>();
        await using var db = Db(tenantId);
        await Service(db, tenantId, notifications).CheckProbationEndDatesAsync();

        await notifications.Received(1).NotifyProbationEndingAsync(
            tenantId, empId,
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(3)),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// TC-CHR-330 steps 2–3: with a tenant default of 180, a DUBAI override of 90 wins for the Dubai employee,
    /// while the COLOMBO employee (null override) falls back to 180.
    ///
    /// <para>Both are positioned to land in the window under THEIR OWN effective period, and both are asserted
    /// in one sweep — so resolving everyone at a single period cannot pass: it would notify one and miss the
    /// other.</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-330")]
    public async Task LocationOverrideWins_AndANullOverrideFallsBackToTheTenantDefault()
    {
        var tenantId = Guid.NewGuid();
        Guid dubaiEmpId, colomboEmpId;

        await using (var seed = Db(tenantId))
        {
            seed.Tenants.Add(NewTenant(tenantId, "tmix", 180));

            var dubai = new Location
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "Dubai", TimeZone = "Asia/Dubai",
                IsActive = true, ProbationPeriodDays = 90,      // the override
            };
            var colombo = new Location
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "Colombo", TimeZone = "Asia/Colombo",
                IsActive = true, ProbationPeriodDays = null,    // silent → inherit 180
            };
            seed.Locations.AddRange(dubai, colombo);

            var (d, t) = SeedOrgUnits(seed, tenantId, "MIX");
            var dubaiEmp = NewEmployee(tenantId, d, t, "DXB", dubai.Id, assumedDays: 90);
            var colomboEmp = NewEmployee(tenantId, d, t, "CMB", colombo.Id, assumedDays: 180);
            dubaiEmpId = dubaiEmp.Id;
            colomboEmpId = colomboEmp.Id;
            seed.Employees.AddRange(dubaiEmp, colomboEmp);
            await seed.SaveChangesAsync();
        }

        var notifications = Substitute.For<ICoreHrNotificationService>();
        await using var db = Db(tenantId);
        await Service(db, tenantId, notifications).CheckProbationEndDatesAsync();

        await notifications.Received(1).NotifyProbationEndingAsync(
            tenantId, dubaiEmpId, Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await notifications.Received(1).NotifyProbationEndingAsync(
            tenantId, colomboEmpId, Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The mirror: an employee whose probation end is NOT within the 7-day window must NOT be notified. Without
    /// this, an implementation that notified every probation employee regardless of date would pass the arms
    /// above. The Dubai employee is positioned for 90 but the override is removed, so the effective period is
    /// the tenant's 180 and the end date is ~90 days out — far outside the window.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-330")]
    public async Task EmployeeOutsideTheReminderWindow_IsNotNotified()
    {
        var tenantId = Guid.NewGuid();

        await using (var seed = Db(tenantId))
        {
            seed.Tenants.Add(NewTenant(tenantId, "tout", 180));
            var (d, t) = SeedOrgUnits(seed, tenantId, "OUT");
            // Positioned for a 90-day period, but the tenant is 180 → end date is ~90 days away.
            seed.Employees.Add(NewEmployee(tenantId, d, t, "FAR", locationId: null, assumedDays: 90));
            await seed.SaveChangesAsync();
        }

        var notifications = Substitute.For<ICoreHrNotificationService>();
        await using var db = Db(tenantId);
        await Service(db, tenantId, notifications).CheckProbationEndDatesAsync();

        await notifications.DidNotReceive().NotifyProbationEndingAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The sweep is CROSS-TENANT (IgnoreQueryFilters), so each tenant must be resolved against ITS OWN period —
    /// not the first one found. Tenant A is 90 and tenant B is 180, each with an employee positioned for its
    /// own period; one sweep must notify both. A single shared period would notify one and miss the other.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-330")]
    public async Task CrossTenantSweep_ResolvesEachTenantsOwnPeriod()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid empA, empB;

        await using (var seed = Db(tenantA))
        {
            seed.Tenants.Add(NewTenant(tenantA, "tenant-a", 90));
            var (d, t) = SeedOrgUnits(seed, tenantA, "TA");
            var e = NewEmployee(tenantA, d, t, "TA1", null, assumedDays: 90);
            empA = e.Id;
            seed.Employees.Add(e);
            await seed.SaveChangesAsync();
        }

        await using (var seed = Db(tenantB))
        {
            seed.Tenants.Add(NewTenant(tenantB, "tenant-b", 180));
            var (d, t) = SeedOrgUnits(seed, tenantB, "TB");
            var e = NewEmployee(tenantB, d, t, "TB1", null, assumedDays: 180);
            empB = e.Id;
            seed.Employees.Add(e);
            await seed.SaveChangesAsync();
        }

        var notifications = Substitute.For<ICoreHrNotificationService>();
        await using var db = Db(tenantA);
        await Service(db, tenantA, notifications).CheckProbationEndDatesAsync();

        await notifications.Received(1).NotifyProbationEndingAsync(
            tenantA, empA, Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await notifications.Received(1).NotifyProbationEndingAsync(
            tenantB, empB, Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Migration safety: a tenant row written WITHOUT an explicit period gets the column default 90, and a
    /// Location written without one stays NULL (silent) rather than becoming an override of its tenant.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-330")]
    public async Task ColumnDefaults_TenantIs90_AndLocationStaysNull()
    {
        var tenantId = Guid.NewGuid();

        await using (var seed = Db(tenantId))
        {
            // ProbationPeriodDays deliberately NOT set on either row.
            seed.Tenants.Add(new Tenant { Id = tenantId, Subdomain = "tdef", Name = "tdef" });
            seed.Locations.Add(new Location
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "Silent", TimeZone = "UTC", IsActive = true,
            });
            await seed.SaveChangesAsync();
        }

        await using var db = Db(tenantId);
        (await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId))
            .ProbationPeriodDays.Should().Be(90, "the tenant column default preserves the old hardcoded value");
        (await db.Locations.AsNoTracking().SingleAsync())
            .ProbationPeriodDays.Should().BeNull("a Location must stay SILENT unless it deliberately overrides");
    }
}
