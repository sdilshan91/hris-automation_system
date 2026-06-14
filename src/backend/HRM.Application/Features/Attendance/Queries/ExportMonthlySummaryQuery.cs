using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>
/// US-ATT-007 AC-4/FR-6: export the monthly summary as csv/xlsx/pdf for a month + filters.
/// </summary>
public sealed record ExportMonthlySummaryQuery(
    int Year, int Month, string Format, MonthlySummaryFilter Filter)
    : IRequest<Result<MonthlySummaryExportResult>>;
