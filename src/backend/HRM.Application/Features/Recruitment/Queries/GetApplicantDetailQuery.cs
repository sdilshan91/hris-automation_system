using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>
/// US-REC-003 (AC-3/FR-7): returns the full applicant detail for the slide-over panel — profile + stage
/// transition timeline + resume download reference. Interview data deferred (US-REC-005/006). Tenant-scoped.
/// </summary>
public sealed record GetApplicantDetailQuery(Guid ApplicantId) : IRequest<Result<ApplicantDetailDto>>;

public sealed class GetApplicantDetailQueryHandler
    : IRequestHandler<GetApplicantDetailQuery, Result<ApplicantDetailDto>>
{
    private readonly IApplicantService _service;

    public GetApplicantDetailQueryHandler(IApplicantService service) => _service = service;

    public Task<Result<ApplicantDetailDto>> Handle(GetApplicantDetailQuery request, CancellationToken cancellationToken)
        => _service.GetDetailAsync(request.ApplicantId, cancellationToken);
}
