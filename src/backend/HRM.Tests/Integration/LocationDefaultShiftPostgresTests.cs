// ============================================================================
// US-ATT-011 AC-1 (CAL-1 / plan Tasks 1.1 + 1.3) — Location.DefaultShiftId: persistence, validation and
// tenant isolation.
//
// Covers TC-ATT-145 (happy path: an active same-tenant shift is accepted, persists, and the nav loads its
// WorkingDays; the FK is nullable), TC-ATT-146 (negative: an INACTIVE or SOFT-DELETED shift is rejected 400
// invalid_default_shift and nothing is persisted) and TC-ATT-ISO-014 (isolation: another tenant's shift is
// rejected, never persisted, and never contributes to resolution).
//
// WHY POSTGRES (not InMemory): the arms here depend on infrastructure the InMemory provider does not
// implement — the locations→shift FK and the real `default_shift_id` column produced by the migration.
// NOTE (corrected 2026-07-15): EF global query filters are provider-INDEPENDENT and do work on InMemory, so
// the tenant/soft-delete filtering is NOT the reason for this harness — do not cite that as grounds to skip a
// Postgres arm elsewhere. The FK, the migrated schema, and (in the sibling resolver suite) real command
// counts are. Uses the project's Testcontainers/Postgres harness (mirrors ShiftNameTrimDuplicatePostgresTests).
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

