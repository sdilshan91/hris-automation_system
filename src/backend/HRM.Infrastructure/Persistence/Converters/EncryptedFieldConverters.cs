using System.Globalization;
using HRM.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HRM.Infrastructure.Persistence.Converters;

/// <summary>
/// EF Core value converters that transparently encrypt/decrypt a property through an <see cref="IFieldEncryptor"/>
/// (P3-4 field-at-rest encryption). The encryptor instance is captured in the converter closure; because the
/// production encryptor is a singleton (and every test build uses one stable key) the captured instance is stable
/// for the lifetime of the compiled model. <see cref="AppDbContext"/> keys its model cache by encryptor type so a
/// no-op-encryptor model and a real-encryptor model never collide.
/// </summary>
internal static class EncryptedFieldConverters
{
    /// <summary>A REQUIRED (non-null) <c>string</c> column encrypted at rest (stored as <c>text</c>).</summary>
    public static ValueConverter<string, string?> RequiredString(IFieldEncryptor encryptor) =>
        new(
            plaintext => encryptor.Encrypt(plaintext),
            stored => encryptor.Decrypt(stored)!);

    /// <summary>A nullable <c>string?</c> column encrypted at rest (stored as <c>text</c>).</summary>
    public static ValueConverter<string?, string?> NullableString(IFieldEncryptor encryptor) =>
        new(
            plaintext => encryptor.Encrypt(plaintext),
            stored => encryptor.Decrypt(stored));

    /// <summary>
    /// A <c>decimal?</c> column encrypted at rest: the value is formatted with the invariant culture, encrypted,
    /// and stored as <c>text</c>; on read it is decrypted and parsed back with the invariant culture. A legacy
    /// plain numeric string (e.g. after the numeric→text migration, before back-fill) decrypts verbatim and parses.
    /// </summary>
    public static ValueConverter<decimal?, string?> Decimal(IFieldEncryptor encryptor) =>
        new(
            value => value == null
                ? null
                : encryptor.Encrypt(value.Value.ToString(CultureInfo.InvariantCulture)),
            stored => string.IsNullOrEmpty(stored)
                ? (decimal?)null
                : decimal.Parse(encryptor.Decrypt(stored)!, NumberStyles.Number, CultureInfo.InvariantCulture));
}
