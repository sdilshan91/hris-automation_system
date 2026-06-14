using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>Gets a single vacancy by id (tenant-scoped, US-REC-001).</summary>
public sealed record GetVacancyByIdQuery(Guid VacancyId) : IRequest<Result<VacancyDto>>;

public sealed class GetVacancyByIdQueryHandler : IRequestHandler<GetVacancyByIdQuery, Result<VacancyDto>>
{
    private readonly IVacancyService _service;

    public GetVacancyByIdQueryHandler(IVacancyService service) => _service = service;

    public Task<Result<VacancyDto>> Handle(GetVacancyByIdQuery request, CancellationToken cancellationToken)
        => _service.GetByIdAsync(request.VacancyId, cancellationToken);
}
