using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>Bulk status change for multiple vacancies (US-REC-001 FR-6).</summary>
public sealed record BulkChangeVacancyStatusCommand(IReadOnlyList<Guid> VacancyIds, VacancyStatus Status)
    : IRequest<Result<BulkStatusChangeResult>>;

public sealed class BulkChangeVacancyStatusCommandHandler
    : IRequestHandler<BulkChangeVacancyStatusCommand, Result<BulkStatusChangeResult>>
{
    private readonly IVacancyService _service;

    public BulkChangeVacancyStatusCommandHandler(IVacancyService service) => _service = service;

    public Task<Result<BulkStatusChangeResult>> Handle(BulkChangeVacancyStatusCommand request, CancellationToken cancellationToken)
        => _service.BulkChangeStatusAsync(request.VacancyIds, request.Status, cancellationToken);
}
