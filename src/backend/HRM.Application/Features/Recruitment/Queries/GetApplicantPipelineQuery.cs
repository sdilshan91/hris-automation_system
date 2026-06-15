using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>
/// US-REC-003 (AC-1/FR-1/FR-5/FR-6): returns the Kanban pipeline board for a vacancy, grouped by stage
/// with per-stage counts + total, after applying the optional filter. Tenant-scoped (AC-5).
/// </summary>
public sealed record GetApplicantPipelineQuery(
    Guid VacancyId,
    PipelineFilter Filter
) : IRequest<Result<ApplicantPipelineBoardDto>>;

public sealed class GetApplicantPipelineQueryHandler
    : IRequestHandler<GetApplicantPipelineQuery, Result<ApplicantPipelineBoardDto>>
{
    private readonly IApplicantService _service;

    public GetApplicantPipelineQueryHandler(IApplicantService service) => _service = service;

    public Task<Result<ApplicantPipelineBoardDto>> Handle(GetApplicantPipelineQuery request, CancellationToken cancellationToken)
        => _service.GetPipelineBoardAsync(request.VacancyId, request.Filter, cancellationToken);
}
