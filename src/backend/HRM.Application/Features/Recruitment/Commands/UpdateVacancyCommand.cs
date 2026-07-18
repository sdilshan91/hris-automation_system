using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>Updates an existing vacancy (US-REC-001 AC-3).</summary>
public sealed record UpdateVacancyCommand(
    Guid VacancyId,
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

public sealed class UpdateVacancyCommandHandler : IRequestHandler<UpdateVacancyCommand, Result<VacancyDto>>
{
    private readonly IVacancyService _service;

    public UpdateVacancyCommandHandler(IVacancyService service) => _service = service;

    public Task<Result<VacancyDto>> Handle(UpdateVacancyCommand request, CancellationToken cancellationToken)
        => _service.UpdateAsync(request.VacancyId, new VacancyInput(
            request.Title, request.DepartmentId, request.JobTitleId, request.LocationId,
            request.HiringManagerId, request.EmploymentType, request.Headcount!.Value,
            request.SalaryMin, request.SalaryMax, request.SalaryCurrency,
            request.Description, request.Qualifications, request.ApplicationDeadline,
            request.PublishToPublicCareers), cancellationToken);
}
