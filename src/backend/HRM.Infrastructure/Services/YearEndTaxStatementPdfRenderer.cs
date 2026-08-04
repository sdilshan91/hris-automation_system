using HRM.Application.Features.Payroll.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-PAY-009 AC-3: renders ONE employee's year-end tax statement as a PDF.
///
/// <para><b>Why this sits beside <see cref="PayslipPdfRenderer"/> rather than reusing
/// <c>PerformancePdfRenderer</c>.</b> The plan said to reuse the latter, but it is an <c>internal static</c>
/// class in the Performance namespace whose public surface is four DTO-specific methods — reusing it would mean
/// dragging a Payroll DTO into Performance or refactoring four shipped PDFs. A year-end statement is
/// structurally a multi-period payslip: same lane, same document shape, same branded header. Following the
/// payslip idiom is the smaller and more honest reuse.</para>
///
/// <para>This is a document an employee files with a tax authority, so it states the fiscal-year window and the
/// per-month breakdown explicitly rather than a single annual figure — the whole reason the columnar report
/// cannot satisfy AC-3.</para>
/// </summary>
public static class YearEndTaxStatementPdfRenderer
{
    public static byte[] Render(YearEndTaxStatementDto statement, string? brandPrimaryColor = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var accent = ParseColor(brandPrimaryColor);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Column(header =>
                {
                    header.Item().BorderBottom(2).BorderColor(accent).PaddingBottom(6).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(statement.TenantName).FontSize(18).Bold().FontColor(accent);
                        });
                        row.ConstantItem(200).AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text("YEAR-END TAX STATEMENT").FontSize(13).Bold();
                            c.Item().AlignRight().Text($"Fiscal Year: {statement.FiscalYear}").FontSize(9);
                            c.Item().AlignRight().Text($"Country: {statement.CountryCode}").FontSize(9);
                        });
                    });
                });

                page.Content().PaddingVertical(12).Column(content =>
                {
                    content.Item().PaddingBottom(10).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(statement.EmployeeName).FontSize(12).Bold();
                            c.Item().Text($"Employee No: {statement.EmployeeNo}").FontSize(9);
                            if (!string.IsNullOrWhiteSpace(statement.Designation))
                                c.Item().Text(statement.Designation!).FontSize(9).Light();
                        });
                    });

                    // The month-wise breakdown AC-3 requires. A single annual total is exactly what the
                    // columnar report already gives and what an employee cannot file with.
                    content.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2.2f); // Period
                            columns.RelativeColumn(1.6f); // Gross
                            columns.RelativeColumn(1.6f); // Taxable
                            columns.RelativeColumn(1.6f); // Deductions
                            columns.RelativeColumn(1.6f); // Tax withheld
                        });

                        table.Header(h =>
                        {
                            h.Cell().Background(accent).Padding(5).AlignLeft()
                                .Text("Period").FontSize(9).Bold().FontColor(Colors.White);
                            foreach (var heading in new[] { "Gross Earnings", "Taxable Income", "Deductions", "Tax Withheld" })
                                h.Cell().Background(accent).Padding(5).AlignRight()
                                    .Text(heading).FontSize(9).Bold().FontColor(Colors.White);
                        });

                        foreach (var line in statement.Lines)
                        {
                            BodyCell(table, line.PeriodLabel, left: true);
                            BodyCell(table, Money(line.GrossEarnings));
                            BodyCell(table, Money(line.TaxableIncome));
                            BodyCell(table, Money(line.TotalDeductions));
                            BodyCell(table, Money(line.IncomeTaxWithheld));
                        }

                        TotalCell(table, "TOTAL", accent, left: true);
                        TotalCell(table, Money(statement.TotalGrossEarnings), accent);
                        TotalCell(table, Money(statement.TotalTaxableIncome), accent);
                        TotalCell(table, Money(statement.TotalDeductions), accent);
                        TotalCell(table, Money(statement.TotalIncomeTaxWithheld), accent);
                    });

                    content.Item().PaddingTop(14).Text(
                            "This statement summarises finalized payroll for the fiscal year shown. Figures are "
                            + "derived from issued payslips.")
                        .FontSize(8).Light();
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Generated ").FontSize(8).Light();
                    t.Span($"{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").FontSize(8).Light();
                    t.Span("  •  Page ").FontSize(8).Light();
                    t.CurrentPageNumber().FontSize(8).Light();
                    t.Span(" of ").FontSize(8).Light();
                    t.TotalPages().FontSize(8).Light();
                });
            });
        });

        return document.GeneratePdf();
    }

    /// <summary>A stable, filesystem-safe name for one statement — also the entry name inside the bulk ZIP.</summary>
    public static string FileNameFor(YearEndTaxStatementDto statement)
    {
        var safeNo = new string(statement.EmployeeNo.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (safeNo.Length == 0)
            safeNo = statement.EmployeeId.ToString("N");

        return $"tax-statement-{safeNo}-{statement.FiscalYear}.pdf";
    }

    private static void BodyCell(TableDescriptor table, string text, bool left = false)
    {
        var cell = table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);
        (left ? cell.AlignLeft() : cell.AlignRight()).Text(text).FontSize(9);
    }

    private static void TotalCell(TableDescriptor table, string text, string accent, bool left = false)
    {
        var cell = table.Cell().BorderTop(2).BorderColor(accent).Padding(5);
        (left ? cell.AlignLeft() : cell.AlignRight()).Text(text).FontSize(9).Bold();
    }

    private static string Money(decimal value) => value.ToString("N2");

    /// <summary>Falls back to the platform indigo when the tenant has set no brand colour.</summary>
    private static string ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return "#4F46E5";

        var value = hex.Trim();
        if (!value.StartsWith('#'))
            value = "#" + value;

        return value.Length is 4 or 7 ? value : "#4F46E5";
    }
}
