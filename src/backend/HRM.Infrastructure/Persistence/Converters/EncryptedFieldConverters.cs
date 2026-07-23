using System.Globalization;
using HRM.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HRM.Infrastructure.Persistence.Converters;

/// <summary>
/// Marker for every field-at-rest ENCRYPTION value converter (P3-4). PUBLIC so tests can walk the EF model
/// and identify encrypted properties by the converter's CLR type — the reverse-drift gate that asserts every
/// converter-carrying property is also listed in <c>Security.EncryptedFieldRegistry</c> (a property encrypted
/// here but missing there would never be re-encrypted on key rotation, and retiring the old key would destroy
/// its data).
/// </summary>
public interface IEncryptedValueConverter;

/// <summary>
/// EF Core value converters that transparently encrypt/decrypt a property through an <see cref="IFieldEncryptor"/>
/// (P3-4 field-at-rest encryption). The encryptor instance is captured in the converter closure; because the
/// production encryptor is a singleton (and every test build uses one stable key) the captured instance is stable
/// for the lifetime of the compiled model. <see cref="AppDbContext"/> keys its model cache by encryptor type so a
/// no-op-encryptor model and a real-encryptor model never collide. Each converter is a NAMED class implementing
/// <see cref="IEncryptedValueConverter"/> (not an anonymous <c>ValueConverter</c> instance) so the encryption
/// converters are mechanically identifiable in the model — see the marker's remarks.
/// </summary>
internal static class EncryptedFieldConverters
{
    /// <summary>A REQUIRED (non-null) <c>string</c> column encrypted at rest (stored as <c>text</c>).</summary>
    public static ValueConverter<string, string?> RequiredString(IFieldEncryptor encryptor) =>
        new EncryptedRequiredStringConverter(encryptor);

    /// <summary>A nullable <c>string?</c> column encrypted at rest (stored as <c>text</c>).</summary>
    public static ValueConverter<string?, string?> NullableString(IFieldEncryptor encryptor) =>
        new EncryptedNullableStringConverter(encryptor);

    /// <summary>
    /// A <c>decimal?</c> column encrypted at rest: the value is formatted with the invariant culture, encrypted,
    /// and stored as <c>text</c>; on read it is decrypted and parsed back with the invariant culture. A legacy
    /// plain numeric string (e.g. after the numeric→text migration, before back-fill) decrypts verbatim and parses.
    /// </summary>
    public static ValueConverter<decimal?, string?> Decimal(IFieldEncryptor encryptor) =>
        new EncryptedDecimalConverter(encryptor);

    private sealed class EncryptedRequiredStringConverter
        : ValueConverter<string, string?>, IEncryptedValueConverter
    {
        public EncryptedRequiredStringConverter(IFieldEncryptor encryptor)
            : base(
                plaintext => encryptor.Encrypt(plaintext),
                stored => encryptor.Decrypt(stored)!)
        {
        }
    }

    private sealed class EncryptedNullableStringConverter
        : ValueConverter<string?, string?>, IEncryptedValueConverter
    {
        public EncryptedNullableStringConverter(IFieldEncryptor encryptor)
            : base(
                plaintext => encryptor.Encrypt(plaintext),
                stored => encryptor.Decrypt(stored))
        {
        }
    }

    private sealed class EncryptedDecimalConverter
        : ValueConverter<decimal?, string?>, IEncryptedValueConverter
    {
        public EncryptedDecimalConverter(IFieldEncryptor encryptor)
            : base(
                value => value == null
                    ? null
                    : encryptor.Encrypt(value.Value.ToString(CultureInfo.InvariantCulture)),
                stored => string.IsNullOrEmpty(stored)
                    ? (decimal?)null
                    : decimal.Parse(encryptor.Decrypt(stored)!, NumberStyles.Number, CultureInfo.InvariantCulture))
        {
        }
    }
}
