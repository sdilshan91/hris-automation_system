// ============================================================================
// Key-rotation tooling — the bulk re-encryption sweep + key-usage report on REAL Postgres (Testcontainers).
//
// Proves what InMemory cannot (the sweep is raw-SQL over the RAW stored column values, which only exist on a
// relational store):
//   1. Values stored under a RETIRED ring key (k1) are re-encrypted under the ACTIVE key (k2) and still
//      decrypt to the original plaintext through EF — across pip, recommendation AND employees.national_id
//      (the ISSUE-293 column the old hand-maintained back-fill list missed → the registry closes that drift).
//   2. Rows already under the active key are BYTE-IDENTICAL afterwards (exact raw string equality — the sweep
//      never rewrites them, so their ciphertext/nonce never churns).
//   3. An undecryptable value (unknown keyId) is skipped + counted, the sweep continues past it, never
//      crashes, and never overwrites the original; a SECOND run re-skips it and re-encrypts nothing
//      (idempotent / re-run safe).
//   4. The per-keyId usage report (the key-retirement verification gate) counts correctly BEFORE and AFTER.
//   5. The sweep is per-tenant via ITenantJobRunner (the AutoClockOutJob cross-tenant pattern — under RLS-ON
//      the GUC gets set, so the scan can never silently fail-closed to 0 rows) and DELIBERATELY includes a
//      Suspended tenant: its ciphertext must move off the old key too, or retiring the key destroys its data.
//   6. The REGISTRY-driven startup back-fill: plaintext in a StartupBackfillFields column (pip.reason) is
//      encrypted by DbInitializer.EncryptSensitiveFieldsAtRestAsync, while plaintext in the EXCLUDED
//      employees.national_id column is left byte-identical (the pinned pre-registry behaviour) but still
//      SURFACES in the usage report under the "(plaintext)" pseudo key — visible, never silently ignored.
//
// Runs against a throwaway postgres:17-alpine container (mirrors FieldEncryptionPostgresTests). The agent
// verify gate has no Docker, so this arm is executed by the orchestrator's Postgres run.
// ============================================================================

