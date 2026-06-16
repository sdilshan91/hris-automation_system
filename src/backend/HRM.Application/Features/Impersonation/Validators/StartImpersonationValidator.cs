using FluentValidation;
using HRM.Application.Features.Impersonation.Commands;

namespace HRM.Application.Features.Impersonation.Validators;

/// <summary>
/// Validates <see cref="StartImpersonationCommand"/> field shape (US-ADM-003 FR-1/BR-4). The reason is mandatory
/// and must be 10–500 characters; the target ids must be non-empty. Cross-entity rules (target tenant exists /
/// not terminated, target membership active, target is not a system-admin, one-active-session-per-impersonator)
/// need the database and live in <see cref="HRM.Application.Common.Interfaces.IImpersonationService"/>, mirroring
/// the US-ADM-001 split (validators in this codebase do not touch the DbContext).
/// </summary>
public sealed class StartImpersonationValidator : AbstractValidator<StartImpersonationCommand>
{
    public StartImpersonationValidator()
    {
        RuleFor(x => x.TargetUserId)
            .NotEmpty().WithMessage("A target user is required.");

        RuleFor(x => x.TargetTenantId)
            .NotEmpty().WithMessage("A target tenant is required.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required.")
            .MinimumLength(10).WithMessage("The reason must be at least 10 characters.")
            .MaximumLength(500).WithMessage("The reason cannot exceed 500 characters.");
    }
}
