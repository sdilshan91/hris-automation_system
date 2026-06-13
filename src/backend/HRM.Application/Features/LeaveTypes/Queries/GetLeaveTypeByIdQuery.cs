using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveTypes.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveTypes.Queries;

/// <summary>
/// Gets a single leave type by its ID within the current tenant.
/// </summary>
public sealed record GetLeaveTypeByIdQuery(
    Guid LeaveTypeId
) : IRequest<Result<LeaveTypeDto>>;
