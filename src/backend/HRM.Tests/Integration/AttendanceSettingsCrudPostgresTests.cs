// ============================================================================
// CAL-4b / US-ATT-011 AC-3: the ADMIN CRUD path for AttendanceSettings — the tenant-default policy and
// its per-Location overrides. Before CAL-4b the entity had NO write path at all: all 24 policy fields
// were lazily created at C# defaults and were never editable.
//
// ⚠ THE RISK THIS SUITE EXISTS FOR: AttendanceSettings is NOT "one row per tenant" — it is one row per
// (tenant, location). LocationId null = the tenant default; LocationId set = that Location's override. A
// write that is not predicated on LocationId hits an ARBITRARY row: a tenant-default PUT could overwrite
// a branch's override, and an override PUT (or DELETE) could silently rewrite the whole tenant's policy.
// CAL-4a had to repoint SEVEN such reads; the arms below exist so CAL-4b does not add an eighth.
//
// WHY POSTGRES (not InMemory): the unique indexes ARE part of the contract here — the 23505 → 409
// translation and "an upsert UPDATEs rather than inserting a second row" are only meaningful on a
// provider that actually enforces ix_attendance_settings_tenant_location_unique. InMemory enforces
// neither unique indexes nor FKs, so an InMemory version of this file would be test theater.
//
// Harness copied from AttendancePolicyResolverTests. UseSnakeCaseNamingConvention() is NOT optional —
// omitting it makes MigrateAsync throw PendingModelChangesWarning.
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

