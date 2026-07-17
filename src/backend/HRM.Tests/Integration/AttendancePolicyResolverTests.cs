// ============================================================================
// CAL-4 / US-ATT-011 AC-3: location-scoped AttendanceSettings override + AttendancePolicyResolver.
//
// A row with LocationId == null is the TENANT DEFAULT; a row with LocationId set is that Location's
// OVERRIDE. Resolution is ROW-LEVEL: the employee's Location override wins wholesale, else the tenant
// default, else a lazily-created default row.
//
// ⚠ THE RISK THIS SUITE EXISTS FOR: before CAL-4 there was exactly ONE settings row per tenant — enforced
// structurally by a unique index on TenantId alone — and SIX call sites silently relied on it by reading with
// an unpredicated FirstOrDefaultAsync(). Adding override rows breaks that invariant: an unpredicated read
// returns an ARBITRARY row, which would apply one branch's geofence/OT multipliers to the WHOLE tenant. The
// `TenantDefault...` arms below exist to prove that cannot happen.
//
// WHY POSTGRES: the unique indexes ARE the contract here (TC-ATT-151), and the InMemory provider enforces
// neither unique indexes nor FKs — an InMemory version of this file would be test theater. Postgres also
// treats NULLs as DISTINCT in a unique index, which is exactly why the tenant-default row needs its own
// partial index; only a real provider proves that.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class AttendancePolicyResolverTests : IAsyncLifetime
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

    // ── seeding ────────────────────────────────────────────────────────

    private static Location NewLocation(Guid tenantId, string name) => new()
    {
        Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = name, TimeZone = "UTC", IsActive = true,
    };

    private static AttendanceSettings NewSettings(Guid tenantId, Guid? locationId, decimal weekendMultiplier) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = tenantId,
        LocationId = locationId,
        WeekendOvertimeMultiplier = weekendMultiplier,
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

    // ══ TC-ATT-150 — the override applies to that Location's employees ══

    /// <summary>
    /// TC-ATT-150 (AC-3): a Dubai override with WeekendOvertimeMultiplier 3.0 resolves for a Dubai employee,
    /// while a Colombo employee (no override at their location) still gets the tenant default 2.0 — and an
    /// employee with NO location gets the tenant default too. Three distinct values would be impossible if
    /// resolution were not per-employee.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-150")]
    public async Task LocationOverride_AppliesToThatLocationsEmployees_OthersGetTheTenantDefault()
    {
        var tenantId = Guid.NewGuid();
        Guid dubaiEmp, colomboEmp, noLocationEmp;

        await using (var seed = Db(tenantId))
        {
            var dubai = NewLocation(tenantId, "Dubai");
            var colombo = NewLocation(tenantId, "Colombo");
            seed.Locations.AddRange(dubai, colombo);

            seed.AttendanceSettings.Add(NewSettings(tenantId, null, 2.0m));       // tenant default
            seed.AttendanceSettings.Add(NewSettings(tenantId, dubai.Id, 3.0m));   // Dubai override

            dubaiEmp = NewEmployee(seed, tenantId, "DXB1", dubai.Id);
            colomboEmp = NewEmployee(seed, tenantId, "CMB1", colombo.Id);
            noLocationEmp = NewEmployee(seed, tenantId, "HQ1", null);
            await seed.SaveChangesAsync();
        }

        await using var db = Db(tenantId);

        var dubaiPolicy = await AttendancePolicyResolver.ResolveForEmployeeAsync(db, dubaiEmp, default);
        dubaiPolicy.WeekendOvertimeMultiplier.Should().Be(3.0m, "the Dubai override wins for a Dubai employee");
        dubaiPolicy.LocationId.Should().Be(
            (await db.Locations.AsNoTracking().FirstAsync(l => l.Name == "Dubai")).Id);

        var colomboPolicy = await AttendancePolicyResolver.ResolveForEmployeeAsync(db, colomboEmp, default);
        colomboPolicy.WeekendOvertimeMultiplier.Should().Be(
            2.0m, "Colombo has no override — the tenant default applies");
        colomboPolicy.LocationId.Should().BeNull("it is the tenant-default row");

        var hqPolicy = await AttendancePolicyResolver.ResolveForEmployeeAsync(db, noLocationEmp, default);
        hqPolicy.WeekendOvertimeMultiplier.Should().Be(2.0m, "no location at all — the tenant default applies");
        hqPolicy.LocationId.Should().BeNull();
    }

    /// <summary>
    /// ⚠ THE INVARIANT-BREAK REGRESSION. With a Dubai override present, the TENANT-DEFAULT accessor must still
    /// return the tenant row — never the override. This is what the six repointed call sites depend on: an
    /// unpredicated FirstOrDefaultAsync() would return an arbitrary row here and could hand Dubai's 3.0×
    /// weekend multiplier to every employee in the tenant. Seeded override-first so a naive read is more
    /// likely to pick the wrong row.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-150")]
    public async Task TenantDefaultAccessor_NeverReturnsALocationOverride()
    {
        var tenantId = Guid.NewGuid();

        await using (var seed = Db(tenantId))
        {
            var dubai = NewLocation(tenantId, "Dubai");
            var abuDhabi = NewLocation(tenantId, "Abu Dhabi");
            seed.Locations.AddRange(dubai, abuDhabi);

            // Overrides inserted BEFORE the tenant default on purpose.
            seed.AttendanceSettings.Add(NewSettings(tenantId, dubai.Id, 3.0m));
            seed.AttendanceSettings.Add(NewSettings(tenantId, abuDhabi.Id, 4.0m));
            seed.AttendanceSettings.Add(NewSettings(tenantId, null, 2.0m));
            await seed.SaveChangesAsync();
        }

        await using var db = Db(tenantId);

        var viaGetOrCreate = await AttendancePolicyResolver.GetOrCreateTenantDefaultAsync(db, default);
        viaGetOrCreate.LocationId.Should().BeNull();
        viaGetOrCreate.WeekendOvertimeMultiplier.Should().Be(
            2.0m, "the tenant default, NOT Dubai's 3.0x or Abu Dhabi's 4.0x");

        var viaReadOnly = await AttendancePolicyResolver.GetTenantDefaultOrNullAsync(db, default);
        viaReadOnly!.LocationId.Should().BeNull();
        viaReadOnly.WeekendOvertimeMultiplier.Should().Be(2.0m);

        db.AttendanceSettings.Count().Should().Be(3, "the accessor must not have created a fourth row");
    }

    /// <summary>
    /// The lazy-create must produce the TENANT-DEFAULT row (LocationId null) and never a Location override —
    /// auto-creating an override would silently pin that branch to code defaults for ever.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-150")]
    public async Task LazyCreate_CreatesTheTenantDefaultRow_NeverALocationOverride()
    {
        var tenantId = Guid.NewGuid();
        Guid dubaiEmp;

        await using (var seed = Db(tenantId))
        {
            var dubai = NewLocation(tenantId, "Dubai");
            seed.Locations.Add(dubai);
            dubaiEmp = NewEmployee(seed, tenantId, "DXB9", dubai.Id);
            await seed.SaveChangesAsync();   // NO settings rows at all
        }

        await using var db = Db(tenantId);
        var policy = await AttendancePolicyResolver.ResolveForEmployeeAsync(db, dubaiEmp, default);

        policy.LocationId.Should().BeNull(
            "the lazily-created row is the TENANT default even though the employee has a location");
        policy.WeekendOvertimeMultiplier.Should().Be(2.0m, "code default");

        var rows = await db.AttendanceSettings.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle("exactly one row created");
        rows[0].LocationId.Should().BeNull();
    }

    /// <summary>
    /// CAL-6 / US-ATT-011 AC-5: a LAZILY-CREATED tenant-default row must have `FteScaledOvertimeBase = false`.
    ///
    /// <para>This arm exists because mutation-testing found the gap: every OT-base arm passes the flag
    /// EXPLICITLY, so flipping the entity's initializer to `true` survived the entire suite — yet EF sends the
    /// property value on INSERT, so a lazily-created row would carry it and silently scale every part-timer's
    /// OT hourly base for a tenant that never opted in. The migration's column default is only a backstop for
    /// rows inserted outside EF.</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-152")]
    public async Task LazyCreatedTenantDefault_HasFteScaledOvertimeBaseOff()
    {
        var tenantId = Guid.NewGuid();

        await using var db = Db(tenantId);
        var created = await AttendancePolicyResolver.GetOrCreateTenantDefaultAsync(db, default);

        created.FteScaledOvertimeBase.Should().BeFalse(
            "the OT base must NOT scale by FTE unless the tenant opts in — a lazily-created row must not "
            + "silently enable it");

        await using var read = Db(tenantId);
        var reloaded = await read.AttendanceSettings.AsNoTracking().SingleAsync();
        reloaded.FteScaledOvertimeBase.Should().BeFalse("and it must persist as false, not just default in memory");
    }

    // ══ TC-ATT-151 — at most one override per (tenant, location) ══

    /// <summary>
    /// TC-ATT-151 (AC-3): a SECOND override row for the same (tenant, location) is rejected by
    /// ix_attendance_settings_tenant_location_unique. Without this, "the Dubai policy" would be ambiguous and
    /// the resolver would pick one non-deterministically — a money path deciding by coin flip.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-151")]
    public async Task SecondOverrideForTheSameLocation_IsRejectedByTheUniqueIndex()
    {
        var tenantId = Guid.NewGuid();
        Guid dubaiId;

        await using (var seed = Db(tenantId))
        {
            var dubai = NewLocation(tenantId, "Dubai");
            dubaiId = dubai.Id;
            seed.Locations.Add(dubai);
            seed.AttendanceSettings.Add(NewSettings(tenantId, dubai.Id, 3.0m));
            await seed.SaveChangesAsync();
        }

        await using var db = Db(tenantId);
        db.AttendanceSettings.Add(NewSettings(tenantId, dubaiId, 9.0m));

        var act = async () => await db.SaveChangesAsync();

        var ex = (await act.Should().ThrowAsync<DbUpdateException>()).And;
        ex.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(
                "23505", "a duplicate (tenant, location) override must violate the unique index");
    }

    /// <summary>
    /// The NULL-distinctness hole. Postgres treats NULLs as DISTINCT in a unique index, so
    /// (tenant, NULL) is NOT de-duplicated by ix_attendance_settings_tenant_location_unique alone — two
    /// tenant-default rows would be insertable and "the tenant default" would become ambiguous, which every
    /// repointed call site depends on being singular. ix_attendance_settings_tenant_default_unique (partial,
    /// location_id IS NULL) closes it. This arm is only meaningful on a real provider.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-151")]
    public async Task SecondTenantDefaultRow_IsRejected_DespitePostgresNullDistinctness()
    {
        var tenantId = Guid.NewGuid();

        await using (var seed = Db(tenantId))
        {
            seed.AttendanceSettings.Add(NewSettings(tenantId, null, 2.0m));
            await seed.SaveChangesAsync();
        }

        await using var db = Db(tenantId);
        db.AttendanceSettings.Add(NewSettings(tenantId, null, 9.0m));

        var act = async () => await db.SaveChangesAsync();

        var ex = (await act.Should().ThrowAsync<DbUpdateException>()).And;
        ex.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(
                "23505",
                "a second tenant-default row must be rejected — NULLs are distinct in a plain unique index, "
                + "so the partial index is what makes the tenant default singular");
    }

    // ══ TC-ATT-ISO-015 — cross-tenant isolation ══

    /// <summary>
    /// TC-ATT-ISO-015: tenant B's override for ITS location must never resolve for tenant A. Both tenants use
    /// the same location NAME and both have an override, so a leak would be visibly wrong rather than
    /// coincidentally right. The EF global tenant filter is the mechanism.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-ISO-015")]
    public async Task CrossTenantOverride_NeverResolves()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid empA;

        await using (var seedA = Db(tenantA))
        {
            var dubaiA = NewLocation(tenantA, "Dubai");
            seedA.Locations.Add(dubaiA);
            seedA.AttendanceSettings.Add(NewSettings(tenantA, null, 2.0m));       // A tenant default
            seedA.AttendanceSettings.Add(NewSettings(tenantA, dubaiA.Id, 3.0m));  // A Dubai override
            empA = NewEmployee(seedA, tenantA, "A-DXB", dubaiA.Id);
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = Db(tenantB))
        {
            var dubaiB = NewLocation(tenantB, "Dubai");
            seedB.Locations.Add(dubaiB);
            seedB.AttendanceSettings.Add(NewSettings(tenantB, null, 7.0m));       // B tenant default
            seedB.AttendanceSettings.Add(NewSettings(tenantB, dubaiB.Id, 9.0m));  // B Dubai override
            await seedB.SaveChangesAsync();
        }

        await using var db = Db(tenantA);
        var policy = await AttendancePolicyResolver.ResolveForEmployeeAsync(db, empA, default);

        policy.WeekendOvertimeMultiplier.Should().Be(
            3.0m, "tenant A's own Dubai override — never B's 9.0x, and never B's 7.0x default");

        var tenantDefault = await AttendancePolicyResolver.GetOrCreateTenantDefaultAsync(db, default);
        tenantDefault.WeekendOvertimeMultiplier.Should().Be(2.0m, "tenant A's default, not B's 7.0x");
    }

    [Fact]
    public async Task Concurrent_first_clockins_create_exactly_one_tenant_default_issue308()
    {
        // ISSUE-308: two+ concurrent first-clock-ins for a tenant with NO settings row both read null and both
        // insert; the unique index (ix_attendance_settings_tenant_location_unique) rejects the loser. The
        // catch-on-conflict must return the committed winner instead of throwing a 500. InMemory can't
        // reproduce this (no unique enforcement) — real Postgres only.
        var tenantId = Guid.NewGuid();
        const int parallelism = 8;

        var tasks = Enumerable.Range(0, parallelism).Select(async _ =>
        {
            await using var db = Db(tenantId);
            return await AttendancePolicyResolver.GetOrCreateTenantDefaultAsync(db, default);
        }).ToArray();

        var results = await Task.WhenAll(tasks); // must NOT throw — the loser catches 23505 and returns the winner

        results.Should().OnlyContain(s => s.LocationId == null);
        results.Select(s => s.Id).Distinct().Should().HaveCount(1, "every racer resolves to the one committed tenant-default row");

        await using var verify = Db(tenantId);
        var count = await verify.AttendanceSettings.IgnoreQueryFilters()
            .CountAsync(s => s.TenantId == tenantId && s.LocationId == null);
        count.Should().Be(1, "exactly one tenant-default row exists despite the concurrent lazy-create race");
    }
}
