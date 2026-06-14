using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>Query: the acting manager's pending overtime-approval queue (US-ATT-006 AC-3/FR-5).</summary>
public sealed record GetPendingOvertimeQuery : IRequest<Result<OvertimeQueueResult>>;
