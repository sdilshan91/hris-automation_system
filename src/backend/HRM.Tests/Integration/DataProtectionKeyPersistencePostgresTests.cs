// ============================================================================
// ISSUE-247 (HIGH) regression — Data Protection key ring persisted to Postgres.
// Traceability: US-AUTH-005 NFR-2 (encrypt the TOTP MFA secret at rest) / ISSUE-247.
//
// WHY THIS MUST RUN ON REAL POSTGRES (the BUG-068 "InMemory-masks-Postgres" class):
// The property under test is that the ASP.NET Core Data Protection key ring is written
// to — and read back from — the real `data_protection_keys` table via EF
// (AddDataProtection().PersistKeysToDbContext<AppDbContext>().SetApplicationName("HRM"),
// DependencyInjection.cs:67-69). That table + its persistence round-trip only exist on a
// relational store; EF InMemory would let a fabricated "key persisted" assertion pass
// without ever exercising the real read/write, so it CANNOT verify this fix.
//
// WHAT THE FIX CHANGED (the thing regression-tested here):
//   * DependencyInjection: services.AddDataProtection()
//         .PersistKeysToDbContext<AppDbContext>().SetApplicationName("HRM")
//     (previously the default EPHEMERAL, per-instance ring — nothing was written anywhere,
//      so a redeploy / a second instance rotated to a fresh ring and could no longer decrypt
//      MFA secrets protected under purpose "HRM.MfaSecret.v1").
//   * AppDbContext : IDataProtectionKeyContext with
//         public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
//   * a migration creating table data_protection_keys (id, friendly_name, xml).
//
// HOW THESE ASSERTIONS BIND TO THE FIX (they FAIL on the old ephemeral-ring code):
//   1. After a real MfaSecretProtector.Protect(...), the data_protection_keys table has >= 1
//      row. Old code wrote to NO store, so this would be 0.
//   2. A SECOND, fully independent ServiceProvider (a simulated redeploy / second instance —
//      no shared in-memory key-ring cache) with the same DB + same SetApplicationName("HRM")
//      Unprotects the ciphertext produced by the first instance back to the exact secret. Old
//      code = fresh ephemeral ring per instance = undecryptable, so this is the load-bearing
//      guarantee that the test genuinely fails pre-fix.
//   3. Negative control: a provider with a DIFFERENT application name on the same DB CANNOT
//      Unprotect instance #1's payload — proving SetApplicationName("HRM") is the load-bearing
//      cross-instance discriminator (without it the persisted keys still would not be shared,
//      because the default app name derives from the content-root path, which differs per deploy).
//
// Harness style mirrors TenantDataDeletionPostgresTests: PostgreSqlContainer + IAsyncLifetime,
// Database.MigrateAsync() (which creates data_protection_keys), and an AppDbContext built with
// the SAME Npgsql options (MigrationsAssembly + EnableRetryOnFailure + snake_case) under a
// system tenant context. Real Data Protection, real Npgsql, real round-trip — no mocking of the
// DataProtection layer or the DB.
// ============================================================================

using System.Security.Cryptography;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

/// <summary>
/// ISSUE-247: proves the Data Protection key ring is persisted to Postgres (via EF) and shared
/// across independent process instances under a fixed application name, so an MFA secret encrypted
/// (purpose "HRM.MfaSecret.v1") on one instance stays decryptable on another — the exact failure
/// the old ephemeral, per-instance ring caused after a redeploy.
/// </summary>
public sealed class DataProtectionKeyPersistencePostgresTests : IAsyncLifetime
{
    private const string ApplicationName = "HRM";
    private const string Secret = "JBSWY3DPEHPK3PXP"; // a base32 TOTP secret (same shape MfaSecretProtector guards)

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Applies all migrations, including the one that creates data_protection_keys.
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── Test 1: Protect writes the key ring to the real Postgres table ──────────────────────

    /// <summary>
    /// A real MfaSecretProtector.Protect(...) through a DI container wired exactly like production
    /// (PersistKeysToDbContext&lt;AppDbContext&gt;) must persist the generated key to Postgres —
    /// asserting >= 1 row in data_protection_keys. With the OLD ephemeral ring nothing is written to
    /// any store, so this row count would be 0.
    /// </summary>
    [Fact]
    public async Task Protect_PersistsKeyRing_ToPostgres_ISSUE247()
    {
        // Sanity: table starts empty (proves the row below is written by Protect, not pre-seeded).
        await using (var pre = CreateContext())
        {
            (await pre.DataProtectionKeys.CountAsync()).Should().Be(0);
        }

        await using var provider = BuildProvider(ApplicationName);
        var protector = ResolveMfaProtector(provider);

        var cipher = protector.Protect(Secret);
        cipher.Should().NotBe(Secret, "Protect must produce ciphertext, not the plaintext");

        await using var db = CreateContext();
        (await db.DataProtectionKeys.CountAsync())
            .Should().BeGreaterThanOrEqualTo(1, "the key ring must be persisted to Postgres, not held in an ephemeral per-instance ring");
    }

