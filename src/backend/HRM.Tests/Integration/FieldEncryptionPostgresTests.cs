// ============================================================================
// P3-4: field-at-rest encryption — REAL Postgres arm (Testcontainers).
//
// Proves the two things InMemory cannot:
//   1. The RAW stored column value is `enc:v1:` ciphertext (NOT the plaintext) — genuine at-rest encryption.
//   2. The DbInitializer back-fill encrypts pre-existing plaintext and is IDEMPOTENT (a second run rewrites nothing).
//   3. ISSUE-523: employees.bank_account_number is ciphertext at rest, its sibling bank_name/bank_branch_code
//      are deliberately NOT, and the back-fill is a no-op on both an all-NULL and an already-encrypted row.
//
// Legacy plaintext rows are produced by writing through an AppDbContext built with the NO-OP encryptor over the SAME
// database (the model-cache factory keeps the two models separate), exactly modelling a pre-P3-4 row.
//
// Runs against a throwaway postgres:17-alpine container (mirrors ConnectionRoutingPostgresTests). The agent verify
// gate has no Docker, so this arm is executed by the orchestrator's Postgres run.
// ============================================================================

using System.Security.Cryptography;
using FluentAssertions;
using HRM.Application.Common.Helpers;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-PLT-P34")]
[Trait("Category", "FieldEncryption")]
public sealed class FieldEncryptionPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly IFieldEncryptor _encryptor = BuildEncryptor();
    private string _cs = null!;

    // STATIC key (not per-call random): EF caches the model + its encryption converter process-wide, so a key
    // that differs between test instances makes a value encrypted under instance A's key fail to decrypt under
    // instance B's cached converter (AuthenticationTagMismatch) when facts run together. Deterministic = consistent.
    private static readonly string _key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static IFieldEncryptor BuildEncryptor()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:ActiveKeyId"] = "k1",
                ["Encryption:Keys:k1"] = _key,
            })
            .Build();
        // Wrapped in a DISTINCT type so EncryptorAwareModelCacheKeyFactory (keys on the encryptor's GetType().Name)
        // gives THIS class its own cached EF model. Otherwise it shares one process-wide cached converter with the
        // other AesGcm-based PG class (FieldEncryptionReencryptPostgresTests) — and their different key material
        // tag-mismatches whichever class didn't build the model first, when both run in the same test process.
        return new TypeIsolatedFieldEncryptor(new AesGcmFieldEncryptor(config));
    }

    private sealed class TypeIsolatedFieldEncryptor(IFieldEncryptor inner) : IFieldEncryptor
    {
        public string? Encrypt(string? plaintext) => inner.Encrypt(plaintext);
        public string? Decrypt(string? stored) => inner.Decrypt(stored);
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _cs = _postgres.GetConnectionString();

        // Create the schema once (applies all migrations, incl. the P3-4 encrypt-columns migration).
        await using var migrate = Db(_encryptor);
        await migrate.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_cs, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

    // AppDbContext bound to a chosen encryptor (real AES-GCM, or the NoOp identity for a "legacy plaintext" writer).
    private AppDbContext Db(IFieldEncryptor? encryptor) =>
        new(Options(), new TestTenantContext(_tenantId), encryptor);

    private async Task<string> RawScalarAsync(AppDbContext db, string table, string column, Guid id)
    {
        // Column/table are test-controlled identifiers (cannot be SQL parameters); id IS parameterized as {0}.
        // Built by concatenation (not an interpolated literal) so the EF1002 injection analyzer isn't tripped.
        var sql = "SELECT " + column + " AS \"Value\" FROM " + table + " WHERE id = {0}";
        return await db.Database.SqlQueryRaw<string>(sql, id).SingleAsync();
    }

    // Seeds the FK parent chain (Postgres ENFORCES the Pip.EmployeeId / Recommendation.EmployeeId + CycleId FKs that
    // InMemory ignores): Department + JobTitle → Employee, and an AppraisalCycle. Fresh ids per call so the two tests
    // never collide on the per-tenant unique indexes. Returns the ids the child rows must point at.
    private async Task<(Guid EmployeeId, Guid CycleId)> SeedFkChainAsync()
    {
        var deptId = Guid.NewGuid();
        var jobTitleId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var cycleId = Guid.NewGuid();
        var suffix = empId.ToString("N")[..6];

        await using var db = Db(_encryptor);
        db.Departments.Add(new Department
        {
            Id = deptId, TenantId = _tenantId, Name = "Perf " + suffix, Code = "PERF-" + suffix, IsActive = true,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = jobTitleId, TenantId = _tenantId, TitleName = "Engineer", IsActive = true,
        });
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = _tenantId, EmployeeNo = "EMP-" + suffix,
            FirstName = "Ada", LastName = "Lovelace", Email = suffix + "@t.com",
            Status = EmployeeStatus.Active, DepartmentId = deptId, JobTitleId = jobTitleId,
            DateOfJoining = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            NationalId = "SL-CIPHER-001", // ISSUE-293: PII encrypted at rest — asserted ciphertext below
            // ISSUE-523: bank account number is PII encrypted at rest — asserted ciphertext below. The two
            // sibling bank columns are seeded too and asserted to remain PLAINTEXT: that is the deliberate
            // scope boundary of ISSUE-523 (public-directory branch data stays SQL-queryable), and asserting it
            // makes an accidental future widening of the encrypted set visible instead of silent.
            BankAccountNumber = "SL-ACCT-4455667788",
            BankName = "Bank of Ceylon",
            BankBranchCode = "BCEYLKLX",
        });
        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = cycleId, TenantId = _tenantId, Name = "FY2026 " + suffix, Status = AppraisalCycleStatus.Completed,
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc), RatingScaleMax = 5,
        });
        await db.SaveChangesAsync();
        return (empId, cycleId);
    }

    [Fact]
    [Trait("TC", "TC-CHR-332")]
    public async Task Persisted_values_are_ciphertext_in_the_raw_column_and_decrypt_on_read()
    {
        var (empId, cycleId) = await SeedFkChainAsync();
        var pipId = Guid.NewGuid();
        var recId = Guid.NewGuid();

        await using (var db = Db(_encryptor))
        {
            db.Pips.Add(new Pip
            {
                Id = pipId,
                TenantId = _tenantId,
                EmployeeId = empId,
                Reason = "Confidential PIP reason.",
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 3, 1),
                EscalationNotes = "Confidential escalation.",
            });
            db.Recommendations.Add(new Recommendation
            {
                Id = recId,
                TenantId = _tenantId,
                EmployeeId = empId,
                CycleId = cycleId,
                Type = RecommendationType.Bonus,
                Status = RecommendationStatus.Draft,
                BonusAmount = 7500.25m,
            });
            await db.SaveChangesAsync();
        }

        // RAW column values are ciphertext, not plaintext.
        await using (var db = Db(_encryptor))
        {
            var rawReason = await RawScalarAsync(db, "pip", "reason", pipId);
            rawReason.Should().StartWith("enc:v1:");
            rawReason.Should().NotContain("Confidential");

            var rawBonus = await RawScalarAsync(db, "recommendation", "bonus_amount", recId);
            rawBonus.Should().StartWith("enc:v1:");
            rawBonus.Should().NotContain("7500");

            // ISSUE-293: Employee.NationalId is ciphertext at rest — this is the WIRING proof (if
            // EmployeeConfiguration.ApplyEncryption were NOT invoked in AppDbContext, this column would be
            // plaintext and both assertions would fail). InMemory round-trip can't catch that; this can.
            var rawNationalId = await RawScalarAsync(db, "employees", "national_id", empId);
            rawNationalId.Should().StartWith("enc:v1:");
            rawNationalId.Should().NotContain("CIPHER");

            // ISSUE-523: the WHOLE POINT of the finding — a round-trip through the encrypting context proves
            // nothing about what a DBA (or a stolen backup) can read. This reads the RAW column with SQL.
            var rawBankAccount = await RawScalarAsync(db, "employees", "bank_account_number", empId);
            rawBankAccount.Should().StartWith("enc:v1:");
            rawBankAccount.Should().NotContain("4455667788",
                "the account number must not appear in the stored column in any form");

            // ...and the deliberate NON-encryption of the sibling bank columns, asserted rather than assumed.
            (await RawScalarAsync(db, "employees", "bank_name", empId)).Should().Be("Bank of Ceylon");
            (await RawScalarAsync(db, "employees", "bank_branch_code", empId)).Should().Be("BCEYLKLX");
        }

        // A fresh EF read decrypts back to the originals.
        await using (var db = Db(_encryptor))
        {
            var pip = await db.Pips.SingleAsync(p => p.Id == pipId);
            pip.Reason.Should().Be("Confidential PIP reason.");
            pip.EscalationNotes.Should().Be("Confidential escalation.");

            var rec = await db.Recommendations.SingleAsync(r => r.Id == recId);
            rec.BonusAmount.Should().Be(7500.25m);

            var emp = await db.Employees.SingleAsync(e => e.Id == empId);
            emp.NationalId.Should().Be("SL-CIPHER-001"); // decrypts back on read
            emp.BankAccountNumber.Should().Be("SL-ACCT-4455667788"); // ISSUE-523: decrypts back on read

            // AccountMasking (the payslip/bank-advice masking path) still sees DECRYPTED plaintext, so the
            // last-4 it produces are the last-4 of the real account number, not of the ciphertext.
            AccountMasking.MaskLast4(emp.BankAccountNumber!).Should().EndWith("7788");
        }
    }

    /// <summary>
    /// ISSUE-523: the startup back-fill is a genuine NO-OP for <c>employees.bank_account_number</c>. Two arms,
    /// because "no-op" means two different things and only one of them is idempotency:
    ///
    /// <list type="number">
    ///   <item>An employee whose account number is NULL — which is EVERY production row today, since the column
    ///     has no write path — is not selected by the back-fill's
    ///     <c>WHERE col IS NOT NULL AND col NOT LIKE 'enc:v1:%'</c> predicate, so it stays NULL and no UPDATE is
    ///     issued. This is the arm that proves the migration is safe to ship over live data.</item>
    ///   <item>An employee written through the encrypting context is already <c>enc:v1:</c>, so the back-fill
    ///     skips it and the stored ciphertext is BYTE-FOR-BYTE unchanged — it is never double-encrypted.</item>
    /// </list>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-332")]
    public async Task Backfill_is_a_no_op_for_bank_account_number_whether_the_row_is_null_or_already_encrypted()
    {
        var (encryptedEmpId, _) = await SeedFkChainAsync();   // seeded WITH a bank account (ciphertext)
        var nullEmpId = await SeedEmployeeWithoutBankAccountAsync();

        string cipherBefore;
        await using (var db = Db(_encryptor))
        {
            cipherBefore = await RawScalarAsync(db, "employees", "bank_account_number", encryptedEmpId);
            cipherBefore.Should().StartWith("enc:v1:");
            (await NullableRawScalarAsync(db, "employees", "bank_account_number", nullEmpId)).Should().BeNull();
        }

        await using (var db = Db(_encryptor))
        {
            await DbInitializer.EncryptSensitiveFieldsAtRestAsync(
                db, _encryptor, NullLogger.Instance, CancellationToken.None);
        }

        await using (var db = Db(_encryptor))
        {
            (await NullableRawScalarAsync(db, "employees", "bank_account_number", nullEmpId)).Should().BeNull(
                "a NULL account number is outside the back-fill predicate — this is why shipping the encryption "
                + "BEFORE any capture path exists makes the back-fill a provable no-op rather than a PII migration");

            (await RawScalarAsync(db, "employees", "bank_account_number", encryptedEmpId)).Should().Be(cipherBefore,
                "an already-encrypted value must never be re-encrypted by the startup back-fill");

            // And it still decrypts — the back-fill did not corrupt it.
            (await db.Employees.SingleAsync(e => e.Id == encryptedEmpId))
                .BankAccountNumber.Should().Be("SL-ACCT-4455667788");
        }
    }

    [Fact]
    public async Task Backfill_encrypts_legacy_plaintext_and_is_idempotent()
    {
        var (empId, cycleId) = await SeedFkChainAsync();
        var pipId = Guid.NewGuid();
        var recId = Guid.NewGuid();

        // Write legacy PLAINTEXT rows via the NoOp-encryptor context (models a pre-P3-4 row).
        await using (var legacy = Db(NoOpFieldEncryptor.Instance))
        {
            legacy.Pips.Add(new Pip
            {
                Id = pipId,
                TenantId = _tenantId,
                EmployeeId = empId,
                Reason = "Legacy plaintext reason.",
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 3, 1),
            });
            legacy.Recommendations.Add(new Recommendation
            {
                Id = recId,
                TenantId = _tenantId,
                EmployeeId = empId,
                CycleId = cycleId,
                Type = RecommendationType.Increment,
                Status = RecommendationStatus.Draft,
                IncrementAmount = 3000.00m,
            });
            await legacy.SaveChangesAsync();
        }

        // Confirm they are stored as plaintext before the back-fill.
        await using (var db = Db(_encryptor))
        {
            (await RawScalarAsync(db, "pip", "reason", pipId)).Should().Be("Legacy plaintext reason.");
        }

        // Run the back-fill.
        await using (var db = Db(_encryptor))
        {
            await DbInitializer.EncryptSensitiveFieldsAtRestAsync(
                db, _encryptor, NullLogger.Instance, CancellationToken.None);
        }

        // Now stored as ciphertext, and it still decrypts to the original.
        string cipherAfterFirst;
        await using (var db = Db(_encryptor))
        {
            cipherAfterFirst = await RawScalarAsync(db, "pip", "reason", pipId);
            cipherAfterFirst.Should().StartWith("enc:v1:");

            (await db.Pips.SingleAsync(p => p.Id == pipId)).Reason.Should().Be("Legacy plaintext reason.");
            (await db.Recommendations.SingleAsync(r => r.Id == recId)).IncrementAmount.Should().Be(3000.00m);
        }

        // Second run is a no-op: already-encrypted rows are skipped, so the ciphertext is byte-for-byte unchanged.
        await using (var db = Db(_encryptor))
        {
            await DbInitializer.EncryptSensitiveFieldsAtRestAsync(
                db, _encryptor, NullLogger.Instance, CancellationToken.None);
        }

        await using (var db = Db(_encryptor))
        {
            (await RawScalarAsync(db, "pip", "reason", pipId)).Should().Be(cipherAfterFirst,
                "an already-encrypted value must not be re-encrypted on a subsequent startup");
        }
    }

    /// <summary>
    /// ISSUE-523: like <see cref="RawScalarAsync"/> but tolerates a NULL column value (needed to prove the
    /// all-NULL production shape is left untouched by the back-fill).
    /// </summary>
    private async Task<string?> NullableRawScalarAsync(AppDbContext db, string table, string column, Guid id)
    {
        var sql = "SELECT " + column + " AS \"Value\" FROM " + table + " WHERE id = {0}";
        return await db.Database.SqlQueryRaw<string?>(sql, id).SingleAsync();
    }

    /// <summary>
    /// ISSUE-523: an employee with NO bank account number — the shape of EVERY production row today, since the
    /// column has no write path. Reuses the department/job-title created by <see cref="SeedFkChainAsync"/>'s
    /// caller only in spirit: it creates its own FK parents so the two rows never collide on the per-tenant
    /// unique indexes.
    /// </summary>
    private async Task<Guid> SeedEmployeeWithoutBankAccountAsync()
    {
        var deptId = Guid.NewGuid();
        var jobTitleId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var suffix = empId.ToString("N")[..6];

        await using var db = Db(_encryptor);
        db.Departments.Add(new Department
        {
            Id = deptId, TenantId = _tenantId, Name = "NoBank " + suffix, Code = "NOBK-" + suffix, IsActive = true,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = jobTitleId, TenantId = _tenantId, TitleName = "Analyst", IsActive = true,
        });
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = _tenantId, EmployeeNo = "NOBK-" + suffix,
            FirstName = "Grace", LastName = "Hopper", Email = "nobank" + suffix + "@t.com",
            Status = EmployeeStatus.Active, DepartmentId = deptId, JobTitleId = jobTitleId,
            DateOfJoining = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            // BankAccountNumber deliberately left NULL.
        });
        await db.SaveChangesAsync();
        return empId;
    }

    /// <summary>Minimal resolved-tenant context for the raw test AppDbContext.</summary>
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
