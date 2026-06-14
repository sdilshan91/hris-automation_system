using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>
/// US-ATT-008 FR-4: reads the tenant late-arrival policy (defaults when none configured).
/// </summary>
public sealed record GetLatePolicyQuery : IRequest<Result<LatePolicyDto>>;

public sealed class GetLatePolicyQueryHandler
    : IRequestHandler<GetLatePolicyQuery, Result<LatePolicyDto>>
{
    private readonly ILateEarlyService _service;

    public GetLatePolicyQueryHandler(ILateEarlyService service)
    {
        _service = service;
    }

    public Task<Result<LatePolicyDto>> Handle(GetLatePolicyQuery request, CancellationToken cancellationToken)
        => _service.GetPolicyAsync(cancellationToken);
}
