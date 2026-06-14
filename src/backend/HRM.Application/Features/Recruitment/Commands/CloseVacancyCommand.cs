using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>Closes an Open/OnHold vacancy (US-REC-001 AC-5).</summary>
public sealed record CloseVacancyCommand(Guid VacancyId) : IRequest<Result<VacancyDto>>;

public sealed class CloseVacancyCommandHandler : IRequestHandler<CloseVacancyCommand, Result<VacancyDto>>
{
    private readonly IVacancyService _service;

    public CloseVacancyCommandHandler(IVacancyService service) => _service = service;

    public Task<Result<VacancyDto>> Handle(CloseVacancyCommand request, CancellationToken cancellationToken)
        => _service.CloseAsync(request.VacancyId, cancellationToken);
}
