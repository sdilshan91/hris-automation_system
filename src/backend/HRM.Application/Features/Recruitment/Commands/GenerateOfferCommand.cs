using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>
/// US-REC-007 (AC-1/FR-1/FR-2/FR-9/BR-2/BR-6): generates an offer letter for an applicant. Supersedes any
/// prior active offer for the applicant/vacancy (BR-2, version bump FR-9), renders + stores the PDF, and
/// saves it as Draft. Tenant-scoped. Expiry defaults to generation date + 7 days when omitted (BR-6).
/// </summary>
public sealed record GenerateOfferCommand(
    Guid ApplicantId,
    string OfferedPosition,
    Guid? DepartmentId,
    Guid? ReportingManagerEmployeeId,
    decimal SalaryAmount,
    string Currency,
    SalaryFrequency SalaryFrequency,
    string? BenefitsSummary,
    DateOnly StartDate,
    DateOnly? ExpiryDate,
    int? ProbationMonths,
    string? CustomClauses
) : IRequest<Result<OfferDto>>;

public sealed class GenerateOfferCommandHandler
    : IRequestHandler<GenerateOfferCommand, Result<OfferDto>>
{
    private readonly IOfferService _service;

    public GenerateOfferCommandHandler(IOfferService service) => _service = service;

    public Task<Result<OfferDto>> Handle(GenerateOfferCommand request, CancellationToken cancellationToken)
        => _service.GenerateAsync(new GenerateOfferInput
        {
            ApplicantId = request.ApplicantId,
            OfferedPosition = request.OfferedPosition,
            DepartmentId = request.DepartmentId,
            ReportingManagerEmployeeId = request.ReportingManagerEmployeeId,
            SalaryAmount = request.SalaryAmount,
            Currency = request.Currency,
            SalaryFrequency = request.SalaryFrequency,
            BenefitsSummary = request.BenefitsSummary,
            StartDate = request.StartDate,
            ExpiryDate = request.ExpiryDate,
            ProbationMonths = request.ProbationMonths,
            CustomClauses = request.CustomClauses,
        }, cancellationToken);
}
