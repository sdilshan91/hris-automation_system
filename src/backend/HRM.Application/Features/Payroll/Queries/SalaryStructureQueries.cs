using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.DTOs;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Queries;

/// <summary>Gets a single salary structure (with components) by id (tenant-scoped).</summary>
public sealed record GetSalaryStructureByIdQuery(Guid StructureId) : IRequest<Result<SalaryStructureDto>>;

public sealed class GetSalaryStructureByIdQueryHandler : IRequestHandler<GetSalaryStructureByIdQuery, Result<SalaryStructureDto>>
{
    private readonly ISalaryStructureService _service;
    public GetSalaryStructureByIdQueryHandler(ISalaryStructureService service) => _service = service;

    public Task<Result<SalaryStructureDto>> Handle(GetSalaryStructureByIdQuery request, CancellationToken cancellationToken)
        => _service.GetByIdAsync(request.StructureId, cancellationToken);
}

/// <summary>Lists salary structures for the tenant, paged + filterable (NFR-3).</summary>
public sealed record ListSalaryStructuresQuery(
    bool? IsActive,
    string? Search,
    int Page = 1,
    int PageSize = 25
) : IRequest<Result<PagedResult<SalaryStructureListItemDto>>>;

public sealed class ListSalaryStructuresQueryHandler : IRequestHandler<ListSalaryStructuresQuery, Result<PagedResult<SalaryStructureListItemDto>>>
{
    private readonly ISalaryStructureService _service;
    public ListSalaryStructuresQueryHandler(ISalaryStructureService service) => _service = service;

    public Task<Result<PagedResult<SalaryStructureListItemDto>>> Handle(ListSalaryStructuresQuery request, CancellationToken cancellationToken)
        => _service.ListAsync(request.IsActive, request.Search, request.Page, request.PageSize, cancellationToken);
}
