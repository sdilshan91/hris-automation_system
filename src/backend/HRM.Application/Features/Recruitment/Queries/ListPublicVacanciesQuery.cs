using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>Anonymous public-careers listing of Open vacancies for the resolved tenant (US-REC-001 FR-4).</summary>
public sealed record ListPublicVacanciesQuery() : IRequest<Result<IReadOnlyList<PublicVacancyListItemDto>>>;

public sealed class ListPublicVacanciesQueryHandler
    : IRequestHandler<ListPublicVacanciesQuery, Result<IReadOnlyList<PublicVacancyListItemDto>>>
{
    private readonly IPublicCareersService _service;

    public ListPublicVacanciesQueryHandler(IPublicCareersService service) => _service = service;

    public Task<Result<IReadOnlyList<PublicVacancyListItemDto>>> Handle(ListPublicVacanciesQuery request, CancellationToken cancellationToken)
        => _service.ListOpenAsync(cancellationToken);
}

/// <summary>Anonymous public-careers detail by slug for the resolved tenant (US-REC-001 FR-5).</summary>
public sealed record GetPublicVacancyBySlugQuery(string Slug) : IRequest<Result<PublicVacancyDetailDto>>;

public sealed class GetPublicVacancyBySlugQueryHandler
    : IRequestHandler<GetPublicVacancyBySlugQuery, Result<PublicVacancyDetailDto>>
{
    private readonly IPublicCareersService _service;

    public GetPublicVacancyBySlugQueryHandler(IPublicCareersService service) => _service = service;

    public Task<Result<PublicVacancyDetailDto>> Handle(GetPublicVacancyBySlugQuery request, CancellationToken cancellationToken)
        => _service.GetBySlugAsync(request.Slug, cancellationToken);
}
