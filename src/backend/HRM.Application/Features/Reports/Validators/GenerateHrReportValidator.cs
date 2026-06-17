using FluentValidation;
using HRM.Application.Features.Reports.DTOs;
using HRM.Application.Features.Reports.Queries;
using HRM.Domain.Enums;

namespace HRM.Application.Features.Reports.Validators;

/// <summary>
/// Structural validation for an HR report request (US-RPT-001 FR-2 / §7):
///  - type must be a known KEBAB report key (e.g. "headcount", "joiners-leavers"),
///  - the date window must be coherent (from &lt;= to),
///  - any supplied employmentTypes / employeeStatuses filter values must be valid enum names.
/// The existence of departmentIds / locationIds in the tenant is naturally enforced by the EF global query
/// filter (an unknown id simply matches no rows), so no DB round-trip is needed here.
/// </summary>
public sealed class GenerateHrReportValidator : AbstractValidator<GenerateHrReportQuery>
{
    public GenerateHrReportValidator()
    {
        RuleFor(x => x.Type)
            .Must(t => HrReportTypeKey.TryParse(t, out _))
            .WithMessage("Unknown report type.");

        RuleFor(x => x.QueryParams)
            .NotNull();

        When(x => x.QueryParams is not null, () =>
        {
            RuleFor(x => x.QueryParams.DateFrom)
                .LessThanOrEqualTo(x => x.QueryParams.DateTo)
                .When(x => x.QueryParams.DateFrom.HasValue && x.QueryParams.DateTo.HasValue)
                .WithMessage("dateFrom must be on or before dateTo.");

            RuleForEach(x => x.QueryParams.EmploymentTypes)
                .Must(v => Enum.TryParse<EmploymentType>(v, ignoreCase: true, out _))
                .WithMessage("Invalid employment type filter value.");

            RuleForEach(x => x.QueryParams.EmployeeStatuses)
                .Must(v => Enum.TryParse<EmployeeStatus>(v, ignoreCase: true, out _))
                .WithMessage("Invalid employee status filter value.");
        });
    }
}
