using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>Query: the monthly HR overtime report for a given year/month (US-ATT-006 AC-5).</summary>
public sealed record GetOvertimeReportQuery(
    int Year, int Month) : IRequest<Result<OvertimeReportResult>>;
