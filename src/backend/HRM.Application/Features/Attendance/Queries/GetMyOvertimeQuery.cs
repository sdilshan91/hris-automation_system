using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>Query: the acting employee's own overtime records, newest first (US-ATT-006).</summary>
public sealed record GetMyOvertimeQuery : IRequest<Result<IReadOnlyList<OvertimeDto>>>;
