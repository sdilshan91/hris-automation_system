using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>Creates a vacancy in Draft status (US-REC-001 AC-1).</summary>
public sealed record CreateVacancyCommand(
    string Title,
    Guid? DepartmentId,
    Guid? JobTitleId,
    Guid? LocationId,
    Guid? HiringManagerId,
    EmploymentType EmploymentType,
    int? Headcount,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryCurrency,
    string Description,
    string? Qualifications,
    DateOnly? ApplicationDeadline,
    bool PublishToPublicCareers
) : IRequest<Result<VacancyDto>>;

public sealed class CreateVacancyCommandHandler : IRequestHandler<CreateVacancyCommand, Result<VacancyDto>>
{
    private readonly IVacancyService _service;

    public CreateVacancyCommandHandler(IVacancyService service) => _service = service;

    public Task<Result<VacancyDto>> Handle(CreateVacancyCommand request, CancellationToken cancellationToken)
        => _service.CreateAsync(new VacancyInput(
            request.Title, request.DepartmentId, request.JobTitleId, request.LocationId,
            request.HiringManagerId, request.EmploymentType, request.Headcount!.Value,
            request.SalaryMin, request.SalaryMax, request.SalaryCurrency,
            request.Description, request.Qualifications, request.ApplicationDeadline,
            request.PublishToPublicCareers), cancellationToken);
}
