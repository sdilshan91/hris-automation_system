using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using HRM.Application.Common.Helpers;
using HRM.Application.Features.Payroll.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Renders a generic columnar <see cref="PayrollReportResult"/> into CSV / Excel (ClosedXML) / PDF
/// (QuestPDF) bytes (US-PAY-009 FR-2, AC-4). A PURE function — no DB, no tenant context, no filesystem —
/// so it returns a <c>byte[]</c> and is fully unit-testable.
///
/// <para>REUSE: the CSV (CsvHelper) and Excel (ClosedXML) paths mirror the US-LV-012 LeaveReportService
/// renderer; PDF reuses the QuestPDF setup established by US-PAY-004 PayslipPdfRenderer. No new export
/// infra is introduced here.</para>
/// </summary>
public static class PayrollReportRenderer
{
    public static (byte[] Content, string FileName, string ContentType) Render(
        PayrollExportFormat format, PayrollReportResult report)
    {
        string baseName = $"payroll-{ToKebab(report.ReportType)}-{report.PayYear:D4}-{report.PayMonth:D2}";

        return format switch
        {
            PayrollExportFormat.Xlsx => (
                RenderXlsx(report),
                $"{baseName}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            PayrollExportFormat.Pdf => (
                RenderPdf(report),
                $"{baseName}.pdf",
                "application/pdf"),
            _ => (
                RenderCsv(report),
                $"{baseName}.csv",
                "text/csv"),
        };
    }

    // ── CSV (CsvHelper, UTF-8 BOM — same approach as the leave-report renderer) ──
    // ISSUE-198: prepend the UTF-8 BOM so Excel auto-detects UTF-8 (matches the HR + leave CSV writers).
    public static byte[] RenderCsv(PayrollReportResult report)
    {
        using var stream = new MemoryStream();
        CsvExport.WriteBom(stream);
        using (var writer = new StreamWriter(stream, CsvExport.NoBomEncoding, leaveOpen: true))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            foreach (var header in report.Columns)
                csv.WriteField(header);
            csv.NextRecord();

            foreach (var row in report.Rows)
            {
                foreach (var cell in row.Cells)
                    csv.WriteField(cell);
                csv.NextRecord();
            }

            if (report.TotalRow is { } total)
            {
                foreach (var cell in total.Cells)
                    csv.WriteField(cell);
                csv.NextRecord();
            }
        }
        return stream.ToArray();
    }

    // ── Excel (ClosedXML — same approach as the leave-report renderer) ────────
    public static byte[] RenderXlsx(PayrollReportResult report)
    {
        using var workbook = new XLWorkbook();
        var sheetName = report.ReportType;
        if (sheetName.Length > 31) sheetName = sheetName[..31]; // Excel sheet-name limit
        var ws = workbook.Worksheets.Add(sheetName);

        for (int c = 0; c < report.Columns.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = report.Columns[c];
            cell.Style.Font.Bold = true;
        }

        int r = 0;
        for (; r < report.Rows.Count; r++)
        {
            var cells = report.Rows[r].Cells;
            for (int c = 0; c < cells.Count; c++)
                ws.Cell(r + 2, c + 1).Value = cells[c];
        }

        if (report.TotalRow is { } total)
        {
            for (int c = 0; c < total.Cells.Count; c++)
            {
                var cell = ws.Cell(r + 2, c + 1);
                cell.Value = total.Cells[c];
                cell.Style.Font.Bold = true;
            }
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ── PDF (QuestPDF — same license + A4 setup as PayslipPdfRenderer) ────────
    public static byte[] RenderPdf(PayrollReportResult report)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var accent = Colors.Blue.Darken2;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontSize(9));

                page.Header().BorderBottom(2).BorderColor(accent).PaddingBottom(6).Column(h =>
                {
                    h.Item().Text(report.Title).FontSize(15).Bold().FontColor(accent);
                    h.Item().Text($"Generated {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC").FontSize(8).Light();
                });

                page.Content().PaddingVertical(10).Column(content =>
                {
                    if (report.Columns.Count == 0)
                    {
                        content.Item().Text("No data for the selected period.").Italic();
                        return;
                    }

                    content.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            for (int i = 0; i < report.Columns.Count; i++)
                                cols.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            foreach (var col in report.Columns)
                                header.Cell().Element(HeaderCell).Text(col).SemiBold();
                        });

                        foreach (var row in report.Rows)
                            foreach (var cell in PadCells(row.Cells, report.Columns.Count))
                                table.Cell().Element(BodyCell).Text(cell);

                        if (report.TotalRow is { } total)
                            foreach (var cell in PadCells(total.Cells, report.Columns.Count))
                                table.Cell().Element(TotalCell).Text(cell).Bold();
                    });

                    if (!string.IsNullOrWhiteSpace(report.Note))
                        content.Item().PaddingTop(8).Text(report.Note!).FontSize(8).Italic().Light();
                });

                page.Footer().AlignRight().Text(t =>
                {
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        });

        return document.GeneratePdf();

        static IContainer HeaderCell(IContainer c) =>
            c.Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingVertical(3).PaddingHorizontal(4);
        static IContainer BodyCell(IContainer c) =>
            c.Border(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(4);
        static IContainer TotalCell(IContainer c) =>
            c.Background(Colors.Grey.Lighten4).BorderTop(1).BorderColor(Colors.Grey.Medium).PaddingVertical(3).PaddingHorizontal(4);
    }

    private static IEnumerable<string> PadCells(IReadOnlyList<string> cells, int width)
    {
        for (int i = 0; i < width; i++)
            yield return i < cells.Count ? cells[i] : string.Empty;
    }

    private static string ToKebab(string pascal)
    {
        var chars = new List<char>();
        for (int i = 0; i < pascal.Length; i++)
        {
            char c = pascal[i];
            if (char.IsUpper(c) && i > 0) chars.Add('-');
            chars.Add(char.ToLowerInvariant(c));
        }
        return new string(chars.ToArray());
    }
}
