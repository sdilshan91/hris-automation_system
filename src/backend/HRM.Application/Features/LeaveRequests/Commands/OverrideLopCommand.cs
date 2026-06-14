using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Commands;

/// <summary>
/// HR overrides a system-generated LOP entry (US-LV-011 BR-3): converts it to a different
/// balance-backed leave type or removes it. Delegates to <c>ILopService.OverrideLopAsync</c>.
/// </summary>
public sealed record OverrideLopCommand(Guid LeaveRequestId, OverrideLopRequest Request)
    : IRequest<Result<OverrideLopResultDto>>;
