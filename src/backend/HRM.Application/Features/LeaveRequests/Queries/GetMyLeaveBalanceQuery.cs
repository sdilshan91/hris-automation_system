using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

/// <summary>
/// Query for the current employee's leave-balance dashboard (US-LV-006 FR-1/FR-2, AC-1/AC-5).
/// Returns one <see cref="LeaveBalanceDto"/> per active leave type (BR-3) for the given leave
/// year (defaults to the current leave year when null, BR-4). Deactivated types that still
/// carry a non-zero balance are returned with <c>IsArchived = true</c>.
/// </summary>
public sealed record GetMyLeaveBalanceQuery(int? Year)
    : IRequest<Result<IReadOnlyList<LeaveBalanceDto>>>;
