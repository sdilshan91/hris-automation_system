using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Queries;

/// <summary>
/// BUG-291 exposure report — spreadsheet export variant (READ-ONLY). Renders the SAME affected population as
/// <see cref="GetAccrualOverCreditExposureQuery"/> to CSV or XLSX so Finance can work the list case-by-case.
/// Remediation tooling for a specific defect, not a permanent reporting feature. Reuses the Performance
/// <see cref="PerformanceExportFile"/> download shape. An unsupported <paramref name="Format"/> fails with
/// <c>invalid_format</c>.
/// </summary>
public sealed record ExportAccrualOverCreditExposureQuery(
    DateOnly AsOfDate,
    string? Format
) : IRequest<Result<PerformanceExportFile>>;
