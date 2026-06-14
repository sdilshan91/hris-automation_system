using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>
/// US-ATT-007 AC-3/FR-4: on-demand (re)generation of the monthly summary for the given month.
/// </summary>
public sealed record GenerateMonthlySummaryCommand(int Year, int Month)
    : IRequest<Result<SummaryGenerationStatusDto>>;
