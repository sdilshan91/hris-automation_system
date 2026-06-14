using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>Lists vacancies (paged, filter by status/department) for the current tenant (US-REC-001 NFR-1).</summary>
public sealed record ListVacanciesQuery(
    VacancyStatus? Status,
    Guid? DepartmentId,
    string? Search = null,
    int Page = 1,
    int PageSize = 20
) : IRequest<Result<PagedResult<VacancyListItemDto>>>;

public sealed class ListVacanciesQueryHandler
    : IRequestHandler<ListVacanciesQuery, Result<PagedResult<VacancyListItemDto>>>
{
    private readonly IVacancyService _service;

    public ListVacanciesQueryHandler(IVacancyService service) => _service = service;

    public Task<Result<PagedResult<VacancyListItemDto>>> Handle(ListVacanciesQuery request, CancellationToken cancellationToken)
        => _service.ListAsync(request.Status, request.DepartmentId, request.Search, request.Page, request.PageSize, cancellationToken);
}
