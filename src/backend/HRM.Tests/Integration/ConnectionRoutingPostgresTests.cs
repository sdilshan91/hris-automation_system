// ============================================================================
// RLS increment 2b — R4 PROOF: the ConnectionRoutingInterceptor's connection-string swap actually works on
// EF10 / Npgsql, and Npgsql pools keyed per connection string.
//
// We configure DefaultConnection and PrivilegedConnection as the SAME container but with DIFFERENT
// `Application Name` values (hrm_app vs hrm_owner) — a stand-in for the two real roles that needs no role DDL.
// Then, under different ambient tenant contexts, we open the DbContext connection (through the interceptor) and
// read `SELECT current_setting('application_name')`. If the interceptor's string-swap took effect, the observed
// application_name reveals WHICH connection string was used — proving routing selects the right connection.
//
// If this swap did NOT work (immutable-connection / pooling issues), these assertions FAIL — which is exactly
// the R4 signal to fall back to a privileged IDbContextFactory (a bigger change, flagged not silently taken).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Multitenancy;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-PLT-002-RLS")]
[Trait("Category", "ConnectionRouting")]
public sealed class ConnectionRoutingPostgresTests : IAsyncLifetime
{
    private const string AppAppName = "hrm_app";       // the Application Name that tags the DEFAULT connection
    private const string OwnerAppName = "hrm_owner";   // the Application Name that tags the PRIVILEGED connection

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private string _defaultCs = null!;    // container, Application Name = hrm_app
    private string _privilegedCs = null!; // container, Application Name = hrm_owner

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var baseCs = _postgres.GetConnectionString();
        _defaultCs = new NpgsqlConnectionStringBuilder(baseCs) { ApplicationName = AppAppName }.ToString();
        _privilegedCs = new NpgsqlConnectionStringBuilder(baseCs) { ApplicationName = OwnerAppName }.ToString();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── R4 proof: resolved-non-system tenant ⇒ DEFAULT connection (hrm_app app-name). ──
    [Fact]
    public async Task ResolvedTenantContext_OpensDefaultConnection()
    {
        try
        {
            AmbientTenant.SetTenant(Guid.NewGuid());
            await using var db = BuildDb(config: Config(_defaultCs, _privilegedCs));
            (await AppNameAsync(db)).Should().Be(AppAppName,
                "a resolved non-system tenant must route to DefaultConnection (hrm_app)");
        }
        finally { AmbientTenant.Clear(); }
    }

    // ── R4 proof: system context ⇒ PRIVILEGED connection (hrm_owner app-name). Starts from the default base
    //    string, so a passing assertion proves the interceptor ACTIVELY swapped the string before open. ──
    [Fact]
    public async Task SystemContext_OpensPrivilegedConnection()
    {
        try
        {
            AmbientTenant.SetSystem();
            await using var db = BuildDb(config: Config(_defaultCs, _privilegedCs));
            (await AppNameAsync(db)).Should().Be(OwnerAppName,
                "the system/admin context must route to PrivilegedConnection (hrm_owner)");
        }
        finally { AmbientTenant.Clear(); }
    }

    // ── R4 proof: unresolved / no ambient (startup / no-context job) ⇒ PRIVILEGED connection. ──
    [Fact]
    public async Task UnresolvedAmbient_OpensPrivilegedConnection()
    {
        AmbientTenant.Clear();
        await using var db = BuildDb(config: Config(_defaultCs, _privilegedCs));
        (await AppNameAsync(db)).Should().Be(OwnerAppName,
            "an unresolved / no-context path must route to PrivilegedConnection (hrm_owner)");
    }

    // ── CONTROL: blank PrivilegedConnection ⇒ ALWAYS the default, even under a privileged (system) context.
    //    This is the non-breaking guarantee that holds until the increment-3 flip. ──
    [Fact]
    public async Task BlankPrivileged_SystemContext_StaysOnDefaultConnection()
    {
        try
        {
            AmbientTenant.SetSystem(); // would route privileged IF one were configured …
            await using var db = BuildDb(config: Config(_defaultCs, privilegedCs: ""));
            (await AppNameAsync(db)).Should().Be(AppAppName,
                "with a blank PrivilegedConnection the interceptor never swaps — behaviour identical to pre-2b");
        }
        finally { AmbientTenant.Clear(); }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static IConfiguration Config(string defaultCs, string? privilegedCs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = defaultCs,
                ["ConnectionStrings:PrivilegedConnection"] = privilegedCs,
            })
            .Build();

    // Builds an AppDbContext whose base connection is the DEFAULT string, with the routing interceptor wired.
    private AppDbContext BuildDb(IConfiguration config)
    {
        var interceptor = new ConnectionRoutingInterceptor(config);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_defaultCs, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(interceptor)
            .Options;
        return new AppDbContext(options, new MutableTenantContext());
    }

    // Opens the DbContext connection THROUGH the interceptor, then reads which connection actually opened by its
    // application_name. Uses the same open connection so the reading proves the physical connection selected.
    private static async Task<string> AppNameAsync(AppDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            var conn = db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT current_setting('application_name')";
            return (string)(await cmd.ExecuteScalarAsync())!;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // Minimal ITenantContext — AppDbContext requires it for the global query filter; routing is driven by the
    // AmbientTenant, not this, so its values are irrelevant to these tests.
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
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status, string? plan = null,
            IReadOnlyCollection<string>? enabledModules = null, string? logoUrl = null, string? primaryColor = null)
            => TenantId = tenantId;
        public void SetSystemContext() { }
    }
}
