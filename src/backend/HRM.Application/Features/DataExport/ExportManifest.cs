using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HRM.Application.Features.DataExport;

/// <summary>
/// US-ADM-010 (FR-6): the manifest.json model + helpers. The manifest records the export identity plus, per
/// file, its entity, row count, byte size, and a REAL SHA-256 checksum of the file's bytes (computed by
/// <see cref="Sha256Hex"/>). Serialized to snake_case JSON for the bundle.
/// </summary>
public sealed record ExportManifest(
    [property: JsonPropertyName("export_id")] Guid ExportId,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("tenant_name")] string TenantName,
    [property: JsonPropertyName("export_timestamp")] DateTime ExportTimestamp,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("files")] IReadOnlyList<ExportManifestFile> Files)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Serializes this manifest to pretty-printed UTF-8 JSON bytes (no BOM — it is JSON, not CSV).</summary>
    public byte[] ToJsonBytes() => JsonSerializer.SerializeToUtf8Bytes(this, Options);

    /// <summary>Serializes to a JSON string (stored on the ExportRequest for quick history reads).</summary>
    public string ToJsonString() => JsonSerializer.Serialize(this, Options);

    /// <summary>The lowercase-hex SHA-256 of the given bytes (FR-6 — a real, verifiable checksum).</summary>
    public static string Sha256Hex(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

/// <summary>US-ADM-010 (FR-6): a per-file manifest entry.</summary>
public sealed record ExportManifestFile(
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("entity")] string Entity,
    [property: JsonPropertyName("row_count")] int RowCount,
    [property: JsonPropertyName("file_size_bytes")] long FileSizeBytes,
    [property: JsonPropertyName("sha256_checksum")] string Sha256Checksum);

/// <summary>
/// US-ADM-010: reads the stored manifest JSON back into a total-bytes figure for the history/status DTO. Pure;
/// returns null when the manifest is absent or unparseable (an in-progress/failed request has none).
/// </summary>
public static class ManifestReader
{
    public static long? TotalBytes(string? manifestJson)
    {
        if (string.IsNullOrWhiteSpace(manifestJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(manifestJson);
            if (!doc.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
                return null;

            long total = 0;
            foreach (var file in files.EnumerateArray())
            {
                if (file.TryGetProperty("file_size_bytes", out var size) && size.TryGetInt64(out var bytes))
                    total += bytes;
            }
            return total;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
