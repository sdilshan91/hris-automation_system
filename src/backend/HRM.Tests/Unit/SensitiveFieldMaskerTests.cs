// ============================================================================
// US-ADM-008 (FR-4): SensitiveFieldMasker — pure masking of configured sensitive
// keys (password_hash, mfa_secret, bank_account_number, national_id + casing
// variants) recursively through nested JSON, replacing VALUES with ***REDACTED***.
// ============================================================================

using FluentAssertions;
using HRM.Application.Features.AuditLog;

namespace HRM.Tests.Unit;

public sealed class SensitiveFieldMaskerTests
{
    [Fact]
    public void Mask_TopLevelSensitiveKeys_AreRedacted()
    {
        const string json = """
        {"name":"Jane","password_hash":"abc123","bank_account_number":"9988776655","national_id":"NID-42"}
        """;

        var masked = SensitiveFieldMasker.Mask(json)!;

        masked.Should().Contain("\"name\":\"Jane\"");
        masked.Should().Contain($"\"password_hash\":\"{SensitiveFieldMasker.Redacted}\"");
        masked.Should().Contain($"\"bank_account_number\":\"{SensitiveFieldMasker.Redacted}\"");
        masked.Should().Contain($"\"national_id\":\"{SensitiveFieldMasker.Redacted}\"");
        masked.Should().NotContain("abc123");
        masked.Should().NotContain("9988776655");
        masked.Should().NotContain("NID-42");
    }

    [Theory]
    [InlineData("passwordHash")]
    [InlineData("PasswordHash")]
    [InlineData("nationalId")]
    [InlineData("NationalId")]
    [InlineData("bankAccountNumber")]
    [InlineData("mfaSecret")]
    [InlineData("salary")]        // BUG-281: at-rest coverage for compensation fields
    [InlineData("Salary")]
    public void Mask_CamelAndPascalVariants_AreRedacted(string key)
    {
        var json = $"{{\"{key}\":\"secret-value\"}}";

        var masked = SensitiveFieldMasker.Mask(json)!;

        masked.Should().Contain(SensitiveFieldMasker.Redacted);
        masked.Should().NotContain("secret-value");
    }

    [Fact]
    public void Mask_NestedObjectsAndArrays_AreRedactedRecursively()
    {
        const string json = """
        {
          "employee": {
            "displayName": "John",
            "mfa_secret": "TOTPSEED",
            "accounts": [
              {"bank_account_number": "111", "label": "primary"},
              {"bank_account_number": "222", "label": "secondary"}
            ]
          }
        }
        """;

        var masked = SensitiveFieldMasker.Mask(json)!;

        masked.Should().NotContain("TOTPSEED");
        masked.Should().NotContain("111");
        masked.Should().NotContain("222");
        masked.Should().Contain("\"label\":\"primary\"");
        masked.Should().Contain("\"displayName\":\"John\"");
    }

    [Fact]
    public void Mask_NonSensitiveJson_IsUnchangedInMeaning()
    {
        const string json = """{"name":"Jane","title":"Engineer"}""";

        var masked = SensitiveFieldMasker.Mask(json)!;

        masked.Should().Contain("Jane");
        masked.Should().Contain("Engineer");
        masked.Should().NotContain(SensitiveFieldMasker.Redacted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Mask_NullOrWhitespace_ReturnsInput(string? input)
    {
        SensitiveFieldMasker.Mask(input).Should().Be(input);
    }

    [Fact]
    public void Mask_NonJsonText_IsReturnedUnchanged()
    {
        const string plain = "User logged in from 10.0.0.1";

        SensitiveFieldMasker.Mask(plain).Should().Be(plain);
    }
}
