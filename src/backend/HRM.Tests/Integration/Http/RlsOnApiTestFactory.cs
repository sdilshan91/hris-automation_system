using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// DF-rls-http-surface — the HTTP harness with <b>RLS actually ON</b>.
///
/// <para><b>Why this had to exist.</b> <see cref="ApiTestFactory"/> hard-codes <c>Rls:Enabled=false</c> and
/// blanks <c>PrivilegedConnection</c>, and it is the only <c>WebApplicationFactory&lt;Program&gt;</c> in the
/// suite — shared by 28 test classes. So every controller, middleware and MediatR handler was tested
/// exclusively with RLS <b>off</b>, in a codebase whose entire tenant-isolation strategy is RLS. Its own
/// comment claimed "RLS enforcement is covered by RlsIsolationPostgresTests", but that suite drives raw
/// <c>AppDbContext</c>s — not the app. Nothing exercised a real request under enforcement.</para>
///
/// <para>That gap is what let the fail-open → fail-closed inversion sit undetected: the EF filter is
/// fail-open, RLS is fail-closed, so every cross-tenant <c>IgnoreQueryFilters()</c> reverses meaning when the
/// flag flips — silently, looking exactly like "no such record". Tenant switching, the workspace switcher and
/// cross-tenant session revocation were all broken under RLS while the whole suite stayed green.</para>
///
/// <para><b>Still hermetic.</b> The original factory forced RLS off to avoid inheriting a developer's RLS-ON
/// machine config and split-braining against their live dev database. This harness sets BOTH connection
/// strings explicitly to its own throwaway container, so it inherits nothing either way.</para>
///
/// <para><b>Ordering matters.</b> Migrations run as the superuser <i>before</i> the app boots, then the roles
/// and grants are provisioned, then the app starts and its reconciler ENABLEs + FORCEs RLS. Letting the app
/// migrate as <c>hrm_owner</c> would require the role to exist before the schema — the same chicken-and-egg
/// the production runbook resolves with <c>roles.sql</c> plus <c>REASSIGN OWNED</c>.</para>
/// </summary>
public sealed class RlsOnApiTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string AppRole = "hrm_app";
    private const string AppPassword = "app_pw_http_rls";
    private const string OwnerRole = "hrm_owner";
    private const string OwnerPassword = "owner_pw_http_rls";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly string _jwtPrivateKeyPem = RSA.Create(2048).ExportPkcs8PrivateKeyPem();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private string _appConnString = null!;
    private string _ownerConnString = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var superCs = _postgres.GetConnectionString();
        _appConnString = WithRole(superCs, AppRole, AppPassword);
        _ownerConnString = WithRole(superCs, OwnerRole, OwnerPassword);

        // (1) Roles, as the superuser — the app never runs role DDL (mirrors Persistence/Rls/roles.sql).
        await ExecAsync(superCs, $"""
            DO $r$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                    CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{OwnerRole}') THEN
                    CREATE ROLE {OwnerRole} LOGIN PASSWORD '{OwnerPassword}' BYPASSRLS;
                END IF;
            END
            $r$;
            ALTER ROLE {AppRole} NOBYPASSRLS;
            """);

        // (2) Schema first, as the superuser. The app's own DbInitializer will then find migrations applied
        //     and move straight to seeding + the RLS reconciler.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(superCs, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var migrate = new AppDbContext(options, new NullTenantContext()))
        {
            await migrate.Database.MigrateAsync();
        }

        // (3) Ownership + grants. hrm_owner owns the tables (so the reconciler may ALTER them) and bypasses
        //     RLS; hrm_app gets DML only and is NOBYPASSRLS, so policies actually bind to it.
        await ExecAsync(superCs, $"""
            GRANT USAGE ON SCHEMA public TO {AppRole}, {OwnerRole};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRole}, {OwnerRole};
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {AppRole}, {OwnerRole};
            GRANT CREATE ON SCHEMA public TO {OwnerRole};
            """);

        // Ownership of the PUBLIC-SCHEMA objects only. `REASSIGN OWNED BY CURRENT_USER` fails here (2BP01):
        // the container superuser also owns system objects the database requires. Production uses
        // REASSIGN OWNED because there the migration role owns nothing else.
        await ExecAsync(superCs, $"""
            DO $o$
            DECLARE r record;
            BEGIN
                FOR r IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' LOOP
                    EXECUTE format('ALTER TABLE public.%I OWNER TO {OwnerRole}', r.tablename);
                END LOOP;
                FOR r IN SELECT sequencename FROM pg_sequences WHERE schemaname = 'public' LOOP
                    EXECUTE format('ALTER SEQUENCE public.%I OWNER TO {OwnerRole}', r.sequencename);
                END LOOP;
            END
            $o$;
            """);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("RateLimiting:Disabled", "true");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // The whole point of this harness.
                ["Rls:Enabled"] = "true",
                ["ConnectionStrings:DefaultConnection"] = _appConnString,      // hrm_app  — RLS ENFORCED
                ["ConnectionStrings:PrivilegedConnection"] = _ownerConnString, // hrm_owner — BYPASSRLS
                ["ConnectionStrings:Redis"] = "",
                ["Jwt:PrivateKey"] = _jwtPrivateKeyPem,
                ["Encryption:ActiveKeyId"] = "hrm-field-key-1",
                ["Encryption:Keys:hrm-field-key-1"] = "ChvBEPLThNv30ZpGbzNQ6lKvo249XGvNBDjKLQhUVn4=",
                ["Hangfire:DisableServer"] = "true",
                // Keep login cheap: this harness logs in repeatedly and BCrypt cost dominates the runtime.
                ["Authentication:PasswordHashing:WorkFactor"] = "10",
            });
        });
    }

    /// <summary>Logs in on a tenant subdomain and returns a client carrying the bearer + tenant headers.</summary>
    public async Task<HttpClient> CreateAuthedClientAsync(string subdomain, string email, string password)
    {
        var client = CreateClient();

        var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email, password }),
        };
        login.Headers.Add("X-Tenant-Subdomain", subdomain);

        var response = await client.SendAsync(login);
        var raw = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Login failed for {email}@{subdomain} under RLS: {(int)response.StatusCode}. Body: {raw}");
        }

        using var doc = JsonDocument.Parse(raw);
        var token = doc.RootElement.GetProperty("data").GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", subdomain);
        return client;
    }

    public HttpClient CreateClientFor(string subdomain)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", subdomain);
        return client;
    }

    /// <summary>A scope on the PRIVILEGED connection, for seeding across tenants in a test's arrangement.</summary>
    public AppDbContext CreatePrivilegedDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_ownerConnString)
            .UseSnakeCaseNamingConvention()
            .Options, new NullTenantContext());

    private static string WithRole(string connectionString, string user, string password) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Username = user, Password = password }
            .ConnectionString;

    private static async Task ExecAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Unresolved tenant context — used only for migration/seeding contexts outside a request.</summary>
    private sealed class NullTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public string Subdomain => string.Empty;
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => false;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }
}
