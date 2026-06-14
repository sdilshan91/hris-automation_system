using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>
/// US-ATT-008 FR-4: upserts the tenant late-arrival policy (HR).
/// </summary>
public sealed record UpsertLatePolicyCommand(LatePolicyDto Policy) : IRequest<Result<LatePolicyDto>>;

public sealed class UpsertLatePolicyCommandHandler
    : IRequestHandler<UpsertLatePolicyCommand, Result<LatePolicyDto>>
{
    private readonly ILateEarlyService _service;

    public UpsertLatePolicyCommandHandler(ILateEarlyService service)
    {
        _service = service;
    }

    public Task<Result<LatePolicyDto>> Handle(UpsertLatePolicyCommand request, CancellationToken cancellationToken)
        => _service.UpsertPolicyAsync(request.Policy, cancellationToken);
}
