// ============================================================================
// US-PLT-005 (Scope A) — the legacy-plaintext MFA-secret back-fill + report visibility on REAL Postgres
// (Testcontainers). Proves what InMemory cannot: the DbInitializer back-fill upgrades a genuine
// stored-plaintext users.mfa_secret through the REAL EF change tracker + Npgsql, LOSSLESSLY, and the
// key-usage report's system-scope MfaSecretsLegacyPlaintext count sees the residue BEFORE the heal and
// zero AFTER.
//
// Why real Postgres (the "InMemory masks Postgres" rule): FieldEncryptionMaintenanceService's legacy count
// is gated to relational providers (it returns 0 on InMemory), and the back-fill's whole point is a real
// raw stored form on a real store. mfa_secret has NO EF value converter, so both the seeded plaintext and
// the protected payload are written to Postgres verbatim — exactly the drift the back-fill heals.
//
// mfa_secret is DELIBERATELY NOT an EncryptedFieldRegistry (AES-GCM) column — users has no tenant_id and the
// registry sweep hard-codes WHERE tenant_id = …, so this legacy count is a separate, tenant-agnostic path.
//
// Runs against a throwaway postgres:17-alpine container (mirrors FieldEncryptionReencryptPostgresTests). The
// agent verify gate has no Docker, so this arm is executed by the orchestrator's Postgres run.
// ============================================================================

using System.Security.Cryptography;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Security;
using HRM.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-PLT-005")] // real-PG legacy MFA-secret back-fill + report visibility
[Trait("Category", "FieldEncryption")]
public sealed class MfaSecretBackfillPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    // Deterministic key so any process-wide cached EF encryption converter stays consistent (see
    // FieldEncryptionReencryptPostgresTests for the rationale). This test never touches an encrypted column,
    // but the AppDbContext ctor still requires an encryptor.
    private static readonly string _k = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    // The SHARED protector: the DbInitializer back-fill, the report service and this test's own assertions all
    // use the same instance, so a value one Protect()s another correctly recognises as protected.
    private readonly IFieldProtector _protector = new MfaSecretProtector(DataProtectionProvider.Create("plt005-tests"));

    private IFieldEncryptor _encryptor = null!;
    private string _cs = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _cs = _postgres.GetConnectionString() + ";Include Error Detail=true";
        _encryptor = new AesGcmFieldEncryptor(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Encryption:ActiveKeyId"] = "k", ["Encryption:Keys:k"] = _k }).Build());

        await using var migrate = Db();
        await migrate.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Backfill_heals_plaintext_mfa_secret_and_report_counts_it_before_and_zero_after()
    {
        const string legacySecret = "JBSWY3DPEHPK3PXP"; // raw base32 TOTP secret, stored verbatim (pre-encryption)
        var legacyUserId = Guid.NewGuid();
        var protectedUserId = Guid.NewGuid();
        var noMfaUserId = Guid.NewGuid();

        // An already-protected value is written verbatim too (no converter on mfa_secret) — it must be counted
        // as NOT legacy and left byte-identical by the back-fill.
        var alreadyProtected = _protector.Protect("KRSXG5CTMVRXEZLU");

        await using (var db = Db())
        {
            db.Users.Add(new User { Id = legacyUserId, Email = "plt005-legacy@acme.com", MfaEnabled = true, MfaSecret = legacySecret });
            db.Users.Add(new User { Id = protectedUserId, Email = "plt005-protected@acme.com", MfaEnabled = true, MfaSecret = alreadyProtected });
            db.Users.Add(new User { Id = noMfaUserId, Email = "plt005-nomfa@acme.com", MfaEnabled = false, MfaSecret = null });
            await db.SaveChangesAsync();
        }

        // Sanity: the seeded legacy secret really is stored as raw plaintext on disk.
        (await RawMfaSecretAsync(legacyUserId)).Should().Be(legacySecret);

        await using var provider = BuildProvider();
        var service = provider.GetRequiredService<IFieldEncryptionMaintenanceService>();

        // BEFORE: the report surfaces exactly the one legacy plaintext row (the protected + null rows do not count).
        var before = await service.GetKeyUsageReportAsync();
        before.MfaSecretsLegacyPlaintext.Should().Be(1,
            "exactly one users.mfa_secret is non-null legacy plaintext; the protected and null rows must not count");

        // Act: the idempotent startup back-fill.
        await using (var db = Db())
        {
            await DbInitializer.BackfillLegacyMfaSecretsAsync(db, _protector, NullLogger.Instance, CancellationToken.None);
        }

        // The legacy row is now protected on disk AND still decrypts to the ORIGINAL secret (lossless — the user
        // can still authenticate). This is the load-bearing assertion.
        var healedRaw = await RawMfaSecretAsync(legacyUserId);
        healedRaw.Should().NotBe(legacySecret, "the plaintext secret must be encrypted at rest after the back-fill");
        _protector.IsProtected(healedRaw).Should().BeTrue();
        _protector.Unprotect(healedRaw).Should().Be(legacySecret, "the upgrade must be lossless");

        // The already-protected row is untouched (byte-identical); the null row stays null.
        (await RawMfaSecretAsync(protectedUserId)).Should().Be(alreadyProtected, "an already-protected row must not be re-wrapped");
        (await RawMfaSecretAsync(noMfaUserId)).Should().BeNull("a user with no MFA secret must not be given one");

        // AFTER: the report counts zero legacy plaintext rows — the residue is gone, not merely reported.
        var after = await service.GetKeyUsageReportAsync();
        after.MfaSecretsLegacyPlaintext.Should().Be(0, "the back-fill healed the only legacy plaintext row");
    }

    // ── harness ─────────────────────────────────────────────────────────────

    private DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_cs, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

    // users is a GLOBAL table — the tenant context is irrelevant to it; a system context keeps the graph simple.
    private AppDbContext Db() => new(Options(), new TestTenantContext(Guid.Empty), _encryptor);

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Encryption:ActiveKeyId"] = "k", ["Encryption:Keys:k"] = _k }).Build());
        services.AddSingleton(_encryptor);
        services.AddSingleton(_protector); // the shared protector drives the report's legacy classification
        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(_cs, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITenantJobRunner, TenantJobRunner>();
        services.AddSingleton<IFieldEncryptionMaintenanceService, FieldEncryptionMaintenanceService>();
        return services.BuildServiceProvider();
    }

    private async Task<string?> RawMfaSecretAsync(Guid userId)
    {
        await using var db = Db();
        var rows = await db.Database
            .SqlQueryRaw<string?>("SELECT mfa_secret AS \"Value\" FROM users WHERE id = {0}", userId)
            .ToListAsync();
        return rows.Single();
    }

    /// <summary>Minimal resolved-tenant context for the raw test AppDbContext (mirrors the sibling PG tests).</summary>
    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
        public string Subdomain => "test";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status, string? plan = null,
            IReadOnlyCollection<string>? enabledModules = null, string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }
}
