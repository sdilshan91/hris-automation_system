namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Application-side, reversible encryption for individual sensitive HR fields stored at rest (P3-4 —
/// PII/financial field encryption). Applied via EF Core value converters on the PIP free-text notes
/// (US-PRF-008 NFR-4) and the per-employee Recommendation compensation figures (US-PRF-010 NFR-3).
/// </summary>
/// <remarks>
/// <para><b>Null-safe:</b> <see cref="Encrypt"/>/<see cref="Decrypt"/> return <c>null</c> for a <c>null</c>
/// input — a null column stays null (never a ciphertext blob).</para>
/// <para><b>Legacy tolerance:</b> <see cref="Decrypt"/> returns a value that is NOT in the encryptor's stored
/// format verbatim, so rows written before encryption was introduced (plain text / plain numeric strings)
/// still read back correctly during the one-time data-migration window. It must NOT, however, silently fall
/// back to plaintext on a <b>tamper/authentication failure</b> of a genuinely-encrypted value — that throws.</para>
/// </remarks>
public interface IFieldEncryptor
{
    /// <summary>Encrypts <paramref name="plaintext"/> for storage. <c>null</c> in ⇒ <c>null</c> out.</summary>
    string? Encrypt(string? plaintext);

    /// <summary>
    /// Decrypts a stored value. <c>null</c> in ⇒ <c>null</c> out. A value not in the encryptor's stored format
    /// is treated as legacy plaintext and returned unchanged; a genuinely-encrypted value that fails
    /// authentication (tampered / wrong key) throws rather than leaking a plaintext fallback.
    /// </summary>
    string? Decrypt(string? stored);

    /// <summary>
    /// Identity used by <c>EncryptorAwareModelCacheKeyFactory</c> to decide whether two <c>AppDbContext</c>
    /// instances may share one compiled EF model. Two encryptors may share a model ONLY if this string matches.
    ///
    /// <para><b>Why this is not just the type name (ISSUE-333).</b> The EF model cache is process-wide, and the
    /// encryption value converters close over a specific encryptor instance. Keying on the CLR type alone means
    /// two <c>AesGcmFieldEncryptor</c>s holding <i>different key rings</i> share one cached model — so whichever
    /// context compiles the model first bakes ITS converters in for the whole process, and every later context
    /// silently decrypts with the wrong ring. In production that is harmless (one ring per process). In the test
    /// suite it is not: classes register different rings, and under parallel load the compile order varies, which
    /// produced an intermittent <c>CryptographicException: No key 'k2' is present in the encryption key ring</c>
    /// in whichever class lost the race. Including the ring identity makes that collision impossible rather than
    /// unlikely.</para>
    /// </summary>
    string ModelCacheDiscriminator => GetType().Name;
}

/// <summary>
/// Identity <see cref="IFieldEncryptor"/> — returns values unchanged. It performs NO encryption; it exists only
/// so an <c>AppDbContext</c> constructed WITHOUT a real encryptor (isolated unit tests using the plain
/// <c>(options, tenantContext)</c> constructor) behaves exactly as before this feature (plaintext round-trips
/// through the value converters). The production composition always injects the real
/// <c>AesGcmFieldEncryptor</c> singleton, so real data is never stored through this.
/// </summary>
public sealed class NoOpFieldEncryptor : IFieldEncryptor
{
    public static readonly NoOpFieldEncryptor Instance = new();

    public string? Encrypt(string? plaintext) => plaintext;

    public string? Decrypt(string? stored) => stored;
}
