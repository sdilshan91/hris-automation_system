using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>
/// US-ATT-007 AC-1/AC-5/FR-5: the HR monthly attendance summary list for a month, with optional
/// department/location/shift/status filters.
/// </summary>
public sealed record GetMonthlySummaryQuery(int Year, int Month, MonthlySummaryFilter Filter)
    : IRequest<Result<MonthlySummaryResult>>;
