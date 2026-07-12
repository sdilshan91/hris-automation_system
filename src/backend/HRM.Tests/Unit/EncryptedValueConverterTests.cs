// ============================================================================
// P3-4: EF value converters for field-at-rest encryption — unit tests.
//
// Proves the AT-REST form directly at the converter boundary (independent of any DB provider): the provider value
// EF would store is `enc:v1:` ciphertext (NOT the plaintext), and it round-trips back to the original CLR value.
// ============================================================================

using System.Security.Cryptography;
using FluentAssertions;
using HRM.Infrastructure.Persistence.Converters;
using HRM.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace HRM.Tests.Unit;

[Trait("TC", "TC-PLT-P34")]
[Trait("Category", "FieldEncryption")]
public sealed class EncryptedValueConverterTests
{
    private static AesGcmFieldEncryptor Encryptor()
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

    [Fact]
    public void String_converter_stores_ciphertext_at_rest_and_reads_back_plaintext()
    {
        var converter = EncryptedFieldConverters.RequiredString(Encryptor());
        const string plaintext = "sensitive PIP escalation notes";

        var stored = (string?)converter.ConvertToProvider(plaintext);
        stored.Should().StartWith("enc:v1:");
        stored.Should().NotContain("escalation");

        var readBack = (string?)converter.ConvertFromProvider(stored);
        readBack.Should().Be(plaintext);
    }

    [Fact]
    public void String_converter_maps_null_to_null()
    {
        var converter = EncryptedFieldConverters.NullableString(Encryptor());
        converter.ConvertToProvider(null).Should().BeNull();
        converter.ConvertFromProvider(null).Should().BeNull();
    }

    [Fact]
    public void Decimal_converter_stores_ciphertext_at_rest_and_reads_back_the_decimal()
    {
        var converter = EncryptedFieldConverters.Decimal(Encryptor());
        decimal? amount = 125000.75m;

        var stored = (string?)converter.ConvertToProvider(amount);
        stored.Should().StartWith("enc:v1:");
        stored.Should().NotContain("125000");

        var readBack = (decimal?)converter.ConvertFromProvider(stored);
        readBack.Should().Be(125000.75m);
    }

    [Fact]
    public void Decimal_converter_maps_null_to_null()
    {
        var converter = EncryptedFieldConverters.Decimal(Encryptor());
        converter.ConvertToProvider((decimal?)null).Should().BeNull();
        converter.ConvertFromProvider(null).Should().BeNull();
    }

    [Fact]
    public void Decimal_converter_reads_a_legacy_plain_numeric_string_verbatim()
    {
        // After the numeric→text migration but BEFORE the back-fill, a value is a plain invariant numeric string.
        var converter = EncryptedFieldConverters.Decimal(Encryptor());
        var readBack = (decimal?)converter.ConvertFromProvider("5000.00");
        readBack.Should().Be(5000.00m);
    }
}
