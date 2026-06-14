using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Commands;

/// <summary>
/// HR manually assigns LOP days to an employee for the given dates (US-LV-011 FR-3, AC-3).
/// Delegates to <c>ILopService.AssignLopAsync</c>.
/// </summary>
public sealed record AssignLopCommand(AssignLopRequest Request)
    : IRequest<Result<AssignLopResultDto>>;
