using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>
/// US-REC-010 (AC-1/FR-2): returns the pre-fill payload for the "Convert to Employee" form — applicant
/// data + the most recent accepted offer. Fails if the applicant is not eligible (not Hired / no accepted
/// offer). Tenant-scoped.
/// </summary>
public sealed record GetConversionPrefillQuery(Guid ApplicantId) : IRequest<Result<ConversionPrefillDto>>;

public sealed class GetConversionPrefillQueryHandler
    : IRequestHandler<GetConversionPrefillQuery, Result<ConversionPrefillDto>>
{
    private readonly IApplicantConversionService _service;

    public GetConversionPrefillQueryHandler(IApplicantConversionService service) => _service = service;

    public Task<Result<ConversionPrefillDto>> Handle(
        GetConversionPrefillQuery request, CancellationToken cancellationToken)
        => _service.GetPrefillAsync(request.ApplicantId, cancellationToken);
}
