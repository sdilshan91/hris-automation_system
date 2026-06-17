// ============================================================================
// US-ADM-010 (FR-8/BR-7 — SECURITY-CRITICAL): the export auth-secret deny-list.
// Auth secrets (password/MFA/token hashes) must NEVER be written to an export;
// PII (national id, bank account) MUST be exportable (narrower than the audit
// masker). Pure unit tests over the normalized substring matcher.
// ============================================================================

using FluentAssertions;
using HRM.Application.Features.DataExport;

namespace HRM.Tests.Unit;

public sealed class ExportSensitiveFieldsTests
{
    [Theory]
    [InlineData("PasswordHash")]
    [InlineData("password_hash")]
    [InlineData("MfaSecret")]
    [InlineData("TotpSecret")]
    [InlineData("MfaRecoveryCodes")]
    [InlineData("RefreshTokenHash")]
    [InlineData("TokenHash")]
    [InlineData("SecurityStamp")]
    public void AuthSecretProperties_AreDenied(string property)
        => ExportSensitiveFields.IsDenied(property).Should().BeTrue();

    [Theory]
    [InlineData("Email")]
    [InlineData("DisplayName")]
    [InlineData("NationalId")]          // PII — must be EXPORTABLE (FR-8)
    [InlineData("BankAccountNumber")]   // PII — must be EXPORTABLE (FR-8)
    [InlineData("Salary")]
    [InlineData("Id")]
    public void NonSecretAndPiiProperties_AreNotDenied(string property)
        => ExportSensitiveFields.IsDenied(property).Should().BeFalse();
}
