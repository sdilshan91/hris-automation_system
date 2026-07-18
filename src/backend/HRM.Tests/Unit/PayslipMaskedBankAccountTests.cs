// ============================================================================
// US-PAY-004 / ISSUE-161 — the payslip employee section must carry the employee's bank account
// MASKED to the last 4 digits (FR-2). Asserts the employee→model mapping (PayslipBatchRenderer.BuildModel)
// masks the account, never leaks the full number, and degrades safely for a null/short account.
// Also covers the shared AccountMasking helper the report + payslip surfaces both use.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Helpers;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Services;

namespace HRM.Tests.Unit;

[Trait("TC", "TC-PAY-004-11")]
public sealed class PayslipMaskedBankAccountTests
{
    private static readonly PayslipBranding Branding = new() { CompanyName = "Acme Corp" };

    private static PayslipSlipSnapshot Slip() => new(
        EmployeeId: Guid.NewGuid(),
        PayMonth: 5, PayYear: 2026,
        GrossEarnings: 50_000m, TotalDeductions: 8_000m, NetSalary: 42_000m,
        WorkingDays: 22m, PaidDays: 22m, LopDays: 0m);

    private static PayslipEmployeeSnapshot Employee(string? bankAccount) => new(
        FirstName: "Jane", LastName: "Doe", EmployeeNo: "EMP-0042",
        DepartmentId: Guid.NewGuid(), JobTitleId: Guid.NewGuid(),
        DateOfJoining: new DateTime(2020, 1, 15),
        BankAccountNumber: bankAccount);

    private static PayslipDocumentModel Build(string? bankAccount) => PayslipBatchRenderer.BuildModel(
        Slip(),
        Employee(bankAccount),
        details: Array.Empty<PayslipDetailSnapshot>(),
        Branding,
        departments: new Dictionary<Guid, string>(),
        jobTitles: new Dictionary<Guid, string>(),
        showYtd: false,
        earningYtd: null,
        deductionYtd: null);

    // ── FR-2/ISSUE-161: the model carries the MASKED account (last 4 visible, rest hidden) ──

    [Fact]
    public void BuildModel_MasksBankAccount_LastFourVisible_RestHidden()
    {
        var model = Build("1234567890");

        model.MaskedBankAccount.Should().Be("******7890");
        model.MaskedBankAccount.Should().EndWith("7890");
        model.MaskedBankAccount.Should().NotContain("123456", "the full account number must never appear");
    }

    // ── Safe handling: no account on file → null (renders as "-") ─────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildModel_NoBankAccount_IsNull(string? account)
    {
        Build(account).MaskedBankAccount.Should().BeNull();
    }

    // ── Safe handling: short account is fully masked (never leaked in the clear) ──────

    [Fact]
    public void BuildModel_ShortAccount_IsFullyMasked()
    {
        var model = Build("99");

        model.MaskedBankAccount.Should().Be("**");
        model.MaskedBankAccount.Should().NotContain("9");
    }

    // ── The rendered PDF still produces a valid document with the masked line present ──

    [Fact]
    public void Render_ModelWithMaskedBankAccount_ProducesValidPdf()
    {
        var model = Build("1234567890");

        var bytes = PayslipPdfRenderer.Render(model);

        bytes.Should().NotBeNullOrEmpty();
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    // ── The shared helper both surfaces (reports + payslips) use ──────────────────────

    [Theory]
    [InlineData("1234567890", "******7890")]
    [InlineData("1234", "****")]
    [InlineData("5", "*")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void AccountMasking_MaskLast4(string? input, string expected)
    {
        AccountMasking.MaskLast4(input).Should().Be(expected);
    }
}
