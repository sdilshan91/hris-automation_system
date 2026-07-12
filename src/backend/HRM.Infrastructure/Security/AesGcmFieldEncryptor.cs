using System.Security.Cryptography;
using System.Text;
using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace HRM.Infrastructure.Security;

/// <summary>
/// <see cref="IFieldEncryptor"/> backed by <b>AES-256-GCM</b> (authenticated encryption) with a rotation-ready
/// key ring, mirroring the <c>JwtService</c> key-ring style (P3-4). Registered as a <b>singleton</b> — AES-GCM is
/// stateless and thread-safe, so a single captured instance is safe for the EF value converters to close over.
/// </summary>
/// <remarks>
/// <para><b>Stored format:</b> <c>enc:v1:{keyId}:{base64(nonce ‖ ciphertext ‖ tag)}</c> — the <c>keyId</c> is
/// stored WITH the value so a retired key still decrypts as long as it stays in the ring (rotation overlap). The
/// <c>enc:v1:</c> prefix also makes an encrypted value self-describing, so <see cref="Decrypt"/> can tell a
/// genuine ciphertext apart from a legacy plaintext value during the data-migration window.</para>
/// <para><b>Key config:</b> <c>Encryption:ActiveKeyId</c> selects the key used for new writes;
/// <c>Encryption:Keys:{keyId}</c> holds each key as a base64-encoded 32-byte (256-bit) value. Dev supplies a
/// committed dev-only key via <c>appsettings.Development.json</c>; production supplies the real key via
/// environment/secret store (<c>Encryption__Keys__{keyId}</c>). If NO usable active key is configured the
/// constructor <b>throws</b> (fail-fast) — the app never silently stores plaintext.</para>
/// </remarks>
public sealed class AesGcmFieldEncryptor : IFieldEncryptor
{
    /// <summary>The prefix that marks a value as produced by this encryptor (format version 1).</summary>
    public const string Prefix = "enc:v1:";

    private const int NonceSize = 12; // AES-GCM standard 96-bit nonce
    private const int TagSize = 16;   // AES-GCM 128-bit authentication tag
    private const int KeySize = 32;   // AES-256

    private readonly string _activeKeyId;
    private readonly IReadOnlyDictionary<string, byte[]> _keyRing;

    public AesGcmFieldEncryptor(IConfiguration configuration)
    {
        var options = configuration.GetSection(FieldEncryptionOptions.SectionName).Get<FieldEncryptionOptions>()
            ?? new FieldEncryptionOptions();

        var ring = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (keyId, base64) in options.Keys)
        {
            if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(base64))
                continue;

            byte[] key;
            try
            {
                key = Convert.FromBase64String(base64);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"Encryption:Keys:{keyId} is not valid base64. Provide a base64-encoded 32-byte AES-256 key.", ex);
            }

            if (key.Length != KeySize)
                throw new InvalidOperationException(
                    $"Encryption:Keys:{keyId} decodes to {key.Length} bytes; a 32-byte (256-bit) key is required.");

            ring[keyId] = key;
        }

        if (string.IsNullOrWhiteSpace(options.ActiveKeyId) || !ring.ContainsKey(options.ActiveKeyId))
            throw new InvalidOperationException(
                "Field encryption is not configured: set Encryption:ActiveKeyId and a matching base64 32-byte "
                + "Encryption:Keys:{ActiveKeyId} (dev: appsettings.Development.json; prod: env/secret store). "
                + "The application fail-fasts rather than store sensitive fields as plaintext.");

        _activeKeyId = options.ActiveKeyId;
        _keyRing = ring;
    }

    public string? Encrypt(string? plaintext)
    {
        if (plaintext is null)
            return null;

        var key = _keyRing[_activeKeyId];
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce); // fresh random nonce per value → same input encrypts differently

        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plainBytes, cipher, tag);
        }

        // Layout: nonce ‖ ciphertext ‖ tag
        var blob = new byte[NonceSize + cipher.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(cipher, 0, blob, NonceSize, cipher.Length);
        Buffer.BlockCopy(tag, 0, blob, NonceSize + cipher.Length, TagSize);

        return $"{Prefix}{_activeKeyId}:{Convert.ToBase64String(blob)}";
    }

    public string? Decrypt(string? stored)
    {
        if (stored is null)
            return null;

        // LEGACY PLAINTEXT tolerance: a value not written by this encryptor (pre-encryption plain text / plain
        // numeric string) is returned verbatim so it still reads back during the data-migration window.
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            return stored;

        // enc:v1:{keyId}:{base64}. Split into exactly 4 segments so a ':' inside base64 (there isn't one, but be
        // strict) never mis-parses.
        var parts = stored.Split(':', 4);
        if (parts.Length != 4)
            throw new CryptographicException("Encrypted field value is malformed (expected enc:v1:{keyId}:{base64}).");

        var keyId = parts[2];
        if (!_keyRing.TryGetValue(keyId, out var key))
            throw new CryptographicException(
                $"No key '{keyId}' is present in the encryption key ring; cannot decrypt this field. "
                + "Keep retired keys in Encryption:Keys until all values written under them are re-encrypted.");

        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Encrypted field payload is not valid base64.", ex);
        }

        if (blob.Length < NonceSize + TagSize)
            throw new CryptographicException("Encrypted field payload is too short to contain a nonce and tag.");

        var cipherLength = blob.Length - NonceSize - TagSize;
        var nonce = new byte[NonceSize];
        var cipher = new byte[cipherLength];
        var tag = new byte[TagSize];
        Buffer.BlockCopy(blob, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(blob, NonceSize, cipher, 0, cipherLength);
        Buffer.BlockCopy(blob, NonceSize + cipherLength, tag, 0, TagSize);

        var plainBytes = new byte[cipherLength];
        using (var aes = new AesGcm(key, TagSize))
        {
            // A tampered ciphertext / wrong key throws AuthenticationTagMismatchException here. We let it bubble
            // (never swallow to a plaintext fallback) so a corrupted/forged value fails loudly.
            aes.Decrypt(nonce, cipher, tag, plainBytes);
        }

        return Encoding.UTF8.GetString(plainBytes);
    }
}

/// <summary>
/// Strongly-typed binding of the <c>Encryption</c> configuration section — the field-encryption key ring.
/// </summary>
public sealed class FieldEncryptionOptions
{
    public const string SectionName = "Encryption";

    /// <summary>The <c>keyId</c> used to encrypt NEW writes. Must be present in <see cref="Keys"/>.</summary>
    public string? ActiveKeyId { get; set; }

    /// <summary>The key ring: <c>keyId</c> ⇒ base64-encoded 32-byte AES-256 key. Retired keys stay here to decrypt.</summary>
    public Dictionary<string, string> Keys { get; set; } = new();
}
