using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>Gets a single applicant by id (recruiter-facing, tenant-scoped, US-REC-002).</summary>
public sealed record GetApplicantByIdQuery(Guid ApplicantId) : IRequest<Result<ApplicantDto>>;

public sealed class GetApplicantByIdQueryHandler : IRequestHandler<GetApplicantByIdQuery, Result<ApplicantDto>>
{
    private readonly IApplicantService _service;

    public GetApplicantByIdQueryHandler(IApplicantService service) => _service = service;

    public Task<Result<ApplicantDto>> Handle(GetApplicantByIdQuery request, CancellationToken cancellationToken)
        => _service.GetByIdAsync(request.ApplicantId, cancellationToken);
}
