// ============================================================================
// ISSUE-333 — the intermittent FieldEncryptionReencryptPostgresTests failure.
//
// Symptom: 3 arms failed on ~2 of every 8 full-suite runs, passed in isolation,
// and passed inside the Integration-only subset. That profile reads like resource
// contention, and it was mis-diagnosed as such twice (including by me) before a
// run captured at full verbosity produced the actual exception:
//
//     CryptographicException: No key 'k2' is present in the encryption key ring
//
// ...in a test that demonstrably registers k2. The cause is not load at all:
//
//   * the EF model cache is PROCESS-WIDE,
//   * the encryption value converters close over ONE IFieldEncryptor instance,
//   * and the cache key was `_fieldEncryptor.GetType().Name`.
//
// So two AesGcmFieldEncryptors holding DIFFERENT key rings shared one compiled
// model. Whichever context compiled it first baked its converters in for the whole
// process; every later context then decrypted with the wrong ring. Under parallel
// load the compile order varies -> intermittent. In isolation the class compiles
// its own model -> always passes. A deterministic cause behind a flaky symptom.
//
// Production was never affected (one ring per process), which is exactly why this
// hid in the test suite instead of being caught by a customer.
//
// These arms pin the discriminator contract so the collision cannot return.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace HRM.Tests.Unit;

public sealed class FieldEncryptorModelCacheDiscriminatorTests
{
    private const string KeyA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";  // 32 bytes, base64
    private const string KeyB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=";
    private const string KeyC = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC=";

    private static AesGcmFieldEncryptor Encryptor(string activeKeyId, params (string Id, string Material)[] ring)
    {
        var settings = new Dictionary<string, string?> { ["Encryption:ActiveKeyId"] = activeKeyId };
        foreach (var (id, material) in ring)
            settings[$"Encryption:Keys:{id}"] = material;

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new AesGcmFieldEncryptor(config);
    }

    // -------- TC-PLT-333-01: DIFFERENT key rings must NOT share a model --------
    // THE regression arm. Under the old `GetType().Name` key these two were identical, which is precisely
    // what let one instance's converters serve the other's queries. Both are AesGcmFieldEncryptor, so a
    // type-based discriminator cannot tell them apart — only a ring-aware one can.
    [Fact]
    public void Encryptors_WithDifferentKeyRings_DoNotShareAModelCacheKey_ISSUE333()
    {
        var ringWithK2 = Encryptor("k1", ("k1", KeyA), ("k2", KeyB));
        var ringWithoutK2 = Encryptor("hrm-field-key-1", ("hrm-field-key-1", KeyC));

        ringWithK2.GetType().Should().Be(ringWithoutK2.GetType(),
            "both are AesGcmFieldEncryptor — a type-based discriminator provably cannot separate them, which "
            + "is the whole reason ISSUE-333 was possible");

        ringWithK2.ModelCacheDiscriminator.Should().NotBe(ringWithoutK2.ModelCacheDiscriminator,
            "a model compiled against a ring containing k2 must never be reused by a context whose ring lacks "
            + "k2 — that is exactly the CryptographicException this fixes");
    }

    // -------- TC-PLT-333-02: the ACTIVE key is part of the identity --------
    // Same key material, different active key: writes would go out under a different key id, so the compiled
    // converters differ in behaviour even though the ring contents match.
    [Fact]
    public void Encryptors_WithSameRingButDifferentActiveKey_DoNotShareAModelCacheKey_ISSUE333()
    {
        var activeK1 = Encryptor("k1", ("k1", KeyA), ("k2", KeyB));
        var activeK2 = Encryptor("k2", ("k1", KeyA), ("k2", KeyB));

        activeK1.ModelCacheDiscriminator.Should().NotBe(activeK2.ModelCacheDiscriminator);
    }

    // -------- TC-PLT-333-03: identical rings DO share, regardless of declaration order --------
    // The fix must not over-fragment the cache: two contexts built from the same configuration must still
    // share one compiled model, or every DbContext construction pays a full model compile. Declaration order
    // must not matter, hence the ordinal sort in the discriminator.
    [Fact]
    public void Encryptors_WithIdenticalRings_ShareAModelCacheKey_RegardlessOfOrder_ISSUE333()
    {
        var first = Encryptor("k1", ("k1", KeyA), ("k2", KeyB));
        var second = Encryptor("k1", ("k2", KeyB), ("k1", KeyA));   // same ring, reversed declaration order

        second.ModelCacheDiscriminator.Should().Be(first.ModelCacheDiscriminator,
            "identical configuration must still share one compiled model — the fix must close the collision "
            + "without fragmenting the cache per instance");
    }

    // -------- TC-PLT-333-04: the discriminator must not leak key MATERIAL --------
    // It ends up in an in-memory cache key, but a discriminator containing raw key bytes could surface in a
    // heap dump or a diagnostic log. Key IDs are identifying enough.
    [Fact]
    public void Discriminator_ContainsKeyIdsButNeverKeyMaterial_ISSUE333()
    {
        var enc = Encryptor("k1", ("k1", KeyA), ("k2", KeyB));

        enc.ModelCacheDiscriminator.Should().Contain("k1").And.Contain("k2");
        enc.ModelCacheDiscriminator.Should().NotContain(KeyA).And.NotContain(KeyB,
            "key material must never appear in a cache key");
    }

    // -------- TC-PLT-333-05: the no-op encryptor stays distinct from any real one --------
    // The original guarantee (a plaintext model must never be shared with an encrypting model) must survive
    // the change.
    [Fact]
    public void NoOpEncryptor_StaysDistinctFromRealEncryptor_ISSUE333()
    {
        IFieldEncryptor noOp = NoOpFieldEncryptor.Instance;
        IFieldEncryptor real = Encryptor("k1", ("k1", KeyA));

        noOp.ModelCacheDiscriminator.Should().NotBe(real.ModelCacheDiscriminator,
            "a plaintext round-trip model must never be shared with an encrypting one — the pre-existing "
            + "guarantee this fix must not regress");
    }
}
