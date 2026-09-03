// ============================================================================
// Shared Testcontainers Postgres fixture for the REPORTS suites (E3, Leg-3 parity).
//
// WHY THIS EXISTS. The established shape in this project is one container per test class
// implementing IAsyncLifetime — but xUnit constructs a NEW instance of a test class for every
// test method, so that shape starts a container AND runs the full migration set once per TEST.
// Measured here: ~20s per test. The reports slice alone is 80+ tests, i.e. ~27 minutes added to a
// gate that already takes 20-35. An IClassFixture moves container start + MigrateAsync to once per
// CLASS while xUnit still gives every test a fresh test-class instance — and therefore fresh
// per-test tenant Guids.
//
// ISOLATION ARGUMENT. Tests sharing this database do NOT share data: each test instance generates
// its own tenant Guid(s), and every entity in this schema carries TenantId behind AppDbContext's
// global query filter. Rows from a sibling test are present in the table and invisible to the
// query — which is a STRONGER assertion environment than a pristine database, because a report
// that leaks across tenants now actually fails here. A test that needs a genuinely empty table
// (unscoped/global counts, IgnoreQueryFilters, schema-level assertions) must NOT use this fixture
// and should keep the per-class-container shape.
// ============================================================================

using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Migrate ONCE for the whole class. MigrateAsync (not EnsureCreated) so the schema under test is
        // the one the generated migrations actually produce.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new AppDbContext(options, new MigrationTenantContext());
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>Minimal system-scoped tenant context — used only to construct the migrating DbContext.</summary>
    private sealed class MigrationTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public string Subdomain => "system";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => true;
        public bool IsResolved => true;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }
}
