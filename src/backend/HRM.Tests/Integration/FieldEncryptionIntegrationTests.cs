// ============================================================================
// P3-4: field-at-rest encryption — EF integration (InMemory).
//
// Proves the EF wiring end-to-end: a Pip (sensitive notes) and a Recommendation (per-employee compensation) written
// through an AppDbContext whose IFieldEncryptor is the REAL AES-GCM encryptor read back — via a FRESH scoped context
// — as the original plaintext / decimals (the converters encrypt on write, decrypt on read). Also asserts the model
// cache discriminates by encryptor type so a no-op-encryptor context and a real-encryptor context never collide.
//
// PROVIDER: InMemory (the verify gate runs `dotnet test` with no Docker). The RAW at-rest ciphertext is proven
// separately at the converter boundary (EncryptedValueConverterTests) and against a real column
// (FieldEncryptionPostgresTests).
// ============================================================================

using System.Security.Cryptography;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-PLT-P34")]
[Trait("Category", "FieldEncryption")]
public sealed class FieldEncryptionIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly IFieldEncryptor _encryptor = BuildEncryptor();

    private static IFieldEncryptor BuildEncryptor()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:ActiveKeyId"] = "k1",
                ["Encryption:Keys:k1"] = key,
            })
            .Build();
        return new AesGcmFieldEncryptor(config);
    }

    private ITenantContext TenantContext()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(_tenantId);
        ctx.IsResolved.Returns(true);
        ctx.IsSystemContext.Returns(false);
        return ctx;
    }

    private AppDbContext EncryptingDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, TenantContext(), _encryptor);
    }

    [Fact]
    public async Task Pip_sensitive_notes_round_trip_through_the_encrypting_context()
    {
        var pipId = Guid.NewGuid();

        await using (var db = EncryptingDb())
        {
            db.Pips.Add(new Pip
            {
                Id = pipId,
                TenantId = _tenantId,
                EmployeeId = Guid.NewGuid(),
                Reason = "Repeated missed deadlines in Q2 — confidential.",
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 3, 1),
                FinalOutcomeNotes = "Improved but not fully met.",
                EscalationNotes = "Escalating to a final written warning.",
            });
            await db.SaveChangesAsync();
        }

        await using (var fresh = EncryptingDb())
        {
            var pip = await fresh.Pips.SingleAsync(p => p.Id == pipId);
            pip.Reason.Should().Be("Repeated missed deadlines in Q2 — confidential.");
            pip.FinalOutcomeNotes.Should().Be("Improved but not fully met.");
            pip.EscalationNotes.Should().Be("Escalating to a final written warning.");
        }
    }

    // ISSUE-293: Employee.NationalId (first encrypted field on Employee) DECRYPT round-trips through the
    // REAL encryptor via a fresh context. (On InMemory this can't prove the ApplyEncryption WIRING or
    // at-rest ciphertext — a missing converter would still round-trip plaintext; the ciphertext + wiring
    // proof for the national_id column lives in FieldEncryptionPostgresTests.)
    [Fact]
    [Trait("TC", "TC-CHR-332")]
    public async Task Employee_national_id_round_trips_through_the_encrypting_context()
    {
        var empId = Guid.NewGuid();

        await using (var db = EncryptingDb())
        {
            db.Employees.Add(new Employee
            {
                Id = empId,
                TenantId = _tenantId,
                EmployeeNo = "EMP-1",
                FirstName = "Ada",
                LastName = "Lovelace",
                Email = "ada@e2e.test",
                NationalId = "SL-931204567V",
                DepartmentId = Guid.NewGuid(),
                JobTitleId = Guid.NewGuid(),
                EmploymentType = EmploymentType.FullTime,
                Status = EmployeeStatus.Active,
                IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        await using (var fresh = EncryptingDb())
        {
            var emp = await fresh.Employees.SingleAsync(e => e.Id == empId);
            emp.NationalId.Should().Be("SL-931204567V", "the encrypted National ID must decrypt back to the original");
        }
    }

    [Fact]
    public async Task Recommendation_compensation_round_trips_through_the_encrypting_context()
    {
        var recId = Guid.NewGuid();

        await using (var db = EncryptingDb())
        {
            db.Recommendations.Add(new Recommendation
            {
                Id = recId,
                TenantId = _tenantId,
                EmployeeId = Guid.NewGuid(),
                CycleId = Guid.NewGuid(),
                Type = RecommendationType.Increment,
                Status = RecommendationStatus.Draft,
                CurrentCompensation = 120000.00m,
                IncrementAmount = 12000.50m,
                IncrementPercent = 10.0000m,
                BonusAmount = null,
                BonusPercent = null,
            });
            await db.SaveChangesAsync();
        }

        await using (var fresh = EncryptingDb())
        {
            var rec = await fresh.Recommendations.SingleAsync(r => r.Id == recId);
            rec.CurrentCompensation.Should().Be(120000.00m);
            rec.IncrementAmount.Should().Be(12000.50m);
            rec.IncrementPercent.Should().Be(10.0000m);
            rec.BonusAmount.Should().BeNull();
            rec.BonusPercent.Should().BeNull();
        }
    }

    [Fact]
    public void Model_cache_discriminates_by_encryptor_type()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;

        using var encrypting = new AppDbContext(options, TenantContext(), _encryptor);
        using var plain = new AppDbContext(options, TenantContext()); // no encryptor ⇒ NoOpFieldEncryptor

        // ISSUE-333 widened this contract: the discriminator is no longer the bare type name, it now also
        // carries the KEY-RING identity (see IFieldEncryptor.ModelCacheDiscriminator). Asserting StartWith
        // rather than Be keeps this arm's original guarantee — an encrypting model and a plaintext model must
        // never share a compiled EF model — without re-pinning the exact string, which is now intentionally
        // richer. Loosening it to a bare NotBe would have been the lazy fix and would have stopped verifying
        // that each context reports the RIGHT encryptor.
        encrypting.FieldEncryptorDiscriminator.Should().StartWith(nameof(AesGcmFieldEncryptor));
        plain.FieldEncryptorDiscriminator.Should().Be(nameof(NoOpFieldEncryptor));
        encrypting.FieldEncryptorDiscriminator.Should().NotBe(plain.FieldEncryptorDiscriminator);
    }

    // -------- ISSUE-333: the WIRING arm — AppDbContext must consume the ring-aware discriminator --------
    // My first pass at this fix pinned the discriminator contract on the encryptor but left the AppDbContext
    // wiring unpinned: reverting `_fieldEncryptor.ModelCacheDiscriminator` back to `GetType().Name` — the exact
    // original bug — kept every arm green. That is the same "looks covered but isn't" pattern this session has
    // caught three times elsewhere, so it gets its own arm here. Mutation-verified: this fails under the revert.
    [Fact]
    public void Model_cache_discriminates_by_KEY_RING_not_just_type_ISSUE333()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;

        var ringOne = new AesGcmFieldEncryptor(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Encryption:ActiveKeyId"] = "k1",
                ["Encryption:Keys:k1"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                ["Encryption:Keys:k2"] = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=",
            }).Build());

        var ringTwo = new AesGcmFieldEncryptor(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Encryption:ActiveKeyId"] = "hrm-field-key-1",
                ["Encryption:Keys:hrm-field-key-1"] = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC=",
            }).Build());

        using var ctxOne = new AppDbContext(options, TenantContext(), ringOne);
        using var ctxTwo = new AppDbContext(options, TenantContext(), ringTwo);

        ctxOne.FieldEncryptorDiscriminator.Should().NotBe(ctxTwo.FieldEncryptorDiscriminator,
            "both contexts hold an AesGcmFieldEncryptor, so a type-based key cannot separate them — yet ringTwo "
            + "has no 'k2', and sharing a compiled model across them is exactly what produced the intermittent "
            + "\"No key 'k2' is present in the encryption key ring\" failure (ISSUE-333)");
    }
}
