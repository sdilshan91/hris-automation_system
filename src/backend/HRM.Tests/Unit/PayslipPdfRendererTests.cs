// ============================================================================
// US-PAY-004: Payslip PDF renderer + storage-path helper — unit tests (pure, no DB).
//
//   - Renderer produces a non-empty, valid PDF (%PDF header) from a full model (FR-1/FR-2/NFR-5).
//   - Renderer handles the YTD-enabled model (BR-4 column) without error.
//   - Renderer handles a minimal model (no department/designation/DOJ) — terminated/sparse employees (BR-6).
//   - PayslipStoragePath derives GUID-only paths and rejects traversal (NFR-6).
//   - Download file name follows BR-5 {EmployeeNo}_{PayMonth}_{PayYear}.pdf.
// ============================================================================

using System.Text;
using FluentAssertions;
using HRM.Application.Common.Payroll;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Services;

namespace HRM.Tests.Unit;

public sealed class PayslipPdfRendererTests
{
    private static PayslipDocumentModel FullModel(bool showYtd = false) => new()
    {
        CompanyName = "Acme Corp",
        CompanyAddress = "1 Industrial Way, Springfield",
        FooterDisclaimer = "This is a computer-generated document and does not require a signature.",
        PayMonth = 5,
        PayYear = 2026,
        EmployeeName = "Jane Doe",
        EmployeeNo = "EMP-0042",
        Department = "Engineering",
        Designation = "Senior Engineer",
        DateOfJoining = new DateTime(2020, 1, 15),
        Earnings = new[]
        {
            new PayslipLine("Basic", false, 50_000m, showYtd ? 250_000m : null),
            new PayslipLine("HRA", false, 20_000m, showYtd ? 100_000m : null),
        },
        Deductions = new[]
        {
            new PayslipLine("Provident Fund", true, 6_000m, showYtd ? 30_000m : null),
            new PayslipLine("Income Tax", false, 4_000m, showYtd ? 20_000m : null),
        },
        GrossEarnings = 70_000m,
        TotalDeductions = 10_000m,
        NetSalary = 60_000m,
        WorkingDays = 22m,
        PaidDays = 22m,
        LopDays = 0m,
        ShowYtd = showYtd,
    };

    private static bool IsPdf(byte[] bytes) =>
        bytes.Length > 4 && Encoding.ASCII.GetString(bytes, 0, 4) == "%PDF";

    [Fact]
    public void Render_FullModel_ProducesValidNonEmptyPdf()
    {
        var bytes = PayslipPdfRenderer.Render(FullModel());

        bytes.Should().NotBeNullOrEmpty();
        IsPdf(bytes).Should().BeTrue("the output must be a real PDF (NFR-5: static, no JS)");
    }

    [Fact]
    public void Render_WithYtdEnabled_ProducesValidPdf()
    {
        var bytes = PayslipPdfRenderer.Render(FullModel(showYtd: true));

        bytes.Should().NotBeNullOrEmpty();
        IsPdf(bytes).Should().BeTrue();
    }

    [Fact]
    public void Render_MinimalModel_NoDepartmentOrDesignation_StillRenders()
    {
        var model = FullModel() with
        {
            Department = null,
            Designation = null,
            DateOfJoining = null,
            Earnings = new[] { new PayslipLine("Basic", false, 30_000m, null) },
            Deductions = Array.Empty<PayslipLine>(),
            GrossEarnings = 30_000m,
            TotalDeductions = 0m,
            NetSalary = 30_000m,
        };

        var bytes = PayslipPdfRenderer.Render(model);

        bytes.Should().NotBeNullOrEmpty();
        IsPdf(bytes).Should().BeTrue();
    }

    [Fact]
    public void Render_WithTenantBrandColor_DoesNotThrow()
    {
        var model = FullModel() with { BrandPrimaryColor = "#1d4ed8" };

        var act = () => PayslipPdfRenderer.Render(model);

        act.Should().NotThrow();
    }
}

public sealed class PayslipStoragePathTests
{
    [Fact]
    public void ForSlip_DerivesGuidOnlyRelativePath()
    {
        var runId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var path = PayslipStoragePath.ForSlip(runId, employeeId);

        path.Should().Be($"payroll/{runId:D}/{employeeId:D}.pdf");
        path.Should().NotContain("..");
    }

    [Fact]
    [Trait("TC", "TC-PAY-004-14")]
    public void DownloadFileName_FollowsBr5Format()
    {
        // ISSUE-163: the pay month is zero-padded (EMP-0042_05_2026, not _5_).
        var name = PayslipStoragePath.DownloadFileName("EMP-0042", 5, 2026);

        name.Should().Be("EMP-0042_05_2026.pdf");
    }

    // ISSUE-163: the month is always two digits; two-digit months are unchanged.
    [Theory]
    [Trait("TC", "TC-PAY-004-14")]
    [InlineData(1, "EMP-0042_01_2026.pdf")]
    [InlineData(5, "EMP-0042_05_2026.pdf")]
    [InlineData(12, "EMP-0042_12_2026.pdf")]
    public void DownloadFileName_ZeroPadsMonthToTwoDigits(int month, string expected)
    {
        PayslipStoragePath.DownloadFileName("EMP-0042", month, 2026).Should().Be(expected);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("payroll/../../../secret.pdf")]
    [InlineData("/absolute/path.pdf")]
    public void AssertSafe_RejectsTraversalOrAbsolutePaths(string badPath)
    {
        var act = () => PayslipStoragePath.AssertSafe(badPath);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AssertSafe_AllowsGuidDerivedPath()
    {
        var act = () => PayslipStoragePath.AssertSafe(PayslipStoragePath.ForSlip(Guid.NewGuid(), Guid.NewGuid()));

        act.Should().NotThrow();
    }

    [Fact]
    public void DownloadFileName_SanitizesSeparatorsInEmployeeNo()
    {
        var name = PayslipStoragePath.DownloadFileName("../EMP/01", 3, 2026);

        name.Should().NotContain("/");
        name.Should().NotContain("..");
        name.Should().EndWith("_03_2026.pdf"); // ISSUE-163: zero-padded month
    }
}
