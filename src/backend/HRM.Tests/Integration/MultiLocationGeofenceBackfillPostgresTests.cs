// ============================================================================
// DF-23/ISSUE-068 — on-data migration backfill proof (20260721173141_MultiLocationGeofence).
//
// The migration backfills every legacy single-center attendance_settings row (geo_fence_enabled + a scalar
// center) into one 'Primary' GeofenceLocation. Once that child exists, clock-in enforcement short-circuits on
// `GeoFenceLocations.Count > 0` and NO LONGER consults the legacy-scalar fallback — so an existing tenant's
// geofence (a security control) then rides entirely on the backfill copying lat/lng/radius FAITHFULLY. The
// Testcontainers CRUD suite migrates an empty DB (parse-only). This seeds a real legacy row BEFORE the
// migration, runs the ACTUAL migration over it, and asserts the backfilled allowed location matches — so a
// future regression that swaps lat/lng or mangles the radius goes RED. postgres:17-alpine via Testcontainers.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class MultiLocationGeofenceBackfillPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();

    private const string BeforeMigration = "20260721163054_Payroll_SlipDetailComponentCode";
    private const string GeofenceMigration = "20260721173141_MultiLocationGeofence";

    public async Task InitializeAsync() => await _postgres.StartAsync();
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

    private AppDbContext CreateContext()
    {
        var tc = new FixedTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
            .Options, tc);
    }

    [Fact]
    [Trait("TC", "TC-ATT-005-18")]
    public async Task Migration_backfills_one_geofence_location_from_a_legacy_single_center()
    {
        await using var db = CreateContext();
        var migrator = db.Database.GetService<IMigrator>();

        // 1. Migrate to just BEFORE the geofence migration — attendance_settings exists, no child table yet.
        await migrator.MigrateAsync(BeforeMigration);

        // 2. A legacy single-center settings row (the scalar geofence, no child collection).
        var settingsId = Guid.NewGuid();
        db.AttendanceSettings.Add(new AttendanceSettings
        {
            Id = settingsId,
            TenantId = _tenantId,
            GeoFenceEnabled = true,
            GeoFenceLatitude = 6.9271m,
            GeoFenceLongitude = 79.8612m,
            GeoFenceRadiusMeters = 123,
        });
        await db.SaveChangesAsync();

        // 3. Apply the REAL geofence migration — its Up() runs the backfill INSERT ... SELECT over the row above.
        await migrator.MigrateAsync(GeofenceMigration);

        // 4. Exactly one 'Primary' allowed location, with the legacy center's coords copied FAITHFULLY (a
        //    lat/lng swap or radius mangle would fail these — the mutation-meaningful part).
        await using var verify = CreateContext();
        var locations = await verify.GeofenceLocations.IgnoreQueryFilters().AsNoTracking()
            .Where(g => g.AttendanceSettingsId == settingsId).ToListAsync();

        locations.Should().ContainSingle("the backfill creates one Primary location per enabled single-center row");
        locations[0].Name.Should().Be("Primary");
        locations[0].Latitude.Should().Be(6.9271m);
        locations[0].Longitude.Should().Be(79.8612m);
        locations[0].RadiusMeters.Should().Be(123);
        locations[0].TenantId.Should().Be(_tenantId, "the backfill stamps tenant_id from the source settings row");
    }
}
