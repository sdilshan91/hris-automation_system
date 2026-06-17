using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Queries;

/// <summary>US-ONB-005: gets an employee's current offboarding instance (or 404).</summary>
public sealed record GetOffboardingByEmployeeQuery(Guid EmployeeId)
    : IRequest<Result<OffboardingInstanceDto>>;

public sealed class GetOffboardingByEmployeeQueryHandler
    : IRequestHandler<GetOffboardingByEmployeeQuery, Result<OffboardingInstanceDto>>
{
    private readonly IOffboardingService _service;

    public GetOffboardingByEmployeeQueryHandler(IOffboardingService service) => _service = service;

    public Task<Result<OffboardingInstanceDto>> Handle(
        GetOffboardingByEmployeeQuery request, CancellationToken cancellationToken)
        => _service.GetByEmployeeAsync(request.EmployeeId, cancellationToken);
}
