using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveTypes.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveTypes.Queries;

/// <summary>
/// Lists all leave types for the current tenant, ordered by display_order.
/// Optionally filters by IsActive.
/// </summary>
public sealed record GetLeaveTypesQuery(
    bool? ActiveOnly = null
) : IRequest<Result<IReadOnlyList<LeaveTypeDto>>>;