public sealed class AttendanceSettingsCrudPostgresTests : IAsyncLifetime
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

    private static AttendanceSettingsService Service(AppDbContext db, Guid tenantId)
        => new(db, new FixedTenantContext { TenantId = tenantId },
            NullLogger<AttendanceSettingsService>.Instance);

    // ── seeding ────────────────────────────────────────────────────────

    private static Location NewLocation(Guid tenantId, string name, bool isActive = true) => new()
    {
        Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = name, TimeZone = "UTC", IsActive = isActive,
    };

    private static Guid NewEmployee(AppDbContext db, Guid tenantId, string no, Guid? locationId)
    {
        var dept = new Department { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = $"D{no}", Code = no };
        var title = new JobTitle { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, TitleName = $"T{no}" };
        db.Departments.Add(dept);
        db.JobTitles.Add(title);

        var id = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = id, TenantId = tenantId, EmployeeNo = no, FirstName = no, LastName = "W",
            Email = $"{no}@acme.test", DepartmentId = dept.Id, JobTitleId = title.Id,
            Status = EmployeeStatus.Active, IsActive = true, LocationId = locationId,
        });
        return id;
    }

    /// <summary>A complete policy with EVERY field moved off its default, so a dropped field is visible.</summary>
    private static AttendanceSettingsDto FullyPopulated(decimal weekendMultiplier = 3.0m) => new()
    {
        RequireGeolocation = true,
        GeoFenceEnabled = true,
        GeoFenceLatitude = 25.2048m,
        GeoFenceLongitude = 55.2708m,
        GeoFenceRadiusMeters = 250,
        IpAllowlistEnabled = true,
        IpAllowlist = new List<string> { "10.0.0.1", "192.168.1.0/24" },
        RequirePhoto = true,
        GracePeriodMinutes = 15,
        StandardWorkMinutes = 540,
        MinimumWorkMinutes = 300,
        AutoBreakMinutes = 45,
        AutoBreakThresholdMinutes = 300,
        OvertimeThresholdMinutes = 10,
        RegularizationLookbackDays = 14,
        OvertimeMinimumThresholdMinutes = 20,
        WeekdayOvertimeMultiplier = 1.25m,
        WeekendOvertimeMultiplier = weekendMultiplier,
        HolidayOvertimeMultiplier = 4.0m,
        MaxDailyOvertimeMinutes = 300,
        MaxWeeklyOvertimeMinutes = 1500,
        RequireOvertimePreApproval = true,
        HalfDayEnabled = true,
        AbsenteeismThresholdDays = 2.5m,
    };

    // ══ Arm 1 — the tenant default is created and round-trips ══

    /// <summary>
    /// PUT /settings creates the TENANT-DEFAULT row (LocationId null) and persists all 24 fields. This is
    /// the whole point of CAL-4b: before it, every field was pinned to its C# default for ever.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-155")]
    public async Task UpsertTenantSettings_CreatesTheTenantDefaultRow_AndRoundTripsEveryField()
    {
        var tenantId = Guid.NewGuid();

        await using (var db = Db(tenantId))
        {
            var result = await Service(db, tenantId).UpsertTenantSettingsAsync(FullyPopulated(), default);
            result.IsSuccess.Should().BeTrue(result.Error);
            result.Value!.LocationId.Should().BeNull("PUT /settings addresses the TENANT DEFAULT scope");
        }

        await using (var verify = Db(tenantId))
        {
            var read = await Service(verify, tenantId).GetTenantSettingsAsync(default);
            read.IsSuccess.Should().BeTrue();

            var dto = read.Value!;
            dto.LocationId.Should().BeNull();
            dto.RequireGeolocation.Should().BeTrue();
            dto.GeoFenceEnabled.Should().BeTrue();
            dto.GeoFenceLatitude.Should().Be(25.2048m);
            dto.GeoFenceLongitude.Should().Be(55.2708m);
            dto.GeoFenceRadiusMeters.Should().Be(250);
            dto.IpAllowlistEnabled.Should().BeTrue();
            dto.IpAllowlist.Should().BeEquivalentTo(new[] { "10.0.0.1", "192.168.1.0/24" });
            dto.RequirePhoto.Should().BeTrue();
            dto.GracePeriodMinutes.Should().Be(15);
            dto.StandardWorkMinutes.Should().Be(540);
            dto.MinimumWorkMinutes.Should().Be(300);
            dto.AutoBreakMinutes.Should().Be(45);
            dto.AutoBreakThresholdMinutes.Should().Be(300);
            dto.OvertimeThresholdMinutes.Should().Be(10);
            dto.RegularizationLookbackDays.Should().Be(14);
            dto.OvertimeMinimumThresholdMinutes.Should().Be(20);
            dto.WeekdayOvertimeMultiplier.Should().Be(1.25m);
            dto.WeekendOvertimeMultiplier.Should().Be(3.0m);
            dto.HolidayOvertimeMultiplier.Should().Be(4.0m);
            dto.MaxDailyOvertimeMinutes.Should().Be(300);
            dto.MaxWeeklyOvertimeMinutes.Should().Be(1500);
            dto.RequireOvertimePreApproval.Should().BeTrue();
            dto.HalfDayEnabled.Should().BeTrue();
            dto.AbsenteeismThresholdDays.Should().Be(2.5m);
        }
    }

    // ══ Arm 2 — the tenant upsert UPDATEs, never inserts a second row ══

    /// <summary>
    /// A second PUT /settings must UPDATE the existing tenant-default row. A second row would be rejected
    /// by ix_attendance_settings_tenant_default_unique (a 500), and if it ever were insertable "the tenant
    /// default" would become ambiguous for every call site that reads it.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-155")]
    public async Task UpsertTenantSettings_Twice_UpdatesTheSameRow()
    {
        var tenantId = Guid.NewGuid();

        await using (var db = Db(tenantId))
            (await Service(db, tenantId).UpsertTenantSettingsAsync(FullyPopulated(3.0m), default))
                .IsSuccess.Should().BeTrue();

        await using (var db = Db(tenantId))
        {
            var second = await Service(db, tenantId)
                .UpsertTenantSettingsAsync(FullyPopulated(5.0m) with { GracePeriodMinutes = 25 }, default);
            second.IsSuccess.Should().BeTrue(second.Error);
        }

        await using (var verify = Db(tenantId))
        {
            var rows = await verify.AttendanceSettings.AsNoTracking().ToListAsync();
            rows.Should().ContainSingle("the upsert must UPDATE, not insert a second tenant-default row");
            rows[0].LocationId.Should().BeNull();
            rows[0].WeekendOvertimeMultiplier.Should().Be(5.0m);
            rows[0].GracePeriodMinutes.Should().Be(25);
        }
    }

    // ══ Arm 3 — an override is scoped to its location and leaves the tenant row alone ══

    /// <summary>
    /// PUT /settings/overrides/{dubai} creates a row for Dubai ONLY: the tenant-default row must be
    /// untouched (an unpredicated write would have overwritten it), and the resolver must then hand Dubai's
    /// employees the override while Colombo's still get the tenant default.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-156")]
    public async Task UpsertOverride_ScopesToThatLocation_LeavesTheTenantDefaultIntact()
    {
        var tenantId = Guid.NewGuid();
        Guid dubaiId, dubaiEmp, colomboEmp;

        await using (var seed = Db(tenantId))
        {
            var dubai = NewLocation(tenantId, "Dubai");
            var colombo = NewLocation(tenantId, "Colombo");
            seed.Locations.AddRange(dubai, colombo);
            dubaiId = dubai.Id;
            dubaiEmp = NewEmployee(seed, tenantId, "DXB1", dubai.Id);
            colomboEmp = NewEmployee(seed, tenantId, "CMB1", colombo.Id);
            await seed.SaveChangesAsync();
        }

        // The tenant default: weekend OT 2.0x, grace 15.
        await using (var db = Db(tenantId))
            (await Service(db, tenantId).UpsertTenantSettingsAsync(FullyPopulated(2.0m), default))
                .IsSuccess.Should().BeTrue();

        // The Dubai override: weekend OT 3.0x, grace 45.
        await using (var db = Db(tenantId))
        {
            var result = await Service(db, tenantId).UpsertOverrideAsync(
                dubaiId, FullyPopulated(3.0m) with { GracePeriodMinutes = 45 }, default);

            result.IsSuccess.Should().BeTrue(result.Error);
            result.Value!.LocationId.Should().Be(dubaiId);
            result.Value.LocationName.Should().Be("Dubai");
        }

        await using (var verify = Db(tenantId))
        {
            var svc = Service(verify, tenantId);

            var tenantDefault = await svc.GetTenantSettingsAsync(default);
            tenantDefault.Value!.LocationId.Should().BeNull();
            tenantDefault.Value.WeekendOvertimeMultiplier.Should().Be(
                2.0m, "creating a Dubai override must NOT have written to the tenant-default row");
            tenantDefault.Value.GracePeriodMinutes.Should().Be(15, "the tenant row is untouched");

            var overrides = await svc.GetOverridesAsync(default);
            overrides.Value.Should().ContainSingle("only Dubai has an override");
            overrides.Value![0].LocationId.Should().Be(dubaiId);
            overrides.Value[0].LocationName.Should().Be("Dubai");
            overrides.Value[0].WeekendOvertimeMultiplier.Should().Be(3.0m);
        }

        // And the whole point: resolution per employee.
        await using (var resolve = Db(tenantId))
        {
            var dubaiPolicy = await AttendancePolicyResolver.ResolveForEmployeeAsync(resolve, dubaiEmp, default);
            dubaiPolicy.WeekendOvertimeMultiplier.Should().Be(3.0m, "the Dubai employee gets the override");
            dubaiPolicy.GracePeriodMinutes.Should().Be(45);

            var colomboPolicy = await AttendancePolicyResolver.ResolveForEmployeeAsync(resolve, colomboEmp, default);
            colomboPolicy.WeekendOvertimeMultiplier.Should().Be(
                2.0m, "Colombo has no override — the tenant default applies");
            colomboPolicy.LocationId.Should().BeNull();
        }
    }

    // ══ Arm 4 — the override upsert UPDATEs, one row per location ══

    /// <summary>
    /// A second PUT to the same location must UPDATE that location's row. A second row would violate
    /// ix_attendance_settings_tenant_location_unique and, if it ever landed, would make "the Dubai policy"
    /// resolve by coin flip.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-156")]
    public async Task UpsertOverride_Twice_UpdatesTheSameRow()
    {
        var tenantId = Guid.NewGuid();
        Guid dubaiId;

        await using (var seed = Db(tenantId))
        {
            var dubai = NewLocation(tenantId, "Dubai");
            dubaiId = dubai.Id;
            seed.Locations.Add(dubai);
            await seed.SaveChangesAsync();
        }

        await using (var db = Db(tenantId))
            (await Service(db, tenantId).UpsertOverrideAsync(dubaiId, FullyPopulated(3.0m), default))
                .IsSuccess.Should().BeTrue();

        await using (var db = Db(tenantId))
        {
            var second = await Service(db, tenantId)
                .UpsertOverrideAsync(dubaiId, FullyPopulated(6.0m), default);
            second.IsSuccess.Should().BeTrue(second.Error);
            second.Value!.WeekendOvertimeMultiplier.Should().Be(6.0m);
        }

        await using (var verify = Db(tenantId))
        {
            var rows = await verify.AttendanceSettings.AsNoTracking()
                .Where(s => s.LocationId == dubaiId).ToListAsync();
            rows.Should().ContainSingle("one override row per (tenant, location)");
            rows[0].WeekendOvertimeMultiplier.Should().Be(6.0m);
        }
    }

    // ══ Arm 5 — cross-tenant isolation ══

    /// <summary>
    /// TC-ATT-ISO-016: tenant A must not be able to read or write an override for tenant B's location. The
    /// EF global tenant filter makes B's location simply NOT FOUND for A, so isolation and "unknown
    /// location" share one rejection path (400 invalid_location) — and nothing is persisted, proven with
    /// IgnoreQueryFilters so a leaked row cannot hide behind the very filter under test.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-ISO-016")]
    public async Task CrossTenantLocationId_IsRejected_AndNothingIsPersisted()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid locationB;

        await using (var seedB = Db(tenantB))
        {
            var dubaiB = NewLocation(tenantB, "Dubai");
            locationB = dubaiB.Id;
            seedB.Locations.Add(dubaiB);
            await seedB.SaveChangesAsync();
        }

        await using (var db = Db(tenantA))
        {
            var svc = Service(db, tenantA);

            var put = await svc.UpsertOverrideAsync(locationB, FullyPopulated(9.0m), default);
            put.IsFailure.Should().BeTrue("tenant A must not configure policy for tenant B's location");
            put.ErrorCode.Should().Be("invalid_location");
            put.StatusCode.Should().Be(400);

            var get = await svc.GetOverrideAsync(locationB, default);
            get.IsFailure.Should().BeTrue("tenant A must not read tenant B's location override");
            get.ErrorCode.Should().Be("invalid_location");

            var del = await svc.DeleteOverrideAsync(locationB, default);
            del.IsFailure.Should().BeTrue();
            del.ErrorCode.Should().Be("invalid_location");
        }

        await using (var verify = Db(tenantA))
        {
            var leaked = await verify.AttendanceSettings.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.LocationId == locationB).ToListAsync();
            leaked.Should().BeEmpty("the rejected upsert must not have persisted a row for B's location");

            var any = await verify.AttendanceSettings.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.TenantId == tenantA).ToListAsync();
            any.Should().BeEmpty("the rejected upsert must not have created a tenant-A row either");
        }
    }

    // ══ Arm 6 — an inactive location cannot be configured ══

    /// <summary>
    /// An override must not be pinned to a deactivated location: the policy would be unreachable dead
    /// config, and a location is deactivated precisely because nobody works there any more.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-156")]
    public async Task UpsertOverride_ForAnInactiveLocation_IsRejected()
    {
        var tenantId = Guid.NewGuid();
        Guid closedId;

        await using (var seed = Db(tenantId))
        {
            var closed = NewLocation(tenantId, "Closed Branch", isActive: false);
            closedId = closed.Id;
            seed.Locations.Add(closed);
            await seed.SaveChangesAsync();
        }

        await using (var db = Db(tenantId))
        {
            var result = await Service(db, tenantId)
                .UpsertOverrideAsync(closedId, FullyPopulated(3.0m), default);

            result.IsFailure.Should().BeTrue();
            result.ErrorCode.Should().Be("invalid_location");
            result.StatusCode.Should().Be(400);
        }

        await using (var verify = Db(tenantId))
        {
            var rows = await verify.AttendanceSettings.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.LocationId == closedId).ToListAsync();
            rows.Should().BeEmpty();
        }
    }

    // ══ Arm 7 — DELETE returns the location to the tenant default ══

    /// <summary>
    /// Deleting Dubai's override must make Dubai's employees fall back to the TENANT DEFAULT — that
    /// fallback IS the semantic of the delete (row-level resolution, AC-3). The tenant-default row must
    /// survive: an unpredicated delete could have removed it instead and reset the whole tenant.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-156")]
    public async Task DeleteOverride_MakesThatLocationsEmployeesFallBackToTheTenantDefault()
    {
        var tenantId = Guid.NewGuid();
        Guid dubaiId, dubaiEmp;

        await using (var seed = Db(tenantId))
        {
            var dubai = NewLocation(tenantId, "Dubai");
            dubaiId = dubai.Id;
            seed.Locations.Add(dubai);
            dubaiEmp = NewEmployee(seed, tenantId, "DXB2", dubai.Id);
            await seed.SaveChangesAsync();
        }

        await using (var db = Db(tenantId))
        {
            var svc = Service(db, tenantId);
            (await svc.UpsertTenantSettingsAsync(FullyPopulated(2.0m), default)).IsSuccess.Should().BeTrue();
            (await svc.UpsertOverrideAsync(dubaiId, FullyPopulated(3.0m), default)).IsSuccess.Should().BeTrue();
        }

        // Pre-condition: the override is live.
        await using (var pre = Db(tenantId))
            (await AttendancePolicyResolver.ResolveForEmployeeAsync(pre, dubaiEmp, default))
                .WeekendOvertimeMultiplier.Should().Be(3.0m);

        await using (var db = Db(tenantId))
        {
            var del = await Service(db, tenantId).DeleteOverrideAsync(dubaiId, default);
            del.IsSuccess.Should().BeTrue(del.Error);
        }

        await using (var verify = Db(tenantId))
        {
            var policy = await AttendancePolicyResolver.ResolveForEmployeeAsync(verify, dubaiEmp, default);
            policy.LocationId.Should().BeNull("the override is gone — the tenant default now applies");
            policy.WeekendOvertimeMultiplier.Should().Be(2.0m);

            var svc = Service(verify, tenantId);
            (await svc.GetOverridesAsync(default)).Value.Should().BeEmpty();

            var get = await svc.GetOverrideAsync(dubaiId, default);
            get.IsFailure.Should().BeTrue();
            get.StatusCode.Should().Be(404);
            get.ErrorCode.Should().Be("override_not_found");

            var tenantDefault = await svc.GetTenantSettingsAsync(default);
            tenantDefault.Value!.WeekendOvertimeMultiplier.Should().Be(
                2.0m, "the DELETE must not have touched the tenant-default row");
        }

        // A second delete is a 404, not a silent success.
        await using (var again = Db(tenantId))
        {
            var del = await Service(again, tenantId).DeleteOverrideAsync(dubaiId, default);
            del.IsFailure.Should().BeTrue();
            del.StatusCode.Should().Be(404);
        }
    }

    // ══ Arm 8 — THE INVARIANT ══

    /// <summary>
    /// ⚠ THE INVARIANT ARM. With Location overrides present, GET /settings must still return the TENANT
    /// row — never an override. An unpredicated FirstOrDefaultAsync() here would return an arbitrary row and
    /// show (then, on the FE's GET-then-PUT round trip, SAVE) one branch's policy as the tenant-wide one.
    ///
    /// The overrides are seeded FIRST and the tenant default LAST on purpose: this arm must be able to fail
    /// against the bug it exists to catch, and an insertion order that happens to put the tenant row first
    /// would let a naive unpredicated read pass by luck.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-155")]
    public async Task GetTenantSettings_WithOverridesPresent_StillReturnsTheTenantRow()
    {
        var tenantId = Guid.NewGuid();
        Guid dubaiId, abuDhabiId;

        await using (var seed = Db(tenantId))
        {
            var dubai = NewLocation(tenantId, "Dubai");
            var abuDhabi = NewLocation(tenantId, "Abu Dhabi");
            seed.Locations.AddRange(dubai, abuDhabi);
            dubaiId = dubai.Id;
            abuDhabiId = abuDhabi.Id;
            await seed.SaveChangesAsync();
        }

        // Overrides FIRST — before any tenant-default row exists.
        await using (var db = Db(tenantId))
        {
            var svc = Service(db, tenantId);
            (await svc.UpsertOverrideAsync(dubaiId, FullyPopulated(3.0m) with { GracePeriodMinutes = 45 }, default))
                .IsSuccess.Should().BeTrue();
            (await svc.UpsertOverrideAsync(abuDhabiId, FullyPopulated(4.0m) with { GracePeriodMinutes = 55 }, default))
                .IsSuccess.Should().BeTrue();
        }

        // A GET of the tenant default at this point must NOT report Dubai's or Abu Dhabi's policy as the
        // tenant policy — with no tenant row yet, it reports code defaults.
        await using (var db = Db(tenantId))
        {
            var beforeAnyTenantRow = await Service(db, tenantId).GetTenantSettingsAsync(default);
            beforeAnyTenantRow.Value!.LocationId.Should().BeNull();
            beforeAnyTenantRow.Value.WeekendOvertimeMultiplier.Should().Be(
                2.0m, "no tenant-default row exists — code defaults, NOT Dubai's 3.0x or Abu Dhabi's 4.0x");
            beforeAnyTenantRow.Value.GracePeriodMinutes.Should().Be(0);
        }

        // The tenant default is written LAST, so it is the third row in insertion order.
        await using (var db = Db(tenantId))
            (await Service(db, tenantId)
                .UpsertTenantSettingsAsync(FullyPopulated(2.0m) with { GracePeriodMinutes = 15 }, default))
                .IsSuccess.Should().BeTrue();

        await using (var verify = Db(tenantId))
        {
            var svc = Service(verify, tenantId);

            var tenantDefault = await svc.GetTenantSettingsAsync(default);
            tenantDefault.Value!.LocationId.Should().BeNull("GET /settings addresses the TENANT DEFAULT scope");
            tenantDefault.Value.WeekendOvertimeMultiplier.Should().Be(
                2.0m, "the tenant row — NOT Dubai's 3.0x or Abu Dhabi's 4.0x");
            tenantDefault.Value.GracePeriodMinutes.Should().Be(15);

            // The tenant-default PUT must not have overwritten either override.
            var overrides = (await svc.GetOverridesAsync(default)).Value!;
            overrides.Should().HaveCount(2);
            overrides.Single(o => o.LocationId == dubaiId).WeekendOvertimeMultiplier.Should().Be(3.0m);
            overrides.Single(o => o.LocationId == abuDhabiId).WeekendOvertimeMultiplier.Should().Be(4.0m);

            // ...and the overrides list must never contain the tenant-default row.
            overrides.Should().OnlyContain(o => o.LocationId != null);

            var rows = await verify.AttendanceSettings.AsNoTracking().ToListAsync();
            rows.Should().HaveCount(3, "two overrides + exactly one tenant default");
            rows.Count(r => r.LocationId == null).Should().Be(1);
        }
    }

    // ══ Arm 9 — DF-23: the GeoFenceLocations child collection full-replaces (no orphans) ══

    /// <summary>
    /// DF-23/ISSUE-068: a settings upsert FULL-REPLACES the allowed-location collection. Re-upserting with a
    /// different set must cascade-delete the prior children (no orphans), stamp tenant_id, and round-trip the
    /// numeric(10,7) coords through real Postgres — the ISSUE-322 full-replace data-loss class + the
    /// Include-dependence of the Clear()+rebuild (a dropped Include would leave stale children behind).
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-005-17")]
    public async Task UpsertTenantSettings_FullReplacesGeofenceLocations_CascadeDeletesOrphans()
    {
        var tenantId = Guid.NewGuid();

        await using (var db = Db(tenantId))
        {
            var dto = FullyPopulated() with
            {
                GeoFenceLocations = new List<GeofenceLocationDto>
                {
                    new() { Name = "HQ", Latitude = 6.9271m, Longitude = 79.8612m, RadiusMeters = 100 },
                    new() { Name = "Branch", Latitude = 7.2906m, Longitude = 80.6337m, RadiusMeters = 150 },
                },
            };
            (await Service(db, tenantId).UpsertTenantSettingsAsync(dto, default)).IsSuccess.Should().BeTrue();
        }

        // Full-replace with ONE different location.
        await using (var db = Db(tenantId))
        {
            var dto = FullyPopulated() with
            {
                GeoFenceLocations = new List<GeofenceLocationDto>
                {
                    new() { Name = "New Site", Latitude = 6.0535m, Longitude = 80.2210m, RadiusMeters = 250 },
                },
            };
            (await Service(db, tenantId).UpsertTenantSettingsAsync(dto, default)).IsSuccess.Should().BeTrue();
        }

        await using (var verify = Db(tenantId))
        {
            // Read through the service (proves the read projection's Include loads the children).
            var read = (await Service(verify, tenantId).GetTenantSettingsAsync(default)).Value!;
            read.GeoFenceLocations.Should().ContainSingle("the full-replace keeps only the new set");
            read.GeoFenceLocations[0].Name.Should().Be("New Site");
            read.GeoFenceLocations[0].Latitude.Should().Be(6.0535m);
            read.GeoFenceLocations[0].RadiusMeters.Should().Be(250);

            // No orphan child rows survive the replace (FK cascade), tenant_id stamped, precision preserved.
            var rows = await verify.GeofenceLocations.IgnoreQueryFilters().AsNoTracking()
                .Where(g => g.TenantId == tenantId).ToListAsync();
            rows.Should().ContainSingle("the prior two children must cascade-delete — no orphans (ISSUE-322 class)");
            rows[0].TenantId.Should().Be(tenantId);
            rows[0].Longitude.Should().Be(80.2210m);
        }
    }
}
