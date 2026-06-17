using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Queries;

/// <summary>US-ONB-005 AC-3/FR-4: gets an offboarding instance with the clearance dashboard data.</summary>
public sealed record GetOffboardingInstanceQuery(Guid OffboardingInstanceId)
    : IRequest<Result<OffboardingInstanceDto>>;

public sealed class GetOffboardingInstanceQueryHandler
    : IRequestHandler<GetOffboardingInstanceQuery, Result<OffboardingInstanceDto>>
{
    private readonly IOffboardingService _service;

    public GetOffboardingInstanceQueryHandler(IOffboardingService service) => _service = service;

    public Task<Result<OffboardingInstanceDto>> Handle(
        GetOffboardingInstanceQuery request, CancellationToken cancellationToken)
        => _service.GetByIdAsync(request.OffboardingInstanceId, cancellationToken);
}
