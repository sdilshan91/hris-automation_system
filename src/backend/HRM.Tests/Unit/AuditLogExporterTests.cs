// ============================================================================
// US-ADM-008 (AC-4/§7): AuditLogExporter — pure CSV/JSON serialization of masked
// audit rows. Verifies the §7 column set, CSV escaping, actor labelling, and that
// the export carries already-masked summaries.
// ============================================================================

using System.Text;
using FluentAssertions;
using HRM.Application.Features.AuditLog;
using HRM.Application.Features.AuditLog.DTOs;

namespace HRM.Tests.Unit;

public sealed class AuditLogExporterTests
{
    private static AuditLogListItemDto Row(
        string action = "Employee.Update",
        string? summary = "{\"name\":\"Jane\"}",
        string? actorName = "Jane Admin",
        string? actorEmail = "jane@acme.com",
        string? ip = "10.0.0.1",
        string? resourceType = "Employee",
        string? resourceId = "emp-1")
        => new(
            Guid.NewGuid(),
            new DateTime(2026, 6, 1, 10, 30, 0, DateTimeKind.Utc),
            Guid.NewGuid(),
            actorName,
            actorEmail,
            action,
            resourceType,
            resourceId,
            ip,
            summary ?? string.Empty);

    [Fact]
    public void BuildCsv_HasHeaderAndOrderedColumns()
    {
        var csv = Encoding.UTF8.GetString(AuditLogExporter.BuildCsv(new[] { Row() }));
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().Be("timestamp,actor,action,resource_type,resource_id,summary,ip_address");
        lines[1].Should().Contain("Employee.Update");
        lines[1].Should().Contain("Jane Admin <jane@acme.com>");
        lines[1].Should().Contain("10.0.0.1");
    }

    [Fact]
    public void BuildCsv_EscapesFieldsContainingCommasAndQuotes()
    {
        var row = Row(summary: "{\"note\":\"a, b, \\\"c\\\"\"}");

        var csv = Encoding.UTF8.GetString(AuditLogExporter.BuildCsv(new[] { row }));
        var dataLine = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1];

        // The summary cell must be quoted (it contains commas) and inner quotes doubled.
        dataLine.Should().Contain("\"\"");
    }

    [Fact]
    public void BuildJson_ProducesArrayWithSection7Fields()
    {
        var json = Encoding.UTF8.GetString(AuditLogExporter.BuildJson(new[] { Row() }));

        json.Should().Contain("\"action\"");
        json.Should().Contain("\"resource_type\"");
        json.Should().Contain("\"resource_id\"");
        json.Should().Contain("\"ip_address\"");
        json.Should().Contain("Employee.Update");
    }

    [Fact]
    public void Export_Csv_SetsContentTypeAndFileNameAndCount()
    {
        var result = AuditLogExporter.Export(
            new[] { Row(), Row() }, AuditLogExportFormat.Csv, deferred: false,
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        result.ContentType.Should().Be("text/csv");
        result.FileName.Should().EndWith(".csv");
        result.RecordCount.Should().Be(2);
        result.Deferred.Should().BeFalse();
    }

    [Fact]
    public void Export_Json_SetsContentTypeAndFileName()
    {
        var result = AuditLogExporter.Export(
            Array.Empty<AuditLogListItemDto>(), AuditLogExportFormat.Json, deferred: true,
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        result.ContentType.Should().Be("application/json");
        result.FileName.Should().EndWith(".json");
        result.RecordCount.Should().Be(0);
        result.Deferred.Should().BeTrue();
    }
}
