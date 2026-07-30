using System.Globalization;
using System.Text.RegularExpressions;
using HRM.Application.Features.Performance.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HRM.Infrastructure.Services.Performance;

/// <summary>
/// Shared QuestPDF renderer for the deferred Performance PDF exports (360 report, review-meeting record, PIP,
/// recommendation summary). Follows the established idiom of
/// <c>PerformanceDashboardService.RenderPdf</c> (ISSUE-126/DF-6): an A4 page with a tenant-brand-coloured
/// header band + title, tabular content, and a UTC-timestamped footer. Rendering is pure DTO → bytes, so it
/// adds NO data access and cannot widen tenant/PII exposure — the caller passes a DTO already produced by the
/// authorized, tenant-filtered read path. The tenant logo is intentionally NOT loaded here (the header renders
/// the brand colour + title only) so the four services need no extra <c>IFileStorage</c> dependency; the
/// dashboard's logo-header path (which already has that seam) is unchanged.
/// </summary>
internal static class PerformancePdfRenderer
{
    /// <summary>Default header colour when the tenant has no valid primary-colour brand set (mirrors the dashboard).</summary>
    private const string DefaultBrandColor = "#1E3A8A";

    /// <summary>A valid <c>#RRGGBB</c>/<c>#RGB</c> hex, or the default — guards QuestPDF against a malformed tenant colour.</summary>
    private static string ResolveBrandColor(string? brandColor)
    {
        var c = brandColor?.Trim();
        return !string.IsNullOrEmpty(c) && Regex.IsMatch(c, "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")
            ? c
            : DefaultBrandColor;
    }

