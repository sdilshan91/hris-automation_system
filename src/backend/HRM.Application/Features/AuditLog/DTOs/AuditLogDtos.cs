namespace HRM.Application.Features.AuditLog.DTOs;

/// <summary>
/// US-ADM-008 §7: the filter inputs shared by the LIST and the EXPORT (same filters, BR-4/AC-4). All optional;
/// AND semantics ACROSS filter types (AC-2). Dates are inclusive UTC bounds against <c>created_at</c>.
///
/// <para>US-NTF-005 FR-2 extends this ADDITIVELY with multi-select action/resource-type filters: the singular
/// <see cref="Action"/>/<see cref="ResourceType"/> remain (back-compat for US-ADM-008 + its tests), and the new
/// <see cref="Actions"/>/<see cref="ResourceTypes"/> arrays add IN-list semantics. Within a multi-select group
/// the match is OR; the singular value is folded in as one more OR-member of its group. The two groups (and the
/// date/actor/keyword filters) still combine with AND.</para>
/// </summary>
public sealed record AuditLogFilter(
    DateTime? StartDate,
    DateTime? EndDate,
    Guid? ActorUserId,
    string? Action,
    string? ResourceType,
    string? SearchQuery,
    IReadOnlyList<string>? Actions = null,
    IReadOnlyList<string>? ResourceTypes = null);

/// <summary>
/// US-NTF-005 FR-2: one actor that appears in THIS tenant's audit log, for the actor type-ahead. Tenant-scoped,
/// distinct by user id. <see cref="Name"/>/<see cref="Email"/> are resolved from the global users table.
/// </summary>
public sealed record AuditLogActorDto(
    Guid UserId,
    string? Name,
    string? Email);

/// <summary>
/// US-NTF-005 FR-2: the distinct action names + resource types present in THIS tenant's audit log, to populate
/// the multi-select dropdowns. Tenant-scoped; sorted ascending.
/// </summary>
public sealed record AuditLogFilterOptionsDto(
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> ResourceTypes);

/// <summary>US-ADM-008 AC-1/FR-1: one row in the paginated audit list. Before/After are MASKED summaries.</summary>
public sealed record AuditLogListItemDto(
    Guid Id,
    DateTime Timestamp,
    Guid? ActorUserId,
    string? ActorName,
    string? ActorEmail,
    string Action,
    string? ResourceType,
    string? ResourceId,
    string? IpAddress,
    string Summary);

/// <summary>
/// US-ADM-008 AC-1/FR-1: the paged audit-list envelope. <see cref="RetentionDays"/> is the tenant's
/// plan-governed retention window, surfaced READ-ONLY for the retention indicator (BR-5).
/// </summary>
public sealed record AuditLogPageDto(
    IReadOnlyList<AuditLogListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int RetentionDays);

/// <summary>
/// US-ADM-008 AC-3/FR-2: the full audit record. <see cref="Before"/>/<see cref="After"/> are MASKED JSON
/// (FR-4); the frontend computes the visual diff (FR-3) from them — the backend does NOT diff server-side.
/// </summary>
public sealed record AuditLogDetailDto(
    Guid Id,
    DateTime Timestamp,
    Guid? ActorUserId,
    string? ActorName,
    string? ActorEmail,
    string? ActorEmployeeNo,
    string Action,
    string? ResourceType,
    string? ResourceId,
    string? Before,
    string? After,
    string? IpAddress,
    string? UserAgent,
    string? TraceId);

/// <summary>The export file formats supported (AC-4/§7).</summary>
public enum AuditLogExportFormat
{
    Csv = 0,
    Json = 1,
}

/// <summary>
/// US-ADM-008 AC-4/FR-5: the materialized export. For the synchronous (small-dataset) path the bytes are
/// returned inline for immediate download. <see cref="Deferred"/> is true when the dataset exceeds
/// <c>LargeExportThreshold</c> and the async Hangfire+email path WOULD take over — that path is DEFERRED, so
/// today the call still returns the file synchronously with <see cref="Deferred"/> flagged for the client.
/// </summary>
public sealed record AuditLogExportResult(
    byte[] Content,
    string ContentType,
    string FileName,
    int RecordCount,
    bool Deferred);