    // ── Test 2: a fresh, independent instance decrypts the first instance's payload ──────────

    /// <summary>
    /// THE POINT OF ISSUE-247. Instance #1 Protects the secret; a SECOND, fully independent
    /// ServiceProvider (brand-new DI container = simulated redeploy / second instance, so no shared
    /// in-memory key-ring cache) with the same DB and same SetApplicationName("HRM") must Unprotect
    /// #1's ciphertext back to the exact secret. On the old code each instance owns a fresh ephemeral
    /// ring, so #2 could not decrypt #1's payload — this assertion is the guarantee the test fails pre-fix.
    /// </summary>
    [Fact]
    public async Task SecondInstance_DecryptsFirstInstancesCiphertext_ISSUE247()
    {
        string cipher;
        await using (var instance1 = BuildProvider(ApplicationName))
        {
            cipher = ResolveMfaProtector(instance1).Protect(Secret);
        }

        // Independent container = a genuinely separate instance loading the ring from Postgres.
        await using var instance2 = BuildProvider(ApplicationName);
        var recovered = ResolveMfaProtector(instance2).Unprotect(cipher);

        recovered.Should().Be(Secret,
            "a second instance sharing the Postgres key ring + application name must decrypt the first instance's payload");
    }

    // ── Test 3 (negative control): a different application name cannot decrypt ───────────────

    /// <summary>
    /// Proves SetApplicationName("HRM") is load-bearing: a provider on the SAME Postgres key ring but
    /// with a DIFFERENT application name cannot Unprotect instance #1's payload (the app name feeds the
    /// key-derivation discriminator). Uses the raw IDataProtector so the failure surfaces as the real
    /// CryptographicException rather than MfaSecretProtector's legacy-plaintext pass-through.
    /// </summary>
    [Fact]
    public async Task DifferentApplicationName_CannotDecrypt_ISSUE247()
    {
        string cipher;
        await using (var instance1 = BuildProvider(ApplicationName))
        {
            cipher = ResolveMfaProtector(instance1).Protect(Secret);
        }

        await using var alien = BuildProvider("OTHER");
        var alienProtector = alien.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(MfaSecretProtector.Purpose);

        var act = () => alienProtector.Unprotect(cipher);

        act.Should().Throw<CryptographicException>(
            "a mismatched application name must not be able to decrypt another instance's payload — proving the discriminator matters");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a self-contained ServiceProvider wired like production for the key-persistence seam:
    /// AppDbContext on the container (same Npgsql options as CreateContext) + a system tenant context,
    /// and AddDataProtection().PersistKeysToDbContext&lt;AppDbContext&gt;().SetApplicationName(appName).
    /// A brand-new provider is a simulated separate instance (its own key-ring cache).
    /// </summary>
    private ServiceProvider BuildProvider(string appName)
    {
        var services = new ServiceCollection();

        var tenantContext = new SystemTenantContext();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);

        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);

        // AppDbContext must be resolvable for PersistKeysToDbContext to read/write data_protection_keys.
        // Same Npgsql options as CreateContext(); data_protection_keys is non-tenant so interceptors/
        // query filters do not touch it (they are still wired to stay production-faithful).
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(_postgres.GetConnectionString(), n =>
                {
                    n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    n.EnableRetryOnFailure(maxRetryCount: 3);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new TenantInterceptor(tenantContext), new AuditInterceptor(currentUser));
        });

        services.AddDataProtection()
            .PersistKeysToDbContext<AppDbContext>()
            .SetApplicationName(appName);

        return services.BuildServiceProvider();
    }

    /// <summary>Resolves the REAL production consumer of the key ring (purpose "HRM.MfaSecret.v1").</summary>
    private static MfaSecretProtector ResolveMfaProtector(ServiceProvider provider)
        => new(provider.GetRequiredService<IDataProtectionProvider>());

    /// <summary>System-context AppDbContext on the container — used to read data_protection_keys directly.</summary>
    private AppDbContext CreateContext()
    {
        var tenantContext = new SystemTenantContext();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tenantContext), new AuditInterceptor(currentUser))
            .Options;
        return new AppDbContext(options, tenantContext);
    }

    /// <summary>System context: unresolved tenant ⇒ non-tenant tables (data_protection_keys) are unaffected by
    /// query filters. Same shape as the sibling Postgres suites' SystemTenantContext.</summary>
    private sealed class SystemTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public string Subdomain => "admin";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => true;
        public bool IsResolved => false;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }
}
