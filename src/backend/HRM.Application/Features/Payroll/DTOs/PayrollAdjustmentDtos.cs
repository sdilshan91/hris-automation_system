namespace HRM.Application.Features.Payroll.DTOs;

/// <summary>
/// One payroll adjustment as returned to the FE (US-PAY-007). Enums (<c>adjustmentType</c>, <c>status</c>)
/// arrive as PascalCase strings (global JsonStringEnumConverter). Money is a decimal number.
/// </summary>
public sealed record PayrollAdjustmentDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeNo,
    string EmployeeName,
    string AdjustmentType,
    decimal Amount,
    string Description,
    int ApplicablePayMonth,
    int ApplicablePayYear,
    bool IsTaxable,
    bool IsRecurring,
    int? RecurrenceEndMonth,
    int? RecurrenceEndYear,
    string Status,
    Guid? AppliedInPayrollRunId,
    Guid? ReferencePayrollSlipId,
    bool HasSupportingDocument,
    Guid? RecurringSeriesId,
    DateTime CreatedAt,
    /// <summary>BR-3 warning surfaced at create time: this Pending deduction would drive the employee's
    /// estimated net pay negative for the target period. Informational; creation is not blocked unless the
    /// adjustment ALONE exceeds the gross (see <c>CreatePayrollAdjustmentResult</c>).</summary>
    bool NegativeNetWarning);

/// <summary>
/// Result of creating an adjustment (US-PAY-007 FR-1). Carries the created row(s) plus a BR-3 warning flag.
/// For a recurring adjustment, <c>GeneratedOccurrences</c> is the count of future Pending records created
/// (FR-5/BR-6), and <c>Adjustment</c> is the originating (first) occurrence.
/// </summary>
public sealed record CreatePayrollAdjustmentResult(
    PayrollAdjustmentDto Adjustment,
    int GeneratedOccurrences,
    /// <summary>BR-3: the target period's run already has slips and this deduction would push the employee's
    /// net negative. A non-blocking warning the FE shows as an amber banner.</summary>
    bool NegativeNetWarning,
    /// <summary>BR-7/BR-8: the requested period was unavailable (Finalized run, or the run already entered
    /// Processing); the adjustment was retargeted to this (year, month). Null when no deferral happened.</summary>
    int? DeferredToPayYear,
    int? DeferredToPayMonth);

/// <summary>Paged adjustment list for the §8 Notion-style table (US-PAY-007).</summary>
public sealed record PayrollAdjustmentPageDto(
    IReadOnlyList<PayrollAdjustmentDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>Per-row outcome of a bulk CSV upload (US-PAY-007 FR-2).</summary>
public sealed record BulkAdjustmentRowResult(
    int RowNumber,
    string EmployeeNo,
    bool Success,
    Guid? AdjustmentId,
    string? Error,
    string? ErrorCode);

/// <summary>Outcome of a bulk CSV adjustment upload (US-PAY-007 FR-2/NFR-2).</summary>
public sealed record BulkAdjustmentResultDto(
    int TotalRows,
    int SucceededCount,
    int FailedCount,
    IReadOnlyList<BulkAdjustmentRowResult> Results);

/// <summary>A streamed supporting-document file (US-PAY-007 AC-3).</summary>
public sealed record AdjustmentDocumentDto(byte[] Content, string ContentType, string FileName);
