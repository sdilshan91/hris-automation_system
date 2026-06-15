using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Payroll.Queries;

/// <summary>Gets a single salary component by id (tenant-scoped).</summary>
public sealed record GetSalaryComponentByIdQuery(Guid ComponentId) : IRequest<Result<SalaryComponentDto>>;

public sealed class GetSalaryComponentByIdQueryHandler : IRequestHandler<GetSalaryComponentByIdQuery, Result<SalaryComponentDto>>
{
    private readonly ISalaryComponentService _service;
    public GetSalaryComponentByIdQueryHandler(ISalaryComponentService service) => _service = service;

    public Task<Result<SalaryComponentDto>> Handle(GetSalaryComponentByIdQuery request, CancellationToken cancellationToken)
        => _service.GetByIdAsync(request.ComponentId, cancellationToken);
}

/// <summary>Lists salary components for the tenant, paged + filterable (NFR-3).</summary>
public sealed record ListSalaryComponentsQuery(
    SalaryComponentType? Type,
    bool? IsActive,
    string? Search,
    int Page = 1,
    int PageSize = 25
) : IRequest<Result<PagedResult<SalaryComponentListItemDto>>>;

public sealed class ListSalaryComponentsQueryHandler : IRequestHandler<ListSalaryComponentsQuery, Result<PagedResult<SalaryComponentListItemDto>>>
{
    private readonly ISalaryComponentService _service;
    public ListSalaryComponentsQueryHandler(ISalaryComponentService service) => _service = service;

    public Task<Result<PagedResult<SalaryComponentListItemDto>>> Handle(ListSalaryComponentsQuery request, CancellationToken cancellationToken)
        => _service.ListAsync(request.Type, request.IsActive, request.Search, request.Page, request.PageSize, cancellationToken);
}
