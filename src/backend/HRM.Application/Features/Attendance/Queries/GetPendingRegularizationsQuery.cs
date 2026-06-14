using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>
/// Query for a manager's pending regularization-approval queue (US-ATT-004 FR-1/AC-3): pending
/// requests from the manager's direct reports, with optional employee/date filtering.
/// </summary>
public sealed record GetPendingRegularizationsQuery(
    PendingRegularizationQueryParams QueryParams) : IRequest<Result<PendingRegularizationQueueResult>>;
