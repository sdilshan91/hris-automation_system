using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.DTOs;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>Lists applicants for a vacancy (recruiter-facing, paged, tenant-scoped, US-REC-002).</summary>
public sealed record ListApplicantsByVacancyQuery(
    Guid VacancyId,
    int Page = 1,
    int PageSize = 20
) : IRequest<Result<PagedResult<ApplicantListItemDto>>>;

public sealed class ListApplicantsByVacancyQueryHandler
    : IRequestHandler<ListApplicantsByVacancyQuery, Result<PagedResult<ApplicantListItemDto>>>
{
    private readonly IApplicantService _service;

    public ListApplicantsByVacancyQueryHandler(IApplicantService service) => _service = service;

    public Task<Result<PagedResult<ApplicantListItemDto>>> Handle(ListApplicantsByVacancyQuery request, CancellationToken cancellationToken)
        => _service.ListByVacancyAsync(request.VacancyId, request.Page, request.PageSize, cancellationToken);
}