public sealed class LocationDefaultShiftPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private static readonly DateOnly AsOf = new(2026, 7, 6);   // a Monday

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db(_tenantA);
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

    private (ITenantContext tc, ICurrentUser cu) Principals(Guid tenantId)
    {
        var tc = new MutableTenantContext { TenantId = tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.Email.Returns("hr@acme.test");
        return (tc, cu);
    }

    private AppDbContext Db(Guid tenantId)
    {
        var (tc, cu) = Principals(tenantId);
        return Db(tc, cu);
    }

    private AppDbContext Db(ITenantContext tc, ICurrentUser cu) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
            .Options, tc);

    private static LocationService Service(AppDbContext db, ITenantContext tc, ICurrentUser cu) =>
        new(db, tc, cu, NullLogger<LocationService>.Instance);

    private static Shift NewShift(
        Guid tenantId, string name, int[] workingDays,
        bool isDefault = false, bool isActive = true, bool isDeleted = false) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = tenantId,
        Name = name,
        Type = ShiftType.Single,
        StartTime = new TimeOnly(9, 0),
        EndTime = new TimeOnly(17, 0),
        WorkingDays = workingDays.ToList(),
        IsDefault = isDefault,
        IsActive = isActive,
        IsDeleted = isDeleted,
    };

    // ══ TC-ATT-145 — happy path ════════════════════════════════════════

    /// <summary>
    /// TC-ATT-145 (AC-1): an active, same-tenant shift is accepted as a Location's DefaultShiftId, persists to
    /// default_shift_id, echoes back on re-read, and the nav loads the shift's WorkingDays. Step 3 then proves
    /// the Location tier actually supplies a located employee's resolved calendar.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-145")]
    public async Task SetDefaultShiftId_ActiveSameTenantShift_PersistsAndDrivesResolution()
    {
        var (tc, cu) = Principals(_tenantA);
        Guid gulfShiftId, locationId;

        // Seed an active Gulf shift + a Dubai location with no calendar yet.
        await using (var db = Db(tc, cu))
        {
            var gulf = NewShift(_tenantA, "Gulf Sun-Thu (145)", [7, 1, 2, 3, 4]);
            db.Shifts.Add(gulf);
            await db.SaveChangesAsync();
            gulfShiftId = gulf.Id;

            var created = await Service(db, tc, cu).CreateAsync(
                "Dubai Branch (145)", null, null, null, null, null, null, "Asia/Dubai", null);
            created.IsSuccess.Should().BeTrue(created.Error);
            created.Value!.DefaultShiftId.Should().BeNull("the FK is nullable — no calendar configured yet");
            locationId = created.Value!.Id;
        }

        // Step 1: set the DefaultShiftId to the active same-tenant shift.
        await using (var db = Db(tc, cu))
        {
            var updated = await Service(db, tc, cu).UpdateAsync(
                locationId, "Dubai Branch (145)", null, null, null, null, null, null, "Asia/Dubai", null,
                countryCode: null, defaultShiftId: gulfShiftId);

            updated.IsSuccess.Should().BeTrue(updated.Error);
            updated.Value!.DefaultShiftId.Should().Be(gulfShiftId);
        }

        // Step 2: re-read — the value persisted and the nav resolves the shift's working days.
        await using (var verify = Db(tc, cu))
        {
            var reloaded = await verify.Locations
                .Include(l => l.DefaultShift)
                .SingleAsync(l => l.Id == locationId);

            reloaded.DefaultShiftId.Should().Be(gulfShiftId);
            reloaded.DefaultShift!.WorkingDays.Should().BeEquivalentTo(new[] { 7, 1, 2, 3, 4 });

            // The API read path echoes it too.
            var (rtc, rcu) = (tc, cu);
            var dto = await Service(verify, rtc, rcu).GetByIdAsync(locationId);
            dto.Value!.DefaultShiftId.Should().Be(gulfShiftId);
        }

        // Step 3 (NFR-3): the Location tier now supplies the calendar for a Dubai employee with no
        // personal shift — Sunday works, Friday does not.
        await using (var db = Db(tc, cu))
        {
            var dept = new Department
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, Name = "Ops 145", Code = "OPS145",
            };
            var title = new JobTitle
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, TitleName = "Analyst 145",
            };
            var emp = new Employee
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, EmployeeNo = "DXB-145",
                FirstName = "Zara", LastName = "K", Email = "zara145@acme.test",
                Status = EmployeeStatus.Active, DepartmentId = dept.Id, JobTitleId = title.Id,
                LocationId = locationId,
            };
            db.Departments.Add(dept);
            db.JobTitles.Add(title);
            db.Employees.Add(emp);
            await db.SaveChangesAsync();

            await using var read = Db(tc, cu);
            var sets = await ShiftScheduleResolver.ResolveWorkingDaySetsAsync(
                read, [emp.Id], AsOf, CancellationToken.None);

            sets[emp.Id].Should().BeEquivalentTo(new[] { 7, 1, 2, 3, 4 });
        }
    }

    /// <summary>AC-1: clearing the Location tier (null) is always valid.</summary>
    [Fact]
    [Trait("TC", "TC-ATT-145")]
    public async Task SetDefaultShiftId_ToNull_ClearsTheLocationTier()
    {
        var (tc, cu) = Principals(_tenantA);
        Guid locationId, shiftId;

        await using (var db = Db(tc, cu))
        {
            var shift = NewShift(_tenantA, "Clearable (145b)", [1, 2, 3, 4, 5]);
            db.Shifts.Add(shift);
            await db.SaveChangesAsync();
            shiftId = shift.Id;

            var created = await Service(db, tc, cu).CreateAsync(
                "Clearable Branch (145b)", null, null, null, null, null, null, "Asia/Colombo", null,
                countryCode: null, defaultShiftId: shiftId);
            created.IsSuccess.Should().BeTrue(created.Error);
            created.Value!.DefaultShiftId.Should().Be(shiftId, "create must accept the field too");
            locationId = created.Value!.Id;
        }

        await using (var db = Db(tc, cu))
        {
            var cleared = await Service(db, tc, cu).UpdateAsync(
                locationId, "Clearable Branch (145b)", null, null, null, null, null, null, "Asia/Colombo",
                null, countryCode: null, defaultShiftId: null);

            cleared.IsSuccess.Should().BeTrue(cleared.Error);
            cleared.Value!.DefaultShiftId.Should().BeNull();
        }

        await using (var verify = Db(tc, cu))
        {
            (await verify.Locations.SingleAsync(l => l.Id == locationId))
                .DefaultShiftId.Should().BeNull();
        }
    }

    // ══ TC-ATT-146 — negative: inactive / soft-deleted ═════════════════

    /// <summary>
    /// TC-ATT-146 step 1 (AC-1 / spec §7.1): an INACTIVE shift may not become a Location's calendar —
    /// 400 invalid_default_shift, and nothing is persisted.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-146")]
    public async Task SetDefaultShiftId_InactiveShift_Rejected400_AndNotPersisted()
    {
        var (tc, cu) = Principals(_tenantA);
        Guid inactiveShiftId, locationId;

        await using (var db = Db(tc, cu))
        {
            var inactive = NewShift(_tenantA, "Retired Shift (146)", [1, 2, 3], isActive: false);
            db.Shifts.Add(inactive);
            await db.SaveChangesAsync();
            inactiveShiftId = inactive.Id;

            var created = await Service(db, tc, cu).CreateAsync(
                "Colombo Branch (146a)", null, null, null, null, null, null, "Asia/Colombo", null);
            locationId = created.Value!.Id;
        }

        await using (var db = Db(tc, cu))
        {
            var result = await Service(db, tc, cu).UpdateAsync(
                locationId, "Colombo Branch (146a)", null, null, null, null, null, null, "Asia/Colombo",
                null, countryCode: null, defaultShiftId: inactiveShiftId);

            result.IsFailure.Should().BeTrue("an inactive shift is not an assignable working calendar");
            result.StatusCode.Should().Be(400);
            result.ErrorCode.Should().Be("invalid_default_shift");
        }

        // Step 3: the location is untouched — the tier stays empty and falls through.
        await using (var verify = Db(tc, cu))
        {
            (await verify.Locations.SingleAsync(l => l.Id == locationId))
                .DefaultShiftId.Should().BeNull();
        }
    }

    /// <summary>
    /// TC-ATT-146 step 2 (AC-1 / spec §7.1): a SOFT-DELETED shift is not resolvable — 400, not persisted.
    /// The soft-delete global query filter is what makes this fail closed, which is why this is a Postgres arm.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-146")]
    public async Task SetDefaultShiftId_SoftDeletedShift_Rejected400_AndNotPersisted()
    {
        var (tc, cu) = Principals(_tenantA);
        Guid deletedShiftId, locationId;

        await using (var db = Db(tc, cu))
        {
            // IsActive stays true so the ONLY reason for rejection is the soft-delete.
            var deleted = NewShift(_tenantA, "Deleted Shift (146)", [1, 2, 3, 4, 5], isDeleted: true);
            db.Shifts.Add(deleted);
            await db.SaveChangesAsync();
            deletedShiftId = deleted.Id;

            var created = await Service(db, tc, cu).CreateAsync(
                "Colombo Branch (146b)", null, null, null, null, null, null, "Asia/Colombo", null);
            locationId = created.Value!.Id;
        }

        await using (var db = Db(tc, cu))
        {
            var result = await Service(db, tc, cu).UpdateAsync(
                locationId, "Colombo Branch (146b)", null, null, null, null, null, null, "Asia/Colombo",
                null, countryCode: null, defaultShiftId: deletedShiftId);

            result.IsFailure.Should().BeTrue("a soft-deleted shift is invisible and unassignable");
            result.StatusCode.Should().Be(400);
            result.ErrorCode.Should().Be("invalid_default_shift");
        }

        await using (var verify = Db(tc, cu))
        {
            (await verify.Locations.SingleAsync(l => l.Id == locationId))
                .DefaultShiftId.Should().BeNull();
        }
    }

    /// <summary>AC-1: create is guarded by the same pre-check as update.</summary>
    [Fact]
    [Trait("TC", "TC-ATT-146")]
    public async Task CreateLocation_WithInactiveDefaultShift_Rejected400_AndNoLocationRowWritten()
    {
        var (tc, cu) = Principals(_tenantA);

        await using var db = Db(tc, cu);
        var inactive = NewShift(_tenantA, "Retired Shift (146c)", [1, 2, 3], isActive: false);
        db.Shifts.Add(inactive);
        await db.SaveChangesAsync();

        var result = await Service(db, tc, cu).CreateAsync(
            "Never Created (146c)", null, null, null, null, null, null, "Asia/Colombo", null,
            countryCode: null, defaultShiftId: inactive.Id);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("invalid_default_shift");

        await using var verify = Db(tc, cu);
        (await verify.Locations.CountAsync(l => l.Name == "Never Created (146c)"))
            .Should().Be(0, "the create must be rejected before any row is written");
    }

    // ══ TC-ATT-ISO-014 — cross-tenant isolation ════════════════════════

    /// <summary>
    /// TC-ATT-ISO-014 (AC-1 / BR-1 / NFR-2, Critical Rule #1): Tenant A may not wire Tenant B's shift as its
    /// Location calendar. Under Tenant A's global query filter the foreign shift is not found, so the write is
    /// rejected exactly like a non-existent id (no cross-tenant probe signal), nothing is persisted, and a
    /// Tenant A employee at that location still falls through to Tenant A's own default — Tenant B's shift
    /// never contributes.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-ISO-014")]
    public async Task SetDefaultShiftId_CrossTenantShift_Rejected_NotPersisted_AndNeverResolves()
    {
        var (tcA, cuA) = Principals(_tenantA);
        var (tcB, cuB) = Principals(_tenantB);
        Guid shiftBId, locationAId, empAId;

        // Tenant B: an ACTIVE shift — active on purpose, so the ONLY reason it must be rejected in A is tenancy.
        await using (var dbB = Db(tcB, cuB))
        {
            var shiftB = NewShift(_tenantB, "Tenant B Sun-Thu", [7, 1, 2, 3, 4]);
            dbB.Shifts.Add(shiftB);
            await dbB.SaveChangesAsync();
            shiftBId = shiftB.Id;
        }

        // Tenant A: its own Mon-Fri default + a location + an employee at it, no personal shift.
        await using (var dbA = Db(tcA, cuA))
        {
            dbA.Shifts.Add(NewShift(_tenantA, "A Default Mon-Fri (ISO14)", [1, 2, 3, 4, 5], isDefault: true));

            var created = await Service(dbA, tcA, cuA).CreateAsync(
                "Tenant A Branch (ISO14)", null, null, null, null, null, null, "Asia/Colombo", null);
            created.IsSuccess.Should().BeTrue(created.Error);
            locationAId = created.Value!.Id;

            var dept = new Department
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, Name = "Ops ISO14", Code = "OPSISO14",
            };
            var title = new JobTitle
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, TitleName = "Analyst ISO14",
            };
            var emp = new Employee
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, EmployeeNo = "A-ISO14",
                FirstName = "Nimal", LastName = "P", Email = "nimal.iso14@acme.test",
                Status = EmployeeStatus.Active, DepartmentId = dept.Id, JobTitleId = title.Id,
                LocationId = locationAId,
            };
            dbA.Departments.Add(dept);
            dbA.JobTitles.Add(title);
            dbA.Employees.Add(emp);
            await dbA.SaveChangesAsync();
            empAId = emp.Id;
        }

        // Step 1: Tenant A tries to point its location at Tenant B's shift → rejected.
        await using (var dbA = Db(tcA, cuA))
        {
            var result = await Service(dbA, tcA, cuA).UpdateAsync(
                locationAId, "Tenant A Branch (ISO14)", null, null, null, null, null, null, "Asia/Colombo",
                null, countryCode: null, defaultShiftId: shiftBId);

            result.IsFailure.Should().BeTrue("another tenant's shift must never be accepted");
            result.StatusCode.Should().Be(400);
            result.ErrorCode.Should().Be("invalid_default_shift");
        }

        // Step 2: nothing persisted — no cross-tenant FK exists, even reading past the query filter.
        await using (var verify = Db(tcA, cuA))
        {
            (await verify.Locations.SingleAsync(l => l.Id == locationAId))
                .DefaultShiftId.Should().BeNull();

            (await verify.Locations.IgnoreQueryFilters()
                    .CountAsync(l => l.DefaultShiftId == shiftBId))
                .Should().Be(0, "no row anywhere may reference the foreign shift");
        }

        // Step 3: resolution for the Tenant A employee falls through to Tenant A's OWN default —
        // Tenant B's Sun-Thu set never contributes.
        await using (var read = Db(tcA, cuA))
        {
            var sets = await ShiftScheduleResolver.ResolveWorkingDaySetsAsync(
                read, [empAId], AsOf, CancellationToken.None);

            sets[empAId].Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 },
                "the tenant A default supplies the calendar");
            sets[empAId].Should().NotContain(7, "Tenant B's Sunday-working calendar must never leak in");
        }
    }
}
