using FluentValidation;
using HRM.Application.Features.Payroll.Queries;

namespace HRM.Application.Features.Payroll.Validators;

/// <summary>Structural validation for the CTC breakdown preview (US-PAY-002 FR-3).</summary>
public sealed class PreviewCtcBreakdownValidator : AbstractValidator<PreviewCtcBreakdownQuery>
{
    public PreviewCtcBreakdownValidator()
    {
        RuleFor(x => x.SalaryStructureId)
            .NotEmpty().WithMessage("A salary structure reference is required.");

        RuleFor(x => x.AnnualCtc)
            .GreaterThan(0).WithMessage("Annual CTC must be greater than zero.")
            // ISSUE-152: same numeric(18,2) money contract as the assign path — reject >2 decimal places.
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
                .WithMessage("Annual CTC cannot have more than 2 decimal places.")
                .WithErrorCode("invalid_ctc_scale");
    }
}
