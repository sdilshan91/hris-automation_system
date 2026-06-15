namespace HRM.Application.Features.Payroll.DTOs;

/// <summary>Result of accepting a generate/regenerate-payslips request (US-PAY-004 AC-1). Returned as 202.</summary>
public sealed record PayslipGenerationAcceptedDto
{
    public required Guid RunId { get; init; }
    /// <summary>Number of slips queued for (re)generation — i.e. the run's slip count.</summary>
    public required int QueuedCount { get; init; }
    /// <summary>True when an existing set was overwritten (AC-5 regenerate), false on first generation.</summary>
    public required bool Regenerated { get; init; }
}

/// <summary>
/// PDF generation status for a run (US-PAY-004 FR-7, §8 status bar "4,850 / 5,000 generated"). Counts per
/// <c>PayrollSlip.PdfStatus</c> across the run's slips.
/// </summary>
public sealed record PayslipGenerationStatusDto
{
    public required Guid RunId { get; init; }
    public required int TotalSlips { get; init; }
    public required int Generated { get; init; }
    public required int Pending { get; init; }
    public required int Failed { get; init; }
    /// <summary>True when every slip is Generated (no Pending, no Failed).</summary>
    public required bool IsComplete { get; init; }
}

/// <summary>A streamable payslip PDF (US-PAY-004 FR-6). The controller writes <see cref="Content"/> as a file.</summary>
public sealed record PayslipFileDto(byte[] Content, string FileName, string ContentType);

/// <summary>
/// One row of the per-run payslip list (US-PAY-004 §8 Notion-style table): employee name / no / department,
/// net salary, and the PDF generation status. Backs the FE table + per-row View/Download actions. Tenant-scoped
/// via the EF global query filter (AC-4).
/// </summary>
public sealed record PayslipListItemDto
{
    /// <summary>The payroll_slip id (the FE addresses single downloads by this id).</summary>
    public required Guid SlipId { get; init; }
    public required Guid EmployeeId { get; init; }
    public required string EmployeeNo { get; init; }
    public required string EmployeeName { get; init; }
    public string? Department { get; init; }
    public required decimal NetSalary { get; init; }
    /// <summary>Pending / Generated / Failed, or null when never generated (treated as Pending by the FE).</summary>
    public string? PdfStatus { get; init; }
    public DateTime? PdfGeneratedAt { get; init; }
    public int? PdfFileSizeBytes { get; init; }
}
