using HRM.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Renders the substituted offer-letter into a real multi-page PDF via QuestPDF (US-REC-007 FR-2/NFR-1).
/// QuestPDF + the Community license are already an established dependency (US-ATT-007); a real PDF (rather
/// than a minimal stub) is therefore low-risk here. The layout is page 1 = letter body (greeting, position,
/// compensation, timeline) and page 2 = terms (probation, benefits, custom clauses, acceptance line) —
/// a standard 2-page offer letter.
///
/// DEFERRED: tenant branding (logo/colours, §10) and PDF encryption-at-rest (NFR-3).
/// </summary>
public static class OfferPdfRenderer
{
    public static byte[] Render(Offer offer, IReadOnlyDictionary<string, string> v)
    {
        // QuestPDF Community license (free for our use). Setting it is idempotent (see AttendanceSummaryService).
        QuestPDF.Settings.License = LicenseType.Community;

        string Var(string key) => v.TryGetValue(key, out var val) ? val : string.Empty;

        var companyName = Var("company_name");
        var applicantName = Var("applicant_name");
        var position = Var("position");
        var salaryLine = $"{Var("currency")} {Var("salary")} ({Var("salary_frequency")})";

        var document = Document.Create(container =>
        {
            // ── Page 1: letter body ───────────────────────────────────
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(t => t.FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Text(companyName).FontSize(18).Bold();
                    col.Item().Text("Letter of Offer").FontSize(13).SemiBold();
                    col.Item().Text($"Reference: {Var("reference_number")}").FontSize(9).Light();
                });

                page.Content().PaddingVertical(15).Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Text($"Dear {applicantName},");

                    col.Item().Text(
                        $"We are pleased to offer you the position of {position} at {companyName}. " +
                        "Please review the details of this offer below.");

                    col.Item().PaddingTop(5).Text("Position Details").Bold().FontSize(12);
                    col.Item().Text($"Position: {position}");
                    col.Item().Text($"Department: {Var("department")}");

                    col.Item().PaddingTop(5).Text("Compensation").Bold().FontSize(12);
                    col.Item().Text($"Salary: {salaryLine}");
                    col.Item().Text($"Benefits: {Var("benefits")}");

                    col.Item().PaddingTop(5).Text("Timeline").Bold().FontSize(12);
                    col.Item().Text($"Proposed start date: {Var("start_date")}");
                    col.Item().Text($"This offer is valid until: {Var("expiry_date")}");
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });

            // ── Page 2: terms + acceptance ────────────────────────────
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(t => t.FontSize(11));

                page.Header().Text("Terms & Conditions").FontSize(13).SemiBold();

                page.Content().PaddingVertical(15).Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Text($"Probation period: {Var("probation")}");

                    var customClauses = Var("custom_clauses");
                    if (!string.IsNullOrWhiteSpace(customClauses))
                    {
                        col.Item().PaddingTop(5).Text("Additional Clauses").Bold().FontSize(12);
                        col.Item().Text(customClauses);
                    }

                    col.Item().PaddingTop(15).Text(
                        "To accept this offer, please confirm your acceptance on or before the validity date " +
                        "stated above. We look forward to welcoming you to the team.");

                    col.Item().PaddingTop(25).Text($"Sincerely,");
                    col.Item().Text(companyName);

                    col.Item().PaddingTop(30).Text("Candidate acknowledgement").Bold();
                    col.Item().Text($"I, {applicantName}, accept the terms of this offer.");
                    col.Item().PaddingTop(20).Text("Signature: ______________________    Date: ______________");
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }
}
