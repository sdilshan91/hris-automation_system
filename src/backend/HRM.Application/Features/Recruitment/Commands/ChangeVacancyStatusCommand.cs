using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>Changes a vacancy's status with FR-2 transition validation (US-REC-001).</summary>
public sealed record ChangeVacancyStatusCommand(Guid VacancyId, VacancyStatus Status)
    : IRequest<Result<VacancyDto>>;

public sealed class ChangeVacancyStatusCommandHandler
    : IRequestHandler<ChangeVacancyStatusCommand, Result<VacancyDto>>
{
    private readonly IVacancyService _service;

    public ChangeVacancyStatusCommandHandler(IVacancyService service) => _service = service;

    public Task<Result<VacancyDto>> Handle(ChangeVacancyStatusCommand request, CancellationToken cancellationToken)
        => _service.ChangeStatusAsync(request.VacancyId, request.Status, cancellationToken);
}
