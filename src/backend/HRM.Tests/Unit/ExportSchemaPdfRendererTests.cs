// ============================================================================
// C5 / GAP-028 — the PARSING layer of the export schema document.
//
// The integration arm proves schema.pdf is in the bundle and checksummed. It cannot prove the columns are
// right: QuestPDF output is compressed, so asserting a column name against the PDF bytes is not meaningful.
// The column list is produced by ReadHeaderColumns, so that is where the property lives and where it is
// pinned — a BOM-strip or delimiter regression would otherwise ship green behind a valid-looking PDF.
//
// Why this matters more than it looks: the renderer's whole claim is that the document and the data cannot
// disagree, because both come from the same bytes. If the header parse is wrong, the claim inverts — the
// PDF confidently describes columns the CSV does not have.
// ============================================================================

using System.Text;
using FluentAssertions;
using HRM.Infrastructure.Services;

namespace HRM.Tests.Unit;

public sealed class ExportSchemaPdfRendererTests
{
    private static byte[] Csv(string text, bool withBom = true)
    {
        var body = Encoding.UTF8.GetBytes(text);
        if (!withBom)
        {
            return body;
        }

        var bom = new UTF8Encoding(true).GetPreamble();
        return [.. bom, .. body];
    }

    /// <summary>
    /// THE ARM THAT MATTERS. The export writer emits a UTF-8 BOM (<c>CsvSerializer.WithBom</c>), and
    /// <c>UTF8Encoding.GetString</c> does NOT strip it — the flag only affects <c>GetPreamble</c>. Left in,
    /// the first column renders as <c>﻿id</c> in the data dictionary: a column name that does not exist.
    /// </summary>
    [Fact]
    public void Strips_the_utf8_bom_the_export_writer_emits()
    {
        var columns = ExportSchemaPdfRenderer.ReadHeaderColumns(Csv("id,name,email\n1,a,b\n"), ',');

        columns.Should().Equal("id", "name", "email");
        columns[0].Should().NotStartWith("﻿", "a BOM left on the first column names a field nobody has");
    }

    [Fact]
    public void Reads_the_header_when_there_is_no_bom()
    {
        ExportSchemaPdfRenderer.ReadHeaderColumns(Csv("id,name\n", withBom: false), ',')
            .Should().Equal("id", "name");
    }

    /// <summary>
    /// The delimiter is threaded from the same local that fed the CSV writer, so the two cannot disagree —
    /// but only if this honours it. Splitting on a hard-coded comma would silently collapse a semicolon file
    /// into one giant "column".
    /// </summary>
    [Theory]
    [InlineData(';')]
    [InlineData('\t')]
    public void Honours_the_delimiter_the_csv_was_written_with(char delimiter)
    {
        var csv = Csv($"id{delimiter}name{delimiter}email\n");

        ExportSchemaPdfRenderer.ReadHeaderColumns(csv, delimiter)
            .Should().Equal("id", "name", "email");
    }

    [Fact]
    public void Splitting_on_the_wrong_delimiter_does_not_silently_look_correct()
    {
        // Guardian for the arm above: if it passed regardless of delimiter, that theory would prove nothing.
        ExportSchemaPdfRenderer.ReadHeaderColumns(Csv("id;name;email\n"), ',')
            .Should().ContainSingle().Which.Should().Be("id;name;email");
    }

    [Theory]
    [InlineData("id,name\r\nrow\r\n")]
    [InlineData("id,name\nrow\n")]
    [InlineData("id,name")]
    public void Reads_only_the_first_line_whatever_the_line_endings(string text)
    {
        ExportSchemaPdfRenderer.ReadHeaderColumns(Csv(text), ',')
            .Should().Equal("id", "name");
    }

    [Fact]
    public void An_empty_file_has_no_columns()
    {
        ExportSchemaPdfRenderer.ReadHeaderColumns([], ',').Should().BeEmpty();
        ExportSchemaPdfRenderer.ReadHeaderColumns(Csv(string.Empty), ',').Should().BeEmpty();
    }

    /// <summary>
    /// The renderer must produce a real PDF for the states the bundle can actually be in — including a
    /// non-columnar file (audit_log.jsonl), which reports zero columns by fact rather than by omission.
    /// </summary>
    [Fact]
    public void Renders_a_pdf_for_a_bundle_mixing_columnar_and_non_columnar_files()
    {
        var pdf = ExportSchemaPdfRenderer.Render(
            "Acme", Guid.NewGuid(), DateTime.UtcNow, "full",
            [
                new ExportSchemaPdfRenderer.FileSchema("employees.csv", "Employees", 2, ["id", "name"]),
                new ExportSchemaPdfRenderer.FileSchema("audit_log.jsonl", "AuditLog", 7, []),
            ]);

        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Renders_a_pdf_when_the_export_selected_nothing()
    {
        var pdf = ExportSchemaPdfRenderer.Render("Acme", Guid.NewGuid(), DateTime.UtcNow, "partial", []);

        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }
}
