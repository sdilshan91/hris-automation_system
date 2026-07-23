// ============================================================================
// Key-rotation tooling — the shared EncryptedFieldRegistry (drift-proofing, the DF-19 lesson) and the pure
// per-value re-encryption core (FieldEncryptionMaintenanceService.TryReencrypt).
//
//   1. The registry CONTENT is pinned (exact table/column/back-fill-flag set). Removing or altering an entry
//      goes red HERE, and because BOTH the DbInitializer startup back-fill and the re-encryption sweep
//      enumerate this registry, the pin covers both consumers at once.
//   2. Every registry entry is bound to the EF MODEL: the named entity/property exists and actually carries a
//      value converter to a string provider type (the encryption converter). A registry row pointing at an
//      un-encrypted property — or a rename that breaks the pairing — goes red here.
//   3. TryReencrypt: old-key values re-encrypt under the active key and still decrypt to the original;
//      active-key values are reported AlreadyActiveKey with NO output value (the sweep must never rewrite
//      them); plaintext is NotEncrypted; corrupt / unknown-key values are Undecryptable (skipped, never
//      throws, never yields a value to write).
//
// The end-to-end sweep over real raw columns needs real Postgres → FieldEncryptionReencryptPostgresTests
// (Testcontainers; executed by the orchestrator's Postgres run).
// ============================================================================

using System.Security.Cryptography;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Converters;
using HRM.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace HRM.Tests.Unit;

[Trait("TC", "TC-PLT-003")] // encrypted-field registry pins + model↔registry drift guards (both directions)
public sealed class EncryptedFieldRegistryTests
{
    // ── 1. Registry content pin ─────────────────────────────────────────────

    [Fact]
    public void Registry_pins_the_exact_encrypted_column_set()
    {
        EncryptedFieldRegistry.Fields
            .Select(f => (f.Table, f.Column, f.IncludeInStartupBackfill))
            .Should().BeEquivalentTo(new[]
            {
                ("pip", "reason", true),
                ("pip", "final_outcome_notes", true),
                ("pip", "escalation_notes", true),
                ("recommendation", "current_compensation", true),
                ("recommendation", "bonus_amount", true),
                ("recommendation", "bonus_percent", true),
                ("recommendation", "increment_amount", true),
                ("recommendation", "increment_percent", true),
                // ISSUE-293: born-encrypted (no plaintext history) → in the rotation sweep but NOT the
                // startup back-fill, keeping DbInitializer byte-identical to its pre-registry column set.
                ("employees", "national_id", false),
            });
    }

    [Fact]
    public void StartupBackfillFields_is_the_original_p34_set_without_national_id()
    {
        var backfill = EncryptedFieldRegistry.StartupBackfillFields.ToList();

        backfill.Should().HaveCount(8);
        backfill.Should().NotContain(f => f.Column == "national_id",
            "employees.national_id was BORN encrypted — including it would change the pinned "
            + "DbInitializer back-fill behaviour");
        backfill.Select(f => f.Table).Distinct().Should().BeEquivalentTo("pip", "recommendation");
    }

    // ── 2. Registry ↔ EF model binding (every entry names a real, converter-carrying property) ──

    [Fact]
    public void Every_registry_entry_is_bound_to_an_encrypted_model_property()
    {
        // InMemory-through-real-EF with the REAL AES-GCM encryptor: OnModelCreating applies the encryption
        // converters regardless of provider, so the model itself proves the wiring.
        using var db = BuildInMemoryContext();

        foreach (var field in EncryptedFieldRegistry.Fields)
        {
            var entityType = db.Model.GetEntityTypes()
                .SingleOrDefault(t => t.ClrType.Name == field.Entity);
            entityType.Should().NotBeNull($"registry entity '{field.Entity}' must exist in the EF model");

            var property = entityType!.FindProperty(field.Property);
            property.Should().NotBeNull(
                $"registry property '{field.Entity}.{field.Property}' must exist in the EF model");

            var converter = property!.GetValueConverter();
            converter.Should().NotBeNull(
                $"'{field.Entity}.{field.Property}' is in EncryptedFieldRegistry, so it must carry the "
                + "field-encryption value converter (ApplyEncryption wiring)");
            converter.Should().BeAssignableTo<IEncryptedValueConverter>(
                $"'{field.Entity}.{field.Property}' must carry the ENCRYPTION converter specifically — "
                + "any other converter would leave the column plaintext at rest");
            converter!.ProviderClrType.Should().Be(typeof(string),
                "the encryption converter stores ciphertext as text");
        }
    }

    [Fact]
    public void Every_encrypted_model_property_is_listed_in_the_registry()
    {
        // REVERSE drift direction (the data-destroying one): a property wired through ApplyEncryption but
        // missing from EncryptedFieldRegistry would never be re-encrypted on key rotation — retiring the old
        // key would destroy that column's data. Encryption converters are identified by CLR type (they all
        // implement IEncryptedValueConverter), so enum/json/other converters can never false-positive.
        using var db = BuildInMemoryContext();

        var encryptedModelProperties = db.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties(), (t, p) => (Type: t, Property: p))
            .Where(x => x.Property.GetValueConverter() is IEncryptedValueConverter)
            .Select(x => new { Entity = x.Type.ClrType.Name, Property = x.Property.Name })
            .ToList();

