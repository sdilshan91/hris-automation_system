using System.Globalization;
using System.Text;
using System.Text.Json;
using HRM.Application.Features.AuditLog.DTOs;

namespace HRM.Application.Features.AuditLog;

/// <summary>
/// US-ADM-008 AC-4/§7: a PURE (no DB, no I/O) serializer that turns already-resolved, already-FILTERED audit
/// rows into a CSV or JSON byte payload. Sensitive values are masked here too (the row summaries fed in are
/// already masked; this is belt-and-braces for the JSON before/after). Kept pure so the export shape + masking
/// are unit-testable without a service or database.
///
/// <para>CSV columns per §7: timestamp, actor, action, resource_type, resource_id, summary, ip_address.</para>
/// </summary>
public static class AuditLogExporter
{
    private static readonly string[] CsvHeader =
        ["timestamp", "actor", "action", "resource_type", "resource_id", "summary", "ip_address"];

    /// <summary>Serializes the rows to the requested format, returning the bytes + the correct content type + a file name.</summary>
    public static AuditLogExportResult Export(
        IReadOnlyList<AuditLogListItemDto> rows,
        AuditLogExportFormat format,
        bool deferred,
        DateTime generatedAtUtc)
    {
        var stamp = generatedAtUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        return format switch
        {
            AuditLogExportFormat.Json => new AuditLogExportResult(
                BuildJson(rows), "application/json", $"audit-log-{stamp}.json", rows.Count, deferred),
            _ => new AuditLogExportResult(
                BuildCsv(rows), "text/csv", $"audit-log-{stamp}.csv", rows.Count, deferred),
        };
    }

    /// <summary>CSV per §7. Each row's Summary is already masked by the caller.</summary>
    public static byte[] BuildCsv(IReadOnlyList<AuditLogListItemDto> rows)
    {
        var sb = new StringBuilder();
        // Use an explicit LF (not AppendLine, which emits the platform newline — \r\n on Windows) so the export
        // is byte-identical on the dev box and Linux CI, matching the LF the data rows below use.
        sb.Append(string.Join(',', CsvHeader)).Append('\n');

        foreach (var r in rows)
        {
            var actor = ActorLabel(r);
            sb.Append(Csv(r.Timestamp.ToString("O", CultureInfo.InvariantCulture))).Append(',')
              .Append(Csv(actor)).Append(',')
              .Append(Csv(r.Action)).Append(',')
              .Append(Csv(r.ResourceType)).Append(',')
              .Append(Csv(r.ResourceId)).Append(',')
              .Append(Csv(r.Summary)).Append(',')
              .Append(Csv(r.IpAddress)).Append('\n');
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>JSON (array of the masked list rows). Summaries are already masked by the caller.</summary>
    public static byte[] BuildJson(IReadOnlyList<AuditLogListItemDto> rows)
    {
        var projected = rows.Select(r => new
        {
            timestamp = r.Timestamp,
            actor = ActorLabel(r),
            action = r.Action,
            resource_type = r.ResourceType,
            resource_id = r.ResourceId,
            summary = r.Summary,
            ip_address = r.IpAddress,
        });

        return JsonSerializer.SerializeToUtf8Bytes(
            projected, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ActorLabel(AuditLogListItemDto r)
    {
        if (!string.IsNullOrWhiteSpace(r.ActorName) && !string.IsNullOrWhiteSpace(r.ActorEmail))
            return $"{r.ActorName} <{r.ActorEmail}>";
        if (!string.IsNullOrWhiteSpace(r.ActorEmail))
            return r.ActorEmail!;
        if (!string.IsNullOrWhiteSpace(r.ActorName))
            return r.ActorName!;
        return r.ActorUserId?.ToString() ?? "system";
    }

    /// <summary>RFC-4180 CSV field escaping: wrap in quotes when the value contains a comma, quote, or newline.</summary>
    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuoting)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
