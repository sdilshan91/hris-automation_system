using System.Globalization;
using HRM.Domain.Payroll;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Renders an employee payslip into a real PDF via QuestPDF (US-PAY-004 FR-1/FR-2). A PURE function of the
/// denormalized <see cref="PayslipDocumentModel"/> — no DB, no tenant context, no filesystem — so it returns
/// a <c>byte[]</c> and is fully unit-testable (render-to-bytes, assert non-empty / parse text).
///
/// <para>Layout (§7 "Payslip PDF Content Structure"): header (company name/address + period), employee
/// section (name, no, department, designation, DOJ), earnings table, deductions table (statutory itemized),
/// summary (gross / total deductions / net), footer (working/paid/LOP days + BR-3 disclaimer). The optional
/// YTD column (BR-4) is rendered only when <see cref="PayslipDocumentModel.ShowYtd"/> is set.</para>
///
/// <para>NFR-5: QuestPDF emits a static PDF with no JavaScript/executable content. The QuestPDF Community
/// license is set idempotently here, matching <see cref="OfferPdfRenderer"/> / AttendanceSummaryService.</para>
/// </summary>
public static class PayslipPdfRenderer
{
    private static readonly string[] MonthNames =
    {
        "", "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    };

    public static byte[] Render(PayslipDocumentModel model)
    {
        // QuestPDF Community license (free for our use). Setting it is idempotent.
        QuestPDF.Settings.License = LicenseType.Community;

        var accent = ParseColor(model.BrandPrimaryColor);
        var period = $"{MonthName(model.PayMonth)} {model.PayYear}";

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
                        // ISSUE-158: tenant logo (when resolved). Bounded to a 110x48 box, aspect preserved.
                        if (model.CompanyLogoBytes is { Length: > 0 } logo)
                        {
                            row.ConstantItem(110).AlignMiddle().Height(48).Image(logo).FitArea();
                            row.ConstantItem(12);
                        }
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(model.CompanyName).FontSize(18).Bold().FontColor(accent);
                            if (!string.IsNullOrWhiteSpace(model.CompanyAddress))
                                c.Item().Text(model.CompanyAddress!).FontSize(9).Light();
                        });
                        row.ConstantItem(180).AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text("PAYSLIP").FontSize(14).Bold();
                            c.Item().AlignRight().Text($"Pay Period: {period}").FontSize(9);
                        });
                    });
                });

                page.Content().PaddingVertical(12).Column(content =>
                {
                    content.Spacing(12);

                    // ── Employee section ─────────────────────────────────────────
                    content.Item().Background(Colors.Grey.Lighten4).Padding(8).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(t => { t.Span("Employee: ").SemiBold(); t.Span(model.EmployeeName); });
                            c.Item().Text(t => { t.Span("Employee No: ").SemiBold(); t.Span(model.EmployeeNo); });
                            c.Item().Text(t => { t.Span("Department: ").SemiBold(); t.Span(model.Department ?? "-"); });
                            // FR-2/ISSUE-161: masked (last-4) bank account — full number is never rendered.
                            c.Item().Text(t => { t.Span("Bank A/C: ").SemiBold(); t.Span(model.MaskedBankAccount ?? "-"); });
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(t => { t.Span("Designation: ").SemiBold(); t.Span(model.Designation ?? "-"); });
                            c.Item().Text(t =>
                            {
                                t.Span("Date of Joining: ").SemiBold();
                                t.Span(model.DateOfJoining?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "-");
                            });
                        });
                    });

                    // ── Earnings + deductions tables (side by side) ──────────────
                    content.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Earnings").SemiBold().FontColor(accent);
                            c.Item().Element(e => ComponentTable(e, model.Earnings, model.ShowYtd, accent, "Gross Earnings", model.GrossEarnings));
                        });
                        row.ConstantItem(14);
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Deductions").SemiBold().FontColor(accent);
                            c.Item().Element(e => ComponentTable(e, model.Deductions, model.ShowYtd, accent, "Total Deductions", model.TotalDeductions));
                        });
                    });

                    // ── Net pay summary ──────────────────────────────────────────
                    content.Item().Background(accent).Padding(8).Row(row =>
                    {
                        row.RelativeItem().AlignLeft().Text("NET PAY").FontColor(Colors.White).Bold().FontSize(12);
                        row.RelativeItem().AlignRight().Text(Money(model.NetSalary)).FontColor(Colors.White).Bold().FontSize(12);
                    });

                    // ── Days line ────────────────────────────────────────────────
                    content.Item().Text(t =>
                    {
                        t.Span("Working Days: ").SemiBold(); t.Span($"{Days(model.WorkingDays)}    ");
                        t.Span("Paid Days: ").SemiBold(); t.Span($"{Days(model.PaidDays)}    ");
                        t.Span("LOP Days: ").SemiBold(); t.Span(Days(model.LopDays));
                    });
                });

                page.Footer().BorderTop(1).BorderColor(Colors.Grey.Lighten2).PaddingTop(6).Column(footer =>
                {
                    footer.Item().Text(model.FooterDisclaimer).FontSize(8).Light().Italic();
                });
            });
        });

        return document.GeneratePdf();
    }

    /// <summary>Renders one component table (rows + a bold total row), optionally with a YTD column (BR-4).</summary>
    private static void ComponentTable(
        IContainer container, IReadOnlyList<PayslipLine> lines, bool showYtd, string accentColor,
        string totalLabel, decimal total)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(3);
                cols.RelativeColumn(2);
                if (showYtd) cols.RelativeColumn(2);
            });

            table.Header(h =>
            {
                h.Cell().Element(HeaderCell).Text("Component").SemiBold();
                h.Cell().Element(HeaderCell).AlignRight().Text("Amount").SemiBold();
                if (showYtd) h.Cell().Element(HeaderCell).AlignRight().Text("YTD").SemiBold();
            });

            foreach (var line in lines)
            {
                var label = line.IsStatutory ? $"{line.ComponentName} (Statutory)" : line.ComponentName;
                table.Cell().Element(BodyCell).Text(label);
                table.Cell().Element(BodyCell).AlignRight().Text(Money(line.Amount));
                if (showYtd) table.Cell().Element(BodyCell).AlignRight().Text(line.YtdAmount.HasValue ? Money(line.YtdAmount.Value) : "-");
            }

            table.Cell().Element(TotalCell).Text(totalLabel).Bold();
            table.Cell().Element(TotalCell).AlignRight().Text(Money(total)).Bold();
            if (showYtd) table.Cell().Element(TotalCell).Text("");
        });

        static IContainer HeaderCell(IContainer c) =>
            c.Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(4);
        static IContainer BodyCell(IContainer c) =>
            c.BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(4);
        static IContainer TotalCell(IContainer c) =>
            c.BorderTop(1).BorderColor(Colors.Grey.Medium).PaddingVertical(3).PaddingHorizontal(4);
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
    private static string Days(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string MonthName(int month) => month is >= 1 and <= 12 ? MonthNames[month] : month.ToString();

    /// <summary>Parses a tenant brand hex colour into a QuestPDF colour, falling back to a neutral default.</summary>
    private static string ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Colors.Blue.Darken2;
        var trimmed = hex.Trim();
        if (!trimmed.StartsWith('#')) trimmed = "#" + trimmed;
        // QuestPDF accepts #RRGGBB; validate length/format, else fall back.
        return trimmed.Length is 7 or 9 ? trimmed : Colors.Blue.Darken2;
    }
}
