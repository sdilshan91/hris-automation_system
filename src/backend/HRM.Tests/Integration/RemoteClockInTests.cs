// ============================================================================
// CAL-6 / US-CHR-013 + US-ATT-001 AC-6 / BR-8: a REMOTE employee is exempt from the clock-in geo-fence — they
// have no branch to be near. OnSite and Hybrid stay fully enforced.
//
// The exemption is the geo-fence RADIUS ONLY. RequireGeolocation, the IP allowlist and the photo requirement
// are separate business rules and apply to every arrangement — the `RemoteEmployee_IsStillSubjectTo...` arms
// below exist to stop the bypass quietly widening into "Remote skips attendance policy".
//
// WorkArrangement defaults to OnSite ⇒ every existing employee is unchanged. The OnSite arm is that control.
//
// WHY POSTGRES: Employee.UserId carries a real FK, and the geo-fence reads the AttendanceSettings row that
// CAL-4a made per-(tenant, location) — this drives the real resolver against real schema.
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

public sealed class RemoteClockInTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();

    // The office, and a point ~110km away — far outside any sane radius.
    private const decimal OfficeLat = 6.9271m, OfficeLon = 79.8612m;
    private const decimal FarAwayLat = 7.9271m, FarAwayLon = 79.8612m;

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

    private AttendanceService Service(AppDbContext db, Guid userId)
    {
        var tc = new FixedTenantContext { TenantId = _tenantId };
        var cu = User(userId);
        var overtime = new OvertimeService(db, tc, cu, NullLogger<OvertimeService>.Instance);
        var shift = new ShiftService(db, tc, cu, NullLogger<ShiftService>.Instance);
        return new AttendanceService(db, tc, cu, overtime, shift, NullLogger<AttendanceService>.Instance);
    }

    // ── seeding ────────────────────────────────────────────────────────

    /// <summary>Geo-fence ON around the office, 100m radius. Optional IP allowlist / photo for the scope arms.</summary>
    private void SeedSettings(AppDbContext db, bool ipAllowlist = false, bool requirePhoto = false,
        bool requireGeolocation = false)
        => db.AttendanceSettings.Add(new AttendanceSettings
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            LocationId = null,                 // the tenant-default row (CAL-4a)
            GeoFenceEnabled = true,
            GeoFenceLatitude = OfficeLat,
            GeoFenceLongitude = OfficeLon,
            GeoFenceRadiusMeters = 100,
            RequireGeolocation = requireGeolocation,
            IpAllowlistEnabled = ipAllowlist,
            IpAllowlist = ipAllowlist ? ["203.0.113.7"] : [],
            RequirePhoto = requirePhoto,
        });

    private Guid SeedEmployee(AppDbContext db, string no, WorkArrangement arrangement)
    {
        var userId = Guid.NewGuid();
        db.Set<User>().Add(new User { Id = userId, Email = $"{no}@acme.test", IsActive = true });

        var dept = new Department { Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = $"D{no}", Code = no, IsActive = true };
        var title = new JobTitle { Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, TitleName = $"T{no}", IsActive = true };
        db.Departments.Add(dept);
        db.JobTitles.Add(title);

        db.Employees.Add(new Employee
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, UserId = userId,
            EmployeeNo = no, FirstName = no, LastName = "W", Email = $"{no}@acme.test",
            DateOfJoining = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DepartmentId = dept.Id, JobTitleId = title.Id,
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
            WorkArrangement = arrangement,
        });
        return userId;
    }

    private static ClockInData FarAway() => new() { Latitude = FarAwayLat, Longitude = FarAwayLon, Source = "WEB", IpAddress = "10.0.0.1" };

    // ══ The exemption ══

    /// <summary>
    /// US-ATT-001 AC-6 / BR-8: a REMOTE employee clocking in from ~110km outside the geo-fence SUCCEEDS.
    /// Pre-fix the radius check was unconditional and returned 403 geo_fence_violation.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-328")]
    public async Task RemoteEmployee_OutsideTheGeoFence_CanClockIn()
    {
        Guid userId;
        await using (var seed = Db(Guid.NewGuid()))
        {
            SeedSettings(seed);
            userId = SeedEmployee(seed, "RMT", WorkArrangement.Remote);
            await seed.SaveChangesAsync();
        }

        await using var db = Db(userId);
        var result = await Service(db, userId).ClockInAsync(FarAway());

        result.IsSuccess.Should().BeTrue(
            because: "a Remote employee has no branch to be near — the geo-fence must not apply. " + result.Error);
    }

    /// <summary>
    /// CONTROL: an ONSITE employee — the DEFAULT, and every existing employee — is still blocked outside the
    /// fence. This is the no-regression contract: CAL-6 must not weaken the geo-fence for anyone on the books.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-328")]
    public async Task OnSiteEmployee_OutsideTheGeoFence_IsStillBlocked()
    {
        Guid userId;
        await using (var seed = Db(Guid.NewGuid()))
        {
            SeedSettings(seed);
            userId = SeedEmployee(seed, "ONS", WorkArrangement.OnSite);
            await seed.SaveChangesAsync();
        }

        await using var db = Db(userId);
        var result = await Service(db, userId).ClockInAsync(FarAway());

        result.IsFailure.Should().BeTrue("OnSite is fully enforced");
        result.ErrorCode.Should().Be("geo_fence_violation");
    }

    /// <summary>
    /// HYBRID is NOT exempt. A hybrid employee is still expected at the office on their office days, and the
    /// module has no which-days-are-office-days concept to tell them apart — so the safe reading is "enforce".
    /// Without this arm, widening the bypass to `!= OnSite` would pass every other arm in this file.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-328")]
    public async Task HybridEmployee_OutsideTheGeoFence_IsStillBlocked()
    {
        Guid userId;
        await using (var seed = Db(Guid.NewGuid()))
        {
            SeedSettings(seed);
            userId = SeedEmployee(seed, "HYB", WorkArrangement.Hybrid);
            await seed.SaveChangesAsync();
        }

        await using var db = Db(userId);
        var result = await Service(db, userId).ClockInAsync(FarAway());

        result.IsFailure.Should().BeTrue("Hybrid is NOT exempt — only Remote is");
        result.ErrorCode.Should().Be("geo_fence_violation");
    }

    /// <summary>
    /// A Remote employee INSIDE the fence is obviously fine — pins that the bypass didn't break the happy path.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-328")]
    public async Task RemoteEmployee_InsideTheGeoFence_CanAlsoClockIn()
    {
        Guid userId;
        await using (var seed = Db(Guid.NewGuid()))
        {
            SeedSettings(seed);
            userId = SeedEmployee(seed, "RIN", WorkArrangement.Remote);
            await seed.SaveChangesAsync();
        }

        await using var db = Db(userId);
        var result = await Service(db, userId).ClockInAsync(new ClockInData { Latitude = OfficeLat, Longitude = OfficeLon, Source = "WEB", IpAddress = "10.0.0.1" });

        result.IsSuccess.Should().BeTrue(because: result.Error);
    }

    // ══ The exemption is GEO-FENCE ONLY ══

    /// <summary>
    /// The bypass must not widen into "Remote skips attendance policy". A Remote employee is still subject to
    /// the IP allowlist (BR-3 / US-ATT-001 AC-5) — a separate business rule with its own error code.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-328")]
    public async Task RemoteEmployee_IsStillSubjectToTheIpAllowlist()
    {
        Guid userId;
        await using (var seed = Db(Guid.NewGuid()))
        {
            SeedSettings(seed, ipAllowlist: true);
            userId = SeedEmployee(seed, "RIP", WorkArrangement.Remote);
            await seed.SaveChangesAsync();
        }

        await using var db = Db(userId);
        var result = await Service(db, userId).ClockInAsync(FarAway());

        result.IsFailure.Should().BeTrue(
            "the geo-fence exemption is the RADIUS only — the IP allowlist is a separate BR and still applies");
        result.ErrorCode.Should().NotBe("geo_fence_violation", "it must fail on the IP rule, not the fence");
    }

    /// <summary>
    /// Likewise RequireGeolocation (BR-2 / AC-3): a Remote employee is exempt from being NEAR the office, not
    /// from SUPPLYING coordinates when the tenant mandates them.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-328")]
    public async Task RemoteEmployee_IsStillSubjectToRequireGeolocation()
    {
        Guid userId;
        await using (var seed = Db(Guid.NewGuid()))
        {
            SeedSettings(seed, requireGeolocation: true);
            userId = SeedEmployee(seed, "RGL", WorkArrangement.Remote);
            await seed.SaveChangesAsync();
        }

        await using var db = Db(userId);
        var result = await Service(db, userId).ClockInAsync(new ClockInData { Source = "WEB", IpAddress = "10.0.0.1" });   // no coordinates

        result.IsFailure.Should().BeTrue(
            "RequireGeolocation is a separate BR — Remote is exempt from the fence, not from supplying a location");
    }

    /// <summary>
    /// The column default is OnSite, so an employee row written WITHOUT an explicit WorkArrangement (every
    /// pre-CAL-6 row) is still fenced. This is what makes the migration safe on live data.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-328")]
    public async Task EmployeeWrittenWithoutAnArrangement_DefaultsToOnSite_AndIsStillFenced()
    {
        Guid userId = Guid.NewGuid();
        await using (var seed = Db(Guid.NewGuid()))
        {
            SeedSettings(seed);
            seed.Set<User>().Add(new User { Id = userId, Email = "def@acme.test", IsActive = true });
            var dept = new Department { Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "DD", Code = "DD", IsActive = true };
            var title = new JobTitle { Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, TitleName = "TT", IsActive = true };
            seed.Departments.Add(dept);
            seed.JobTitles.Add(title);
            seed.Employees.Add(new Employee
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, UserId = userId,
                EmployeeNo = "DEF", FirstName = "Def", LastName = "W", Email = "def@acme.test",
                DateOfJoining = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                DepartmentId = dept.Id, JobTitleId = title.Id,
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
                // WorkArrangement deliberately NOT set — the initializer + column default must supply OnSite.
            });
            await seed.SaveChangesAsync();
        }

        await using var db = Db(userId);
        var emp = await db.Employees.AsNoTracking().SingleAsync(e => e.UserId == userId);
        emp.WorkArrangement.Should().Be(WorkArrangement.OnSite);

        var result = await Service(db, userId).ClockInAsync(FarAway());
        result.ErrorCode.Should().Be("geo_fence_violation", "an un-set arrangement must never open the fence");
    }
}
