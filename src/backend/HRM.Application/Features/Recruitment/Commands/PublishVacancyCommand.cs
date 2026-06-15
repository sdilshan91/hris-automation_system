using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>Publishes a Draft vacancy (Draft → Open, US-REC-001 AC-2).</summary>
public sealed record PublishVacancyCommand(Guid VacancyId) : IRequest<Result<VacancyDto>>;

public sealed class PublishVacancyCommandHandler : IRequestHandler<PublishVacancyCommand, Result<VacancyDto>>
{
    private readonly IVacancyService _service;

    public PublishVacancyCommandHandler(IVacancyService service) => _service = service;

    public Task<Result<VacancyDto>> Handle(PublishVacancyCommand request, CancellationToken cancellationToken)
        => _service.PublishAsync(request.VacancyId, cancellationToken);
}