using System.Security.Cryptography;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Security;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-PLT-P34")]
[Trait("TC", "TC-PLT-004")] // real-PG bulk re-encrypt sweep + key-usage report (retirement gate), incl. soft-deleted tenant
[Trait("Category", "FieldEncryption")]
public sealed class FieldEncryptionReencryptPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    // STATIC (not per-instance): EF caches the model — and its encryption converter — process-wide, so the key
    // baked into the first-built converter must match every test instance's ring. Per-instance random keys made a
    // row encrypted under instance A's key fail to decrypt under instance B's cached converter (tag mismatch) when
    // facts run together. Deterministic keys keep the whole process consistent.
    private static readonly string _k1 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static readonly string _k2 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    // Writer frozen at the RETIRED key (k1) — used ONLY to fabricate "written before the rotation" raw
    // values; it never backs a DbContext (one AesGcm instance per process backs the cached EF model).
    private IFieldEncryptor _k1Writer = null!;

    // THE ring: active k2 + retired k1 — backs every DbContext AND the maintenance service.
    private IFieldEncryptor _ring = null!;

    private string _cs = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _cs = _postgres.GetConnectionString() + ";Include Error Detail=true";
        _k1Writer = BuildEncryptor([("k1", _k1)], active: "k1");
        _ring = BuildEncryptor([("k1", _k1), ("k2", _k2)], active: "k2");

        await using var migrate = Db(Guid.NewGuid());
        await migrate.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Reencrypt_sweep_moves_old_key_values_leaves_active_rows_byte_identical_and_skips_corrupt()
    {
        // ── Arrange: an Active tenant + a Suspended tenant, mid-rotation state ──
        var tenantActive = Guid.NewGuid();
        var tenantSuspended = Guid.NewGuid();
        await SeedTenantRowAsync(tenantActive, "reenc-a", TenantStatus.Active);
        await SeedTenantRowAsync(tenantSuspended, "reenc-s", TenantStatus.Suspended);

        // One employee PER Pip: ix_pip_tenant_id_employee_id enforces a single PIP per employee per tenant
        // (Postgres rejects a second with 23505 — InMemory doesn't), so each Pip row gets its own FK chain.
        var a1 = await SeedFkChainAsync(tenantActive); // old-key pip + recommendation + national_id
        var a2 = await SeedFkChainAsync(tenantActive); // active-key (byte-identical) pip
        var a3 = await SeedFkChainAsync(tenantActive); // corrupt (undecryptable) pip
        var s = await SeedFkChainAsync(tenantSuspended);

        Guid pipOldKey, pipActiveKey, pipCorrupt, recOldKey, pipSuspended;
        await using (var db = Db(tenantActive))
        {
            pipOldKey = await AddPipAsync(db, tenantActive, a1.EmployeeId, "Legacy k1 pip reason.");
            pipActiveKey = await AddPipAsync(db, tenantActive, a2.EmployeeId, "Already-current k2 reason.");
            pipCorrupt = await AddPipAsync(db, tenantActive, a3.EmployeeId, "Reason to be corrupted.");
            recOldKey = await AddRecommendationAsync(db, tenantActive, a1.EmployeeId, a1.CycleId, 1234.50m);
        }
        await using (var db = Db(tenantSuspended))
        {
            pipSuspended = await AddPipAsync(db, tenantSuspended, s.EmployeeId, "Suspended tenant secret.");
        }

        // Rewrite the "old" rows to k1 ciphertext (exactly what a pre-rotation write left behind), corrupt one
        // value under an unknown keyId, and put national_id under k1 too (the column the old list missed).
        await using (var db = Db(tenantActive))
        {
            await RawUpdateAsync(db, "pip", "reason", pipOldKey, _k1Writer.Encrypt("Legacy k1 pip reason.")!);
            await RawUpdateAsync(db, "recommendation", "bonus_amount", recOldKey, _k1Writer.Encrypt("1234.50")!);
            await RawUpdateAsync(db, "employees", "national_id", a1.EmployeeId, _k1Writer.Encrypt("SL-OLD-1")!);
            await RawUpdateAsync(db, "pip", "reason", pipCorrupt,
                "enc:v1:kX:" + Convert.ToBase64String(new byte[] { 1, 2, 3 })); // unknown key + short payload
        }
        await using (var db = Db(tenantSuspended))
        {
            await RawUpdateAsync(db, "pip", "reason", pipSuspended, _k1Writer.Encrypt("Suspended tenant secret.")!);
        }

        var activeRowRawBefore = await RawScalarAsync(Db(tenantActive), "pip", "reason", pipActiveKey);
        activeRowRawBefore.Should().StartWith("enc:v1:k2:");

        await using var provider = BuildProvider();
        var service = provider.GetRequiredService<IFieldEncryptionMaintenanceService>();

        // ── BEFORE report: 4 values under the retired key, 1 under the unknown key ──
        var before = await service.GetKeyUsageReportAsync();
        before.ActiveKeyId.Should().Be("k2");
        before.TotalsByKeyId.Should().ContainKey("k1").WhoseValue.Should().Be(4);
        before.TotalsByKeyId.Should().ContainKey("kX").WhoseValue.Should().Be(1);

        // ── Act: the sweep ──
        var summary = await service.ReencryptAsync();

        // ── Assert: summary + BEFORE/AFTER report (the key-retirement verification gate) ──
        summary.ActiveKeyId.Should().Be("k2");
        summary.RowsReencrypted.Should().Be(4, "3 Active-tenant values + 1 Suspended-tenant value sat under k1");
        summary.RowsSkippedUndecryptable.Should().Be(1);
        summary.Before.TotalsByKeyId["k1"].Should().Be(4);
        summary.After.TotalsByKeyId.Should().NotContainKey("k1",
            "0 rows on the old key AFTER the sweep is the retire-safe signal");
        summary.After.TotalsByKeyId.Should().ContainKey("kX").WhoseValue.Should().Be(1,
            "the undecryptable row stays under its unknown key — surfaced, never overwritten");

        // Re-encrypted rows: raw ciphertext now under k2, and EF still decrypts to the ORIGINAL plaintext.
        (await RawScalarAsync(Db(tenantActive), "pip", "reason", pipOldKey)).Should().StartWith("enc:v1:k2:");
        (await RawScalarAsync(Db(tenantActive), "recommendation", "bonus_amount", recOldKey)).Should().StartWith("enc:v1:k2:");
        (await RawScalarAsync(Db(tenantActive), "employees", "national_id", a1.EmployeeId)).Should().StartWith("enc:v1:k2:");
        (await RawScalarAsync(Db(tenantSuspended), "pip", "reason", pipSuspended)).Should().StartWith("enc:v1:k2:",
            "a Suspended tenant's data must also move off the old key before it can be retired");

        await using (var db = Db(tenantActive))
        {
            (await db.Pips.SingleAsync(p => p.Id == pipOldKey)).Reason.Should().Be("Legacy k1 pip reason.");
            (await db.Recommendations.SingleAsync(r => r.Id == recOldKey)).BonusAmount.Should().Be(1234.50m);
            (await db.Employees.SingleAsync(e => e.Id == a1.EmployeeId)).NationalId.Should().Be("SL-OLD-1");
        }
        await using (var db = Db(tenantSuspended))
        {
            (await db.Pips.SingleAsync(p => p.Id == pipSuspended)).Reason.Should().Be("Suspended tenant secret.");
        }

        // Active-key row: BYTE-IDENTICAL (exact string equality — never rewritten, ciphertext never churns).
        (await RawScalarAsync(Db(tenantActive), "pip", "reason", pipActiveKey))
            .Should().Be(activeRowRawBefore, "a value already under the active key must not be rewritten");

        // Corrupt row: original bytes untouched.
        (await RawScalarAsync(Db(tenantActive), "pip", "reason", pipCorrupt))
            .Should().Be("enc:v1:kX:" + Convert.ToBase64String(new byte[] { 1, 2, 3 }));

        // ── Second run: idempotent — nothing left to re-encrypt, the corrupt row is re-skipped, no crash ──
        var second = await service.ReencryptAsync();
        second.RowsReencrypted.Should().Be(0);
        second.RowsSkippedUndecryptable.Should().Be(1);
    }

    [Fact]
    public async Task Registry_backfill_encrypts_pip_plaintext_but_leaves_born_encrypted_national_id_and_reports_it()
    {
        var tenantId = Guid.NewGuid();
        await SeedTenantRowAsync(tenantId, "reenc-bf", TenantStatus.Active);
        var (employeeId, _) = await SeedFkChainAsync(tenantId);

        Guid pipId;
        await using (var db = Db(tenantId))
        {
            pipId = await AddPipAsync(db, tenantId, employeeId, "placeholder");
            // Model a pre-P3-4 row (plaintext pip.reason) and a hypothetical plaintext national_id.
            await RawUpdateAsync(db, "pip", "reason", pipId, "Legacy plaintext reason.");
            await RawUpdateAsync(db, "employees", "national_id", employeeId, "SL-PLAINTEXT-1");
        }

        await using (var db = Db(tenantId))
        {
            await DbInitializer.EncryptSensitiveFieldsAtRestAsync(
                db, _ring, NullLogger.Instance, CancellationToken.None);
        }

        // pip.reason is a StartupBackfillFields column → encrypted (registry-driven back-fill still works).
        (await RawScalarAsync(Db(tenantId), "pip", "reason", pipId)).Should().StartWith("enc:v1:k2:");

        // employees.national_id is EXCLUDED from the startup back-fill (born-encrypted; pinned pre-registry
        // behaviour) → byte-identical plaintext ...
        (await RawScalarAsync(Db(tenantId), "employees", "national_id", employeeId)).Should().Be("SL-PLAINTEXT-1");

        // ... but the usage report SURFACES it under the "(plaintext)" pseudo key — never silently ignored.
        await using var provider = BuildProvider();
        var report = await provider.GetRequiredService<IFieldEncryptionMaintenanceService>()
            .GetKeyUsageReportAsync();
        report.Counts.Should().Contain(c =>
            c.Table == "employees" && c.Column == "national_id"
            && c.KeyId == FieldEncryptionMaintenanceService.PlaintextKeyId && c.Count == 1,
            "exactly this test's one plaintext national_id row exists — an exact count keeps the "
            + "report's arithmetic honest");
    }

    [Fact]
    public async Task Sweep_and_report_include_a_soft_deleted_tenant()
    {
        // Regression pin for the IgnoreQueryFilters fix: Tenant carries a global `!IsDeleted` query filter, so
        // a bare db.Tenants enumeration would silently DROP a soft-deleted tenant from both the sweep and the
        // retirement-gate report — its old-key rows would be invisible, the gate would read 0, and retiring
        // the key would destroy the tenant's data if it is later RESTORED (US-ADM-004).
        var tenantId = Guid.NewGuid();
        await SeedTenantRowAsync(tenantId, "reenc-del", TenantStatus.Suspended, isDeleted: true);
        var (employeeId, _) = await SeedFkChainAsync(tenantId);

        Guid pipId;
        await using (var db = Db(tenantId))
        {
            pipId = await AddPipAsync(db, tenantId, employeeId, "Soft-deleted tenant secret.");
            await RawUpdateAsync(db, "pip", "reason", pipId, _k1Writer.Encrypt("Soft-deleted tenant secret.")!);
        }

        await using var provider = BuildProvider();
        var service = provider.GetRequiredService<IFieldEncryptionMaintenanceService>();

        // The report SEES the soft-deleted tenant's old-key row (exactly one k1 row exists at this point:
        // other tests either sweep their k1 rows away or never create any).
        var before = await service.GetKeyUsageReportAsync();
        before.TotalsByKeyId.Should().ContainKey("k1").WhoseValue.Should().Be(1);

        // The sweep re-encrypts it.
        var summary = await service.ReencryptAsync();
        summary.RowsReencrypted.Should().Be(1);
        summary.After.TotalsByKeyId.Should().NotContainKey("k1");

        (await RawScalarAsync(Db(tenantId), "pip", "reason", pipId)).Should().StartWith("enc:v1:k2:");
        await using (var db = Db(tenantId))
        {
            (await db.Pips.SingleAsync(p => p.Id == pipId)).Reason.Should().Be("Soft-deleted tenant secret.");
        }
    }

    // ── harness ─────────────────────────────────────────────────────────────

    private static IFieldEncryptor BuildEncryptor((string Id, string Key)[] keys, string active)
    {
        var settings = new Dictionary<string, string?> { ["Encryption:ActiveKeyId"] = active };
        foreach (var (id, key) in keys)
            settings[$"Encryption:Keys:{id}"] = key;
        return new AesGcmFieldEncryptor(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    private DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_cs, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

    private AppDbContext Db(Guid tenantId) => new(Options(), new TestTenantContext(tenantId), _ring);

    /// <summary>
    /// The maintenance service over a REAL DI graph: real scoped TenantJobRunner (Rls:Enabled defaults false
    /// here — the RLS-ON arm of the runner is covered by RlsIsolationPostgresTests) + the shared ring encryptor.
    /// </summary>
    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Encryption:ActiveKeyId"] = "k2",
                ["Encryption:Keys:k1"] = _k1,
                ["Encryption:Keys:k2"] = _k2,
            }).Build());
        services.AddSingleton(_ring);
        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(_cs, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITenantJobRunner, TenantJobRunner>();
        services.AddSingleton<IFieldEncryptionMaintenanceService, FieldEncryptionMaintenanceService>();
        return services.BuildServiceProvider();
    }

    private async Task SeedTenantRowAsync(
        Guid tenantId, string subdomain, TenantStatus status, bool isDeleted = false)
    {
        await using var db = Db(tenantId);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Subdomain = subdomain, Name = subdomain, Status = status,
            PlanId = "default", CreatedAt = DateTime.UtcNow, IsDeleted = isDeleted,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>FK parents Postgres enforces (InMemory doesn't): Department + JobTitle → Employee, + a cycle.</summary>
    private async Task<(Guid EmployeeId, Guid CycleId)> SeedFkChainAsync(Guid tenantId)
    {
        var deptId = Guid.NewGuid();
        var jobTitleId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var cycleId = Guid.NewGuid();
        var suffix = empId.ToString("N")[..6];

        await using var db = Db(tenantId);
        db.Departments.Add(new Department
        {
            Id = deptId, TenantId = tenantId, Name = "Perf " + suffix, Code = "PERF-" + suffix, IsActive = true,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = jobTitleId, TenantId = tenantId, TitleName = "Engineer " + suffix, IsActive = true,
        });
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = tenantId, EmployeeNo = "EMP-" + suffix,
            FirstName = "Ada", LastName = "Lovelace", Email = suffix + "@t.com",
            Status = EmployeeStatus.Active, DepartmentId = deptId, JobTitleId = jobTitleId,
            DateOfJoining = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            NationalId = "SL-SEEDED-K2", // written under the ring's active key (k2) by the converter
        });
        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = cycleId, TenantId = tenantId, Name = "FY2026 " + suffix, Status = AppraisalCycleStatus.Completed,
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc), RatingScaleMax = 5,
        });
        await db.SaveChangesAsync();
        return (empId, cycleId);
    }

    private static async Task<Guid> AddPipAsync(AppDbContext db, Guid tenantId, Guid employeeId, string reason)
    {
        var id = Guid.NewGuid();
        db.Pips.Add(new Pip
        {
            Id = id, TenantId = tenantId, EmployeeId = employeeId, Reason = reason,
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1),
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> AddRecommendationAsync(
        AppDbContext db, Guid tenantId, Guid employeeId, Guid cycleId, decimal bonus)
    {
        var id = Guid.NewGuid();
        db.Recommendations.Add(new Recommendation
        {
            Id = id, TenantId = tenantId, EmployeeId = employeeId, CycleId = cycleId,
            Type = RecommendationType.Bonus, Status = RecommendationStatus.Draft, BonusAmount = bonus,
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>Raw column overwrite — models pre-rotation/legacy stored bytes. Identifiers are test constants.</summary>
    private static async Task RawUpdateAsync(AppDbContext db, string table, string column, Guid id, string value)
    {
        var sql = "UPDATE " + table + " SET " + column + " = {0} WHERE id = {1}";
        await db.Database.ExecuteSqlRawAsync(sql, value, id);
    }

    private async Task<string> RawScalarAsync(AppDbContext db, string table, string column, Guid id)
    {
        await using (db)
        {
            var sql = "SELECT " + column + " AS \"Value\" FROM " + table + " WHERE id = {0}";
            return await db.Database.SqlQueryRaw<string>(sql, id).SingleAsync();
        }
    }

    /// <summary>Minimal resolved-tenant context for the raw test AppDbContext (mirrors FieldEncryptionPostgresTests).</summary>
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
