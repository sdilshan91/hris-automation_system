using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>
/// US-REC-010 (AC-2/AC-4/FR-1/FR-4/FR-6/FR-7/FR-10/BR-2/BR-3/BR-5/NFR-3): converts an accepted applicant
/// into a Core HR employee record. Pre-filled defaults come from the applicant + accepted offer; the HR
/// Officer supplies the structured job title, employment type, location, optional manager, and optionally a
/// manual employee number. The conversion is atomic (NFR-3) and reuses the Core HR employee-create path.
/// Tenant-scoped (AC-5).
/// </summary>
public sealed record ConvertApplicantToEmployeeCommand(
    Guid ApplicantId,
    Guid JobTitleId,
    Guid DepartmentId,
    EmploymentType EmploymentType,
    DateTime DateOfJoining,
    Guid? ReportsToEmployeeId,
    Guid? LocationId,
    string? EmployeeNo,
    DateTime? DateOfBirth,
    Gender? Gender
) : IRequest<Result<ConversionResultDto>>;

public sealed class ConvertApplicantToEmployeeCommandHandler
    : IRequestHandler<ConvertApplicantToEmployeeCommand, Result<ConversionResultDto>>
{
    private readonly IApplicantConversionService _service;

    public ConvertApplicantToEmployeeCommandHandler(IApplicantConversionService service) => _service = service;

    public Task<Result<ConversionResultDto>> Handle(
        ConvertApplicantToEmployeeCommand request, CancellationToken cancellationToken)
        => _service.ConvertAsync(new ConvertApplicantToEmployeeInput
        {
            ApplicantId = request.ApplicantId,
            JobTitleId = request.JobTitleId,
            DepartmentId = request.DepartmentId,
            EmploymentType = request.EmploymentType,
            DateOfJoining = request.DateOfJoining,
            ReportsToEmployeeId = request.ReportsToEmployeeId,
            LocationId = request.LocationId,
            EmployeeNo = request.EmployeeNo,
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender,
        }, cancellationToken);
}