    private static string Num(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Num(decimal? v) => v is null ? "-" : Num(v.Value);
    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string DateTimeUtc(DateTime? d)
        => d is null ? "-" : d.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>Strips tags from the sanitized-HTML notes so the PDF renders readable plain text, not markup.</summary>
    private static string Plain(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "-";
        var noTags = Regex.Replace(html, "<[^>]+>", " ");
        var decoded = System.Net.WebUtility.HtmlDecode(noTags);
        return Regex.Replace(decoded, "\\s+", " ").Trim() is { Length: > 0 } s ? s : "-";
    }

    /// <summary>Scaffolds the branded A4 document (header band + title, content column, footer) and returns the PDF bytes.</summary>
    private static byte[] Build(string title, string subtitle, string? brandColor, Action<ColumnDescriptor> content)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var brand = ResolveBrandColor(brandColor);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(t => t.FontSize(9));

                page.Header().Background(brand).Padding(12).Column(col =>
                {
                    col.Item().Text(title).FontColor(Colors.White).FontSize(16).Bold();
                    if (!string.IsNullOrWhiteSpace(subtitle))
                        col.Item().Text(subtitle).FontColor(Colors.White).FontSize(9);
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Spacing(10);
                    content(col);
                });

                page.Footer().AlignRight().Text(t =>
                {
                    t.Span("Generated ");
                    t.Span(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
                });
            });
        });

        return document.GeneratePdf();
    }

    // ── local table helpers ────────────────────────────────────────────────

    private static void KeyValue(ColumnDescriptor col, string brand, string heading, params (string Key, string Value)[] rows)
    {
        col.Item().Text(heading).Bold();
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(2); });
            foreach (var (k, v) in rows)
            {
                t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(k);
                t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(2).AlignRight().Text(v ?? "-");
            }
        });
    }

    // ══════════════════════════════════════════════════════════════════════
    //  360 report (US-PRF-005 FR-7)
    // ══════════════════════════════════════════════════════════════════════

    public static byte[] RenderFeedback360(Feedback360ResultsDto d, string? brandColor) => Build(
        "360-Degree Feedback Report",
        $"{d.RevieweeName}  ·  Composite score: {Num(d.CompositeScore)} / {d.RatingScaleMax}",
        brandColor,
        col =>
        {
            if (!string.IsNullOrWhiteSpace(d.ReleaseWarning))
                col.Item().Text(d.ReleaseWarning!).FontColor(Colors.Red.Medium).Italic();

            KeyValue(col, brandColor ?? string.Empty, "Summary",
                ("Composite Score", $"{Num(d.CompositeScore)} / {d.RatingScaleMax}"),
                ("Anonymous Feedback", d.IsAnonymousFeedback ? "Yes" : "No"),
                ("Peer Responses", $"{d.PeerResponseCount} (min {d.MinPeerReviewers})"),
                ("Min-Peer Threshold Met", d.MinPeerThresholdMet ? "Yes" : "No"));

            var brand = ResolveBrandColor(brandColor);

            if (d.CategoryAverages.Count > 0)
            {
                col.Item().Text("Category Averages").Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(1); });
                    foreach (var h in new[] { "Category", "Avg", "Responses", "Weight" })
                        t.Cell().Background(brand).Padding(3).Text(h).FontColor(Colors.White).Bold();
                    foreach (var c in d.CategoryAverages)
                    {
                        t.Cell().Padding(2).Text(c.CategoryName);
                        t.Cell().Padding(2).AlignRight().Text(Num(c.AverageRating));
                        t.Cell().Padding(2).AlignRight().Text(c.ResponseCount.ToString(CultureInfo.InvariantCulture));
                        t.Cell().Padding(2).AlignRight().Text(c.Weight.ToString(CultureInfo.InvariantCulture));
                    }
                });
            }

            if (d.CompetencyAverages.Count > 0)
            {
                col.Item().Text("Competency / Goal Averages").Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(4); c.RelativeColumn(1); c.RelativeColumn(1); });
                    foreach (var h in new[] { "Item", "Avg", "Responses" })
                        t.Cell().Background(brand).Padding(3).Text(h).FontColor(Colors.White).Bold();
                    foreach (var c in d.CompetencyAverages)
                    {
                        t.Cell().Padding(2).Text(c.Label);
                        t.Cell().Padding(2).AlignRight().Text(Num(c.AverageRating));
                        t.Cell().Padding(2).AlignRight().Text(c.ResponseCount.ToString(CultureInfo.InvariantCulture));
                    }
                });
            }
        });

    // ══════════════════════════════════════════════════════════════════════
    //  Review-meeting record (US-PRF-006 AC-4/FR-6)
    // ══════════════════════════════════════════════════════════════════════

    public static byte[] RenderReviewMeeting(ReviewExportDto d, string? brandColor) => Build(
        "Performance Review Record",
        $"{d.EmployeeName} ({d.EmployeeNo})  ·  {d.CycleName}",
        brandColor,
        col =>
        {
            var brand = ResolveBrandColor(brandColor);

            KeyValue(col, brand, "Summary",
                ("Reviewer", d.ReviewerName ?? "-"),
                ("Review Status", d.ReviewStatusName),
                ("Sign-off Status", d.SignoffStatusName),
                ("Self Score", Num(d.WeightedSelfScore)),
                ("Manager Score", Num(d.WeightedManagerScore)),
                ("Final Score", Num(d.FinalScore)),
                ("Submitted", DateTimeUtc(d.SubmittedAt)),
                ("Signed Off", DateTimeUtc(d.SignoffCompletedAt)),
                ("Locked", d.IsLocked ? "Yes" : "No"));

            if (!string.IsNullOrWhiteSpace(d.SummaryComment))
            {
                col.Item().Text("Summary Comment").Bold();
                col.Item().Text(Plain(d.SummaryComment));
            }

            if (d.Goals.Count > 0)
            {
                col.Item().Text("Goals & Ratings").Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(4); c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(1); });
                    foreach (var h in new[] { "Goal", "Weight", "Self", "Manager" })
                        t.Cell().Background(brand).Padding(3).Text(h).FontColor(Colors.White).Bold();
                    foreach (var g in d.Goals)
                    {
                        t.Cell().Padding(2).Text(g.GoalTitle);
                        t.Cell().Padding(2).AlignRight().Text(g.GoalWeight.ToString(CultureInfo.InvariantCulture));
                        t.Cell().Padding(2).AlignRight().Text(g.SelfRating?.ToString(CultureInfo.InvariantCulture) ?? "-");
                        t.Cell().Padding(2).AlignRight().Text(g.ManagerRating?.ToString(CultureInfo.InvariantCulture) ?? "-");
                    }
                });
            }

            if (d.MeetingNotes is { } notes)
            {
                col.Item().Text("Meeting Notes").Bold();
                KeyValue(col, brand, "Discussion",
                    ("Body", Plain(notes.Body)),
                    ("Strengths", Plain(notes.Strengths)),
                    ("Development Areas", Plain(notes.DevelopmentAreas)),
                    ("Summary", Plain(notes.Summary)));
            }

            if (d.Signoffs.Count > 0)
            {
                col.Item().Text("Signature Log").Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(2); });
                    foreach (var h in new[] { "Party", "Action", "Signer", "Signed At" })
                        t.Cell().Background(brand).Padding(3).Text(h).FontColor(Colors.White).Bold();
                    foreach (var s in d.Signoffs)
                    {
                        t.Cell().Padding(2).Text(s.PartyName);
                        t.Cell().Padding(2).Text(s.ActionName);
                        t.Cell().Padding(2).Text(s.SignerName);
                        t.Cell().Padding(2).Text(DateTimeUtc(s.SignedAt));
                    }
                });
            }
        });

    // ══════════════════════════════════════════════════════════════════════
    //  PIP record (US-PRF-008 AC-1/FR-5)
    // ══════════════════════════════════════════════════════════════════════

    public static byte[] RenderPip(PipDto d, string? brandColor) => Build(
        "Performance Improvement Plan",
        $"{d.EmployeeName} ({d.EmployeeNo})  ·  {Date(d.StartDate)} – {Date(d.EndDate)}",
        brandColor,
        col =>
        {
            var brand = ResolveBrandColor(brandColor);

            KeyValue(col, brand, "Summary",
                ("Status", d.StatusName),
                ("Manager", d.ManagerName ?? "-"),
                ("Mentor", d.MentorName ?? "-"),
                ("Escalation Action", d.EscalationActionName),
                ("Acknowledgement", d.AcknowledgementStatusName),
                ("Acknowledged At", DateTimeUtc(d.AcknowledgedAt)),
                ("Outcome Set At", DateTimeUtc(d.OutcomeSetAt)));

            col.Item().Text("Reason").Bold();
            col.Item().Text(Plain(d.Reason));

            if (d.Objectives.Count > 0)
            {
                col.Item().Text("Objectives").Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(4); c.RelativeColumn(1); });
                    foreach (var h in new[] { "Objective", "Success Criteria", "Due" })
                        t.Cell().Background(brand).Padding(3).Text(h).FontColor(Colors.White).Bold();
                    foreach (var o in d.Objectives)
                    {
                        t.Cell().Padding(2).Text(o.Title);
                        t.Cell().Padding(2).Text(o.SuccessCriteria);
                        t.Cell().Padding(2).Text(Date(o.DueDate));
                    }
                });
            }

            if (d.Checkpoints.Count > 0)
            {
                col.Item().Text("Checkpoints").Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(4); });
                    foreach (var h in new[] { "Date", "Status", "Evidence" })
                        t.Cell().Background(brand).Padding(3).Text(h).FontColor(Colors.White).Bold();
                    foreach (var c in d.Checkpoints)
                    {
                        t.Cell().Padding(2).Text(Date(c.CheckpointDate));
                        t.Cell().Padding(2).Text(c.ProgressStatusName);
                        t.Cell().Padding(2).Text(Plain(c.EvidenceNotes));
                    }
                });
            }
        });

    // ══════════════════════════════════════════════════════════════════════
    //  Recommendation summary (US-PRF (recommendations) FR-6)
    // ══════════════════════════════════════════════════════════════════════

    public static byte[] RenderRecommendationSummary(RecommendationSummaryDto s, string? brandColor) => Build(
        "Recommendation Summary",
        s.CycleName,
        brandColor,
        col =>
        {
            var brand = ResolveBrandColor(brandColor);

            KeyValue(col, brand, "Totals",
                ("Total Recommendations", s.TotalRecommendations.ToString(CultureInfo.InvariantCulture)),
                ("Total Promotions", s.TotalPromotions.ToString(CultureInfo.InvariantCulture)),
                ("Total Bonus Pool Allocated", Num(s.TotalBonusPoolAllocated)),
                ("Total Increment Allocated", Num(s.TotalIncrementAllocated)),
                ("Total Training Nominations", s.TotalTrainingNominations.ToString(CultureInfo.InvariantCulture)));

            if (s.ByStatus.Count > 0)
            {
                col.Item().Text("By Status").Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(1); });
                    foreach (var h in new[] { "Status", "Count" })
                        t.Cell().Background(brand).Padding(3).Text(h).FontColor(Colors.White).Bold();
                    foreach (var row in s.ByStatus)
                    {
                        t.Cell().Padding(2).Text(row.StatusName);
                        t.Cell().Padding(2).AlignRight().Text(row.Count.ToString(CultureInfo.InvariantCulture));
                    }
                });
            }

            if (s.IncrementByDepartment.Count > 0)
            {
                col.Item().Text("Increment by Department").Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(1); c.RelativeColumn(2); });
                    foreach (var h in new[] { "Department", "Count", "Total Increment" })
                        t.Cell().Background(brand).Padding(3).Text(h).FontColor(Colors.White).Bold();
                    foreach (var row in s.IncrementByDepartment)
                    {
                        t.Cell().Padding(2).Text(row.DepartmentName);
                        t.Cell().Padding(2).AlignRight().Text(row.RecommendationCount.ToString(CultureInfo.InvariantCulture));
                        t.Cell().Padding(2).AlignRight().Text(Num(row.TotalIncrementAmount));
                    }
                });
            }
        });
}
