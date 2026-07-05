using FluentValidation;
using HRM.Application.Features.Tenants.Commands;

namespace HRM.Application.Features.Tenants.Validators;

/// <summary>
/// Validates <see cref="SuspendTenantCommand"/> field shape (US-ADM-004 AC-1/FR-1). The reason is mandatory,
/// 10-500 chars. The allowed-transition check (BR-1) and system-tenant guard (BR-2) need the DB, so they live
/// in the service.
/// </summary>
public sealed class SuspendTenantValidator : AbstractValidator<SuspendTenantCommand>
{
    public SuspendTenantValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("A tenant id is required.");
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A suspension reason is required.")
            .MinimumLength(10).WithMessage("The reason must be at least 10 characters.")
            .MaximumLength(500).WithMessage("The reason cannot exceed 500 characters.");
    }
}

/// <summary>
/// Validates <see cref="TerminateTenantCommand"/> field shape (US-ADM-004 AC-3/FR-2). Reason 10-500 chars;
/// grace period 7-90 days (BR-4).
/// </summary>
public sealed class TerminateTenantValidator : AbstractValidator<TerminateTenantCommand>
{
    public TerminateTenantValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("A tenant id is required.");
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A termination reason is required.")
            .MinimumLength(10).WithMessage("The reason must be at least 10 characters.")
            .MaximumLength(500).WithMessage("The reason cannot exceed 500 characters.");
        // BUG-002: an OMITTED grace period (null) is allowed — the service applies the plan default. A
        // SUPPLIED value is still range-checked 7-90 (BR-4). Kept on the GraceDays selector (not .Value) so
        // the validation-error property path stays "GraceDays".
        RuleFor(x => x.GraceDays)
            .Must(g => g is null or (>= 7 and <= 90))
            .WithMessage("The grace period must be between 7 and 90 days.");
    }
}

/// <summary>Validates <see cref="ReactivateTenantCommand"/> (US-ADM-004 AC-5).</summary>
public sealed class ReactivateTenantValidator : AbstractValidator<ReactivateTenantCommand>
{
    public ReactivateTenantValidator()
        => RuleFor(x => x.TenantId).NotEmpty().WithMessage("A tenant id is required.");
}

/// <summary>Validates <see cref="RestoreTenantCommand"/> (US-ADM-004 AC-6).</summary>
public sealed class RestoreTenantValidator : AbstractValidator<RestoreTenantCommand>
{
    public RestoreTenantValidator()
        => RuleFor(x => x.TenantId).NotEmpty().WithMessage("A tenant id is required.");
}
