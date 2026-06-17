// ============================================================================
// US-RPT-004: HrReportRenderer unit tests.
//
// Locks the pure render logic that backs every report export:
//   - CSV (FR-2/AC-4): UTF-8 BOM, header row first, comma separator, RFC-4180 escaping of special chars.
//     Test Hint: a 100-employee headcount-style table → 1 header row + 100 data rows.
//   - XLSX (FR-3/AC-2/BR-6): bold header row + the "Filters Applied" metadata section + the report title.
//   - PDF  (FR-4/AC-3/BR-5): produces non-empty bytes with the QuestPDF/PDF signature (branding/title/footer).
//
// The renderer is a PURE function (no DB/tenant/filesystem), so these run with no fixture.
// ============================================================================

using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using HRM.Application.Features.Reports.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Services;

namespace HRM.Tests.Unit;

public sealed class HrReportRendererTests
{
    private static HrReportResult Report(
        IReadOnlyList<string> columns,
        IReadOnlyList<object?[]> rows,
        string title = "Headcount Summary",
        string type = "headcount",
        HrAppliedFilters? filters = null) =>
        new()
        {
            Metadata = new HrReportMetadata
            {
                Type = type,
                Title = title,
                GeneratedAt = new DateTime(2026, 6, 17, 10, 30, 0, DateTimeKind.Utc),
                AppliedFilters = filters ?? new HrAppliedFilters { DateFrom = "2026-06-01", DateTo = "2026-06-17" },
            },
            Table = new HrReportTable { Columns = columns, Rows = rows },
        };

    // ── CSV (FR-2 / AC-4) ────────────────────────────────────────────────────

    [Fact]
    public void Csv_StartsWith_Utf8Bom()
    {
        var bytes = HrReportRenderer.RenderCsv(Report(["Metric", "Value"], [new object?[] { "Total", 5 }]));

        bytes.Length.Should().BeGreaterThan(3);
        bytes[0].Should().Be(0xEF);
        bytes[1].Should().Be(0xBB);
        bytes[2].Should().Be(0xBF);
    }

    [Fact]
    public void Csv_HasHeaderRow_ThenOneRowPerEmployee_HundredEmployeeHint()
    {
        // Test Hint: a 100-employee headcount-style table.
        var rows = Enumerable.Range(1, 100)
            .Select(i => new object?[] { $"EMP{i:D3}", $"Employee {i}", "Engineering" })
            .Cast<object?[]>()
            .ToList();

        var bytes = HrReportRenderer.RenderCsv(Report(["Employee No", "Name", "Department"], rows));

        // Decode and strip the leading UTF-8 BOM (U+FEFF) so the assertions target the textual content.
        var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        // CsvHelper terminates the final record too, so splitting on newline yields a trailing empty entry.
        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

        lines.Should().HaveCount(101);                       // 1 header + 100 data rows
        lines[0].Should().Be("Employee No,Name,Department"); // header first
        lines[1].Should().StartWith("EMP001,");
        lines[100].Should().StartWith("EMP100,");
    }

    [Fact]
    public void Csv_Escapes_SpecialCharacters_PerRfc4180()
    {
        // A comma, a double-quote and a newline must all be quoted/escaped by CsvHelper.
        var rows = new List<object?[]>
        {
            new object?[] { "Smith, John", "He said \"hi\"", "line1\nline2" },
        };

        var bytes = HrReportRenderer.RenderCsv(Report(["Name", "Note", "Multi"], rows));
        var text = Encoding.UTF8.GetString(bytes);

        text.Should().Contain("\"Smith, John\"");        // comma → field is quoted
        text.Should().Contain("\"He said \"\"hi\"\"\""); // embedded quotes are doubled
        text.Should().Contain("\"line1\nline2\"");       // embedded newline → field is quoted
    }

    // ── XLSX (FR-3 / AC-2 / BR-6) ────────────────────────────────────────────

    [Fact]
    public void Xlsx_HasBoldHeaders_Title_AndFiltersAppliedSection()
    {
        var filters = new HrAppliedFilters
        {
            DateFrom = "2026-06-01",
            DateTo = "2026-06-17",
            EmploymentTypes = ["FullTime"],
        };
        var report = Report(["Metric", "Value"], [new object?[] { "Total Headcount", 42 }], filters: filters);

        var bytes = HrReportRenderer.RenderXlsx(report);
        bytes.Length.Should().BeGreaterThan(0);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();

        // Read the whole used range as text so we don't couple to exact row offsets.
        var allText = ws.CellsUsed().Select(c => c.GetString()).ToList();

        allText.Should().Contain("Headcount Summary");        // AC-2 title
        allText.Should().Contain("Filters Applied");          // BR-6 section heading
        allText.Should().Contain("Employment Types");         // a described filter label
        allText.Should().Contain("Metric");                   // the data-table header
        allText.Should().Contain("Total Headcount");          // a data cell

        // The data-table header cell must be bold (find the "Metric" header cell).
        var header = ws.CellsUsed().First(c => c.GetString() == "Metric");
        header.Style.Font.Bold.Should().BeTrue();
    }

    [Fact]
    public void Xlsx_KeepsNumericCells_AsNumbers()
    {
        var report = Report(["Metric", "Value"], [new object?[] { "Total Headcount", 42 }]);
        var bytes = HrReportRenderer.RenderXlsx(report);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();

        var valueCell = ws.CellsUsed().First(c => c.GetString() == "42");
        valueCell.DataType.Should().Be(XLDataType.Number);
    }

    // ── PDF (FR-4 / AC-3 / BR-5) ─────────────────────────────────────────────

    [Fact]
    public void Pdf_ProducesNonEmptyBytes_WithPdfSignature()
    {
        var report = Report(["Metric", "Value"], [new object?[] { "Total Headcount", 42 }]);

        var (content, fileName, contentType) = HrReportRenderer.Render(
            HrReportExportFormat.Pdf, report, tenantName: "Acme Corp");

        content.Length.Should().BeGreaterThan(0);
        // "%PDF" magic header.
        Encoding.ASCII.GetString(content, 0, 4).Should().Be("%PDF");
        fileName.Should().EndWith(".pdf");
        contentType.Should().Be("application/pdf");
    }

    [Fact]
    public void Render_PicksFileNameAndContentType_PerFormat()
    {
        var report = Report(["A"], [new object?[] { "x" }]);

        HrReportRenderer.Render(HrReportExportFormat.Csv, report).ContentType.Should().Be("text/csv");
        HrReportRenderer.Render(HrReportExportFormat.Xlsx, report).FileName.Should().EndWith(".xlsx");
        HrReportRenderer.Render(HrReportExportFormat.Xlsx, report).ContentType
            .Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }
}
