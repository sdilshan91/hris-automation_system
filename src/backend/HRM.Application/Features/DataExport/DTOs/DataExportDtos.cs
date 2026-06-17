using HRM.Domain.Entities;

namespace HRM.Application.Features.DataExport.DTOs;

/// <summary>
/// US-ADM-010 (FR-1): the export-initiation request body. <see cref="Scope"/> is either "full" or a list of
/// entity codes (e.g. ["Employees","LeaveTypes"]); when null/empty it defaults to a full export. Format options
/// (FR-3) are optional: a single-char CSV delimiter (default ',') and a .NET date-format token.
/// </summary>
public sealed record ExportInitiateRequest(
    string? Scope,
    IReadOnlyList<string>? Entities,
    string? Delimiter,
    string? DateFormat);

/// <summary>US-ADM-010 (AC-1): the confirmation returned on initiation — the export id + the "you'll get an email" note.</summary>
public sealed record ExportInitiatedDto(
    Guid ExportId,
    string Status,
    string Message);

/// <summary>US-ADM-010 (FR-9 / §8): an export-request status/history row.</summary>
public sealed record ExportRequestDto(
    Guid Id,
    string Scope,
    string Status,
    Guid RequestedByUserId,
    DateTime RequestedAt,
    DateTime? CompletedAt,
    DateTime? ExpiresAt,
    int? RowCountTotal,
    long? FileSizeBytes,
    bool DownloadAvailable,
    bool InitiatedBySystemAdmin,
    string? Error)
{
    /// <summary>Projects an entity row to the history/status DTO. <paramref name="fileSize"/> is read from the manifest.</summary>
    public static ExportRequestDto From(ExportRequest e, DateTime nowUtc)
    {
        var available = e.Status == ExportRequestStatus.Completed
            && e.ExpiresAt is { } exp && exp > nowUtc;

        return new ExportRequestDto(
            e.Id,
            e.Scope,
            e.Status.ToString(),
            e.RequestedByUserId,
            e.RequestedAt,
            e.CompletedAt,
            e.ExpiresAt,
            e.RowCountTotal,
            ManifestReader.TotalBytes(e.ManifestJson),
            available,
            e.InitiatedBySystemAdmin,
            e.Error);
    }
}

/// <summary>US-ADM-010 (AC-3/FR-7): a download payload — the bundle bytes + content type + filename.</summary>
public sealed record ExportDownloadDto(
    byte[] Content,
    string ContentType,
    string FileName);
