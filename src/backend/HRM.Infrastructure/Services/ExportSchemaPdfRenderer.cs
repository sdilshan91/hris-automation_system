using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-ADM-010 / GAP-028 (C5): renders <c>schema.pdf</c> — the human-readable data dictionary shipped inside a
/// tenant data-export bundle.
///
/// <para><b>Why a PDF is part of a data export at all.</b> The story's stated purpose is GDPR Art. 20
/// portability, which requires the data be provided in an "intelligible form". A folder of CSVs with column
/// names like <c>fte</c> and <c>reports_to_employee_id</c> is machine-readable but not intelligible to the
/// person exercising the right; the schema document is what makes the bundle self-describing to a human.</para>
///
/// <para><b>Derived from the bytes actually exported, never from a parallel reflection pass.</b> Every column
/// listed here is read back out of the header row of the CSV that ships in the same ZIP. A second pass over
/// the EF model would be a second description of one truth — the exact defect class this codebase has spent
/// this programme removing — and it would drift the first time a property was excluded from the CSV writer but
/// not from the renderer. If the PDF says a column exists, it exists, because the same bytes produced both.</para>
/// </summary>
public static class ExportSchemaPdfRenderer
{
    /// <summary>One exported file as the dictionary describes it.</summary>
    /// <param name="FileName">The name inside the ZIP, e.g. <c>employees.csv</c>.</param>
    /// <param name="EntityCode">The stable export code from <c>ExportEntityRegistry</c>.</param>
    /// <param name="RowCount">Rows exported (excluding the header).</param>
    /// <param name="Columns">Column names, read from the file's own header row.</param>
    public sealed record FileSchema(string FileName, string EntityCode, int RowCount, IReadOnlyList<string> Columns);

    /// <summary>
    /// Reads the column names out of a BOM'd, delimited CSV's first line.
    /// </summary>
    /// <remarks>
    /// Deliberately simple: the export writer quotes a field only when it contains the delimiter, a quote or a
    /// newline, and entity COLUMN names in this model are plain identifiers — so a split on the delimiter is
    /// accurate for the header row specifically. It is not a general CSV parser and is not used as one; a
    /// quoted header would surface with its quotes, which is legible rather than wrong.
    /// </remarks>
    public static IReadOnlyList<string> ReadHeaderColumns(byte[] csvBytes, char delimiter)
    {
        if (csvBytes.Length == 0)
        {
            return [];
        }

        var text = new UTF8Encoding(false).GetString(csvBytes);
        // Strip a UTF-8 BOM if the writer emitted one.
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        var newline = text.IndexOfAny(['\r', '\n']);
        var header = newline < 0 ? text : text[..newline];

        return header.Length == 0
            ? []
            : header.Split(delimiter).Select(c => c.Trim()).ToList();
    }

    public static byte[] Render(
        string tenantName,
        Guid exportId,
        DateTime generatedAtUtc,
        string scope,
        IReadOnlyList<FileSchema> files)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(9));

                page.Header().Column(header =>
                {
                    header.Item().Text("Data export — schema").FontSize(16).SemiBold();
                    header.Item().PaddingTop(2).Text(tenantName).FontSize(11);
                    header.Item().PaddingTop(2).Text(
                        $"Export {exportId} · scope {scope} · generated {generatedAtUtc:yyyy-MM-dd HH:mm} UTC")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                    header.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingVertical(10).Column(body =>
                {
                    body.Item().PaddingBottom(8).Text(
                        "This bundle contains the data held about your organisation, one file per record type. "
                        + "Every column below is read from the header row of the file it describes, so this "
                        + "document and the data cannot disagree. Checksums for each file are in manifest.json.")
                        .FontSize(9).FontColor(Colors.Grey.Darken2);

                    if (files.Count == 0)
                    {
                        body.Item().Text("No files were included in this export.").Italic();
                        return;
                    }

                    foreach (var file in files)
                    {
                        body.Item().PaddingTop(10).Text(file.FileName).FontSize(11).SemiBold();
                        body.Item().Text($"{file.EntityCode} · {file.RowCount:N0} row(s) · {file.Columns.Count} column(s)")
                            .FontSize(8).FontColor(Colors.Grey.Darken1);

                        if (file.Columns.Count == 0)
                        {
                            body.Item().PaddingTop(2)
                                .Text("Not a columnar file — see the file itself for its structure.")
                                .FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
                            continue;
                        }

                        body.Item().PaddingTop(3).Text(string.Join(" · ", file.Columns)).FontSize(8);
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.CurrentPageNumber().FontSize(8);
                    t.Span(" / ").FontSize(8);
                    t.TotalPages().FontSize(8);
                });
            });
        });

        return document.GeneratePdf();
    }
}