        encryptedModelProperties.Should().BeEquivalentTo(
            EncryptedFieldRegistry.Fields.Select(f => new { f.Entity, f.Property }),
            "the EF model's encrypted-property set and EncryptedFieldRegistry must match EXACTLY — "
            + "add every new ApplyEncryption property to the registry (and vice versa)");
    }

    // ── 3. TryReencrypt — the pure per-value sweep core ─────────────────────

    [Fact]
    public void TryReencrypt_moves_an_old_key_value_to_the_active_key_and_preserves_the_plaintext()
    {
        var (k1Only, ring) = BuildKeyRing();
        var stored = k1Only.Encrypt("Confidential PIP reason.")!;
        stored.Should().StartWith("enc:v1:k1:");

        var outcome = FieldEncryptionMaintenanceService.TryReencrypt(stored, "k2", ring, out var reencrypted);

        outcome.Should().Be(FieldReencryptOutcome.Reencrypted);
        reencrypted.Should().StartWith("enc:v1:k2:");
        ring.Decrypt(reencrypted).Should().Be("Confidential PIP reason.");
    }

    [Fact]
    public void TryReencrypt_reports_an_active_key_value_without_producing_any_output_to_write()
    {
        var (_, ring) = BuildKeyRing();
        var stored = ring.Encrypt("already current")!; // ring's active key is k2

        var outcome = FieldEncryptionMaintenanceService.TryReencrypt(stored, "k2", ring, out var reencrypted);

        outcome.Should().Be(FieldReencryptOutcome.AlreadyActiveKey);
        reencrypted.Should().BeNull("the sweep must never rewrite a value already under the active key");
    }

    [Fact]
    public void TryReencrypt_reports_legacy_plaintext_as_NotEncrypted()
    {
        var (_, ring) = BuildKeyRing();

        var outcome = FieldEncryptionMaintenanceService.TryReencrypt(
            "just plain text", "k2", ring, out var reencrypted);

        outcome.Should().Be(FieldReencryptOutcome.NotEncrypted);
        reencrypted.Should().BeNull();
    }

    [Fact]
    public void TryReencrypt_skips_a_corrupt_value_without_throwing_or_yielding_output()
    {
        var (_, ring) = BuildKeyRing();
        // Valid prefix + known keyId, but the payload is 3 bytes — too short for nonce+tag → CryptographicException.
        var corrupt = "enc:v1:k1:" + Convert.ToBase64String(new byte[] { 1, 2, 3 });

        var outcome = FieldEncryptionMaintenanceService.TryReencrypt(corrupt, "k2", ring, out var reencrypted);

        outcome.Should().Be(FieldReencryptOutcome.Undecryptable);
        reencrypted.Should().BeNull("a corrupt original must never be overwritten with garbage");
    }

    [Fact]
    public void TryReencrypt_skips_a_value_whose_key_is_missing_from_the_ring()
    {
        var (k1Only, ring) = BuildKeyRing();
        // A structurally-valid ciphertext under a keyId ("kX") the ring does not hold.
        var underUnknownKey = k1Only.Encrypt("orphaned")!.Replace("enc:v1:k1:", "enc:v1:kX:");

        var outcome = FieldEncryptionMaintenanceService.TryReencrypt(
            underUnknownKey, "k2", ring, out var reencrypted);

        outcome.Should().Be(FieldReencryptOutcome.Undecryptable);
        reencrypted.Should().BeNull();
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// (writer under k1 only, full ring with ACTIVE k2 + retired k1) — the exact mid-rotation shape: old
    /// values were written when k1 was active; the ring now holds both with k2 active.
    /// </summary>
    private static (IFieldEncryptor K1Writer, IFieldEncryptor Ring) BuildKeyRing()
    {
        var k1 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var k2 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return (BuildEncryptor(("k1", k1), active: "k1"),
                BuildEncryptor([("k1", k1), ("k2", k2)], active: "k2"));
    }

    private static IFieldEncryptor BuildEncryptor((string Id, string Key) key, string active)
        => BuildEncryptor([key], active);

    private static IFieldEncryptor BuildEncryptor((string Id, string Key)[] keys, string active)
    {
        var settings = new Dictionary<string, string?> { ["Encryption:ActiveKeyId"] = active };
        foreach (var (id, key) in keys)
            settings[$"Encryption:Keys:{id}"] = key;
        return new AesGcmFieldEncryptor(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    private static AppDbContext BuildInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = Substitute.For<ITenantContext>();
        var (_, ring) = BuildKeyRing();
        return new AppDbContext(options, tenantContext, ring);
    }
}
