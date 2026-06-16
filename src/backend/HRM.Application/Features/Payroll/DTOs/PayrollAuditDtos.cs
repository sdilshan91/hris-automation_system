namespace HRM.Application.Features.Payroll.DTOs;

/// <summary>
/// US-PAY-012 — DTOs for the payroll history list (FR-1/AC-1), the payroll audit trail (FR-4/AC-4), the
/// per-run audit timeline (FR-6/AC-2) and audit export (FR-5). All reads are tenant-scoped (AC-5).
/// </summary>

// ── FR-1 / AC-1: payroll run history ─────────────────────────────────────────

/// <summary>One row in the payroll run history list (AC-1 columns).</summary>
public sealed record PayrollRunHistoryItemDto(
    Guid RunId,
    int PayMonth,
    int PayYear,
    string Period,            // "YYYY-MM"
    string Status,
    int EmployeeCount,        // ProcessedEmployees
    decimal TotalNet,
    decimal TotalGross,
    decimal TotalDeductions,
    Guid InitiatedBy,
    DateTime InitiatedAt,
    Guid? ApprovedBy,
    DateTime? ApprovedAt,
    DateTime? FinalizedAt);

/// <summary>Paginated payroll run history (FR-1).</summary>
public sealed record PayrollRunHistoryPageDto(
    IReadOnlyList<PayrollRunHistoryItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>Query filter/sort/paging for the run history list (FR-1).</summary>
public sealed record PayrollRunHistoryFilter
{
    public int? Year { get; init; }
    public int? Month { get; init; }
    public string? Status { get; init; }
    /// <summary>One of: period | status | net | initiated | finalized. Defaults to period.</summary>
    public string? SortBy { get; init; }
    /// <summary>true => descending (default). false => ascending.</summary>
    public bool Descending { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

// ── FR-3: a structured audit-log entry (the wire shape) ──────────────────────

/// <summary>
/// A single structured audit-log entry (FR-3, technical doc §19.9). before/after are raw JSON strings
/// (the FE renders the diff view); null for create/delete respectively.
/// </summary>
public sealed record PayrollAuditEntryDto(
    Guid Id,
    Guid? TenantId,
    DateTime Timestamp,
    Guid? ActorUserId,
    string? ActorEmployeeNo,
    string Action,
    string? ResourceType,
    string? ResourceId,
    string? Before,
    string? After,
    string? IpAddress,
    string? UserAgent,
    string? TraceId);

/// <summary>Paginated payroll audit trail (FR-4).</summary>
public sealed record PayrollAuditPageDto(
    IReadOnlyList<PayrollAuditEntryDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

// ── FR-4 / AC-4: audit-trail filter ──────────────────────────────────────────

/// <summary>
/// Filters for the payroll audit trail (FR-4): date range, action type, actor, resource type, resource id.
/// The trail is restricted to payroll actions (resource types in the payroll catalog) regardless of filter,
/// and is always tenant-scoped (AC-5).
/// </summary>
public sealed record PayrollAuditTrailFilter
{
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public string? Action { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

/// <summary>Export format for the audit trail (FR-5). Reuses the payroll-report renderer formats.</summary>
public sealed record PayrollAuditExportResult(byte[] FileContent, string FileName, string ContentType);
