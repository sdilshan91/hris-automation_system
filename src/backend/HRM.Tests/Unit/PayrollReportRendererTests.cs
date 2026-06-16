// ============================================================================
// US-PAY-009: Payroll report renderer + account-masking helper — unit tests (pure, no DB).
//
//   - CSV export produces a non-empty file whose first line is the header row (FR-2/AC-4).
//   - Excel (ClosedXML) export produces a non-empty .xlsx whose header cells match the columns (AC-4).
//   - PDF (QuestPDF) export produces a non-empty, valid PDF (%PDF header) (FR-2).
//   - The TotalRow is rendered into CSV/Excel.
//   - PayrollReportService.MaskAccount shows only the last 4 digits (BR-2).
// ============================================================================

using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Infrastructure.Services;

namespace HRM.Tests.Unit;

public sealed class PayrollReportRendererTests
{
    private static PayrollReportResult SampleReport() => new()
    {
        ReportType = PayrollReportType.PayrollSummary.ToString(),
        Title = "Payroll Summary — May 2026",
        PayMonth = 5,
        PayYear = 2026,
        Columns = ["Department", "Employee Count", "Total Gross", "Total Net"],
        Rows =
        [
            new PayrollReportRow { Cells = ["Engineering", "2", "80000.00", "70000.00"] },
            new PayrollReportRow { Cells = ["Sales", "1", "40000.00", "35000.00"] },
        ],
        TotalRow = new PayrollReportRow { Cells = ["TOTAL", "3", "120000.00", "105000.00"] },
        TotalCount = 2,
    };

    [Fact]
    public void Csv_FirstLine_IsHeaderRow_AndIncludesTotal()
    {
        var (content, fileName, contentType) = PayrollReportRenderer.Render(PayrollExportFormat.Csv, SampleReport());

        content.Should().NotBeEmpty();
        fileName.Should().EndWith(".csv");
        contentType.Should().Be("text/csv");

        var text = Encoding.UTF8.GetString(content);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Contain("Department").And.Contain("Total Net");
        text.Should().Contain("Engineering").And.Contain("TOTAL");
    }

    [Fact]
    public void Xlsx_HeaderCells_MatchColumns()
    {
        var report = SampleReport();
        var (content, fileName, contentType) = PayrollReportRenderer.Render(PayrollExportFormat.Xlsx, report);

        content.Should().NotBeEmpty();
        fileName.Should().EndWith(".xlsx");
        contentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        using var workbook = new XLWorkbook(new MemoryStream(content));
        var ws = workbook.Worksheets.First();
        for (int c = 0; c < report.Columns.Count; c++)
            ws.Cell(1, c + 1).GetString().Should().Be(report.Columns[c]);

        // First data row + a total row are present.
        ws.Cell(2, 1).GetString().Should().Be("Engineering");
        ws.Cell(4, 1).GetString().Should().Be("TOTAL");
    }

    [Fact]
    public void Pdf_Produces_ValidPdfHeader()
    {
        var (content, fileName, contentType) = PayrollReportRenderer.Render(PayrollExportFormat.Pdf, SampleReport());

        content.Should().NotBeEmpty();
        fileName.Should().EndWith(".pdf");
        contentType.Should().Be("application/pdf");
        Encoding.ASCII.GetString(content, 0, 4).Should().Be("%PDF");
    }

    [Theory]
    [InlineData("1234567890", "******7890")]
    [InlineData("9876", "****")]
    [InlineData("12", "**")]
    [InlineData("", "")]
    public void MaskAccount_ShowsOnlyLastFour(string input, string expected)
    {
        PayrollReportService.MaskAccount(input).Should().Be(expected);
    }
}
