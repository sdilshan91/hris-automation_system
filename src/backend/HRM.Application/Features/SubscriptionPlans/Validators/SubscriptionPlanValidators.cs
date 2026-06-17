using System.Text.RegularExpressions;
using FluentValidation;
using HRM.Application.Features.SubscriptionPlans.Commands;
using HRM.Application.Features.SubscriptionPlans.DTOs;
using HRM.Domain.Authorization;

namespace HRM.Application.Features.SubscriptionPlans.Validators;

/// <summary>
/// Field-shape validation shared by plan create/update (US-ADM-009 FR-2/FR-6). The DB-dependent rules — code
/// UNIQUENESS (FR-3) and the referenced-tenant guards — live in the service against persisted rows; validators
/// in this codebase never touch the DbContext. The code FORMAT rule (lowercase-alphanumeric-hyphens) is on the
/// create command only, since the code is immutable thereafter (FR-3/BR-5).
/// </summary>
internal static class PlanFieldRules
{
    /// <summary>Lowercase alphanumeric + hyphens; must start and end with an alphanumeric (US-ADM-009 FR-3).</summary>
    public static readonly Regex CodeFormat = new(
        "^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", RegexOptions.Compiled);

    public static void ApplyEditableRules<T>(AbstractValidator<T> v, Func<T, PlanEditableFields> selector)
    {
        v.RuleFor(x => selector(x).Name)
            .NotEmpty().WithMessage("Plan name is required.")
            .MaximumLength(100).WithMessage("Plan name cannot exceed 100 characters.");

        v.RuleFor(x => selector(x).Description)
            .MaximumLength(2000).WithMessage("Description cannot exceed 2000 characters.");

        v.RuleFor(x => selector(x).PriceMonthly)
            .GreaterThanOrEqualTo(0m).WithMessage("Monthly price cannot be negative.");

        v.RuleFor(x => selector(x).PriceYearly)
            .GreaterThanOrEqualTo(0m).WithMessage("Yearly price cannot be negative.");

        v.RuleFor(x => selector(x).Currency)
            .NotEmpty().WithMessage("Currency is required.")
            .Length(3).WithMessage("Currency must be a 3-letter ISO-4217 code.");

        v.RuleFor(x => selector(x).TrialDays)
            .GreaterThanOrEqualTo(0).WithMessage("Trial days cannot be negative.");

        v.RuleFor(x => selector(x).AuditLogRetentionDays)
            .GreaterThanOrEqualTo(0).WithMessage("Audit-log retention days cannot be negative.");

        v.RuleFor(x => selector(x).SlaTier)
            .NotEmpty().WithMessage("SLA tier is required.")
            .MaximumLength(50).WithMessage("SLA tier cannot exceed 50 characters.");

        // Non-negative limits when supplied (null = unlimited, which is allowed).
        v.RuleFor(x => selector(x).MaxEmployees).GreaterThanOrEqualTo(0)
            .When(x => selector(x).MaxEmployees.HasValue).WithMessage("Max employees cannot be negative.");
        v.RuleFor(x => selector(x).MaxStorageGb).GreaterThanOrEqualTo(0)
            .When(x => selector(x).MaxStorageGb.HasValue).WithMessage("Max storage cannot be negative.");
        v.RuleFor(x => selector(x).MaxApiCallsPerMonth).GreaterThanOrEqualTo(0)
            .When(x => selector(x).MaxApiCallsPerMonth.HasValue).WithMessage("Max API calls cannot be negative.");
        v.RuleFor(x => selector(x).MaxEmailSendsPerMonth).GreaterThanOrEqualTo(0)
            .When(x => selector(x).MaxEmailSendsPerMonth.HasValue).WithMessage("Max email sends cannot be negative.");
        v.RuleFor(x => selector(x).MaxCustomRoles).GreaterThanOrEqualTo(0)
            .When(x => selector(x).MaxCustomRoles.HasValue).WithMessage("Max custom roles cannot be negative.");
        v.RuleFor(x => selector(x).MaxCustomFieldsPerEntity).GreaterThanOrEqualTo(0)
            .When(x => selector(x).MaxCustomFieldsPerEntity.HasValue).WithMessage("Max custom fields cannot be negative.");
        v.RuleFor(x => selector(x).MaxWorkflows).GreaterThanOrEqualTo(0)
            .When(x => selector(x).MaxWorkflows.HasValue).WithMessage("Max workflows cannot be negative.");

        // Every enabled module must be a canonical key (US-ADM-009 FR-6).
        v.RuleFor(x => selector(x).EnabledModules)
            .NotNull().WithMessage("Enabled modules are required.")
            .Must(modules => modules is not null && modules.All(PlanModules.IsValid))
                .WithMessage("Enabled modules must reference only canonical module keys.");
    }
}

/// <summary>Validates <see cref="CreatePlanCommand"/> (US-ADM-009 AC-2/FR-2/FR-3/FR-6).</summary>
public sealed class CreatePlanCommandValidator : AbstractValidator<CreatePlanCommand>
{
    public CreatePlanCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Plan code is required.")
            .MaximumLength(64).WithMessage("Plan code cannot exceed 64 characters.")
            .Must(c => c is not null && PlanFieldRules.CodeFormat.IsMatch(c))
                .WithMessage("Plan code may contain only lowercase letters, numbers, and hyphens, and must start and end with a letter or number.");

        PlanFieldRules.ApplyEditableRules(this, x => x.Fields);
    }
}

/// <summary>Validates <see cref="UpdatePlanCommand"/> (US-ADM-009 AC-3). The code is immutable and not present here.</summary>
public sealed class UpdatePlanCommandValidator : AbstractValidator<UpdatePlanCommand>
{
    public UpdatePlanCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Plan id is required.");
        PlanFieldRules.ApplyEditableRules(this, x => x.Fields);
    }
}

/// <summary>Validates <see cref="UpsertPlanLimitOverrideCommand"/> (US-ADM-009 AC-5/FR-4).</summary>
public sealed class UpsertPlanLimitOverrideCommandValidator : AbstractValidator<UpsertPlanLimitOverrideCommand>
{
    public UpsertPlanLimitOverrideCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("Tenant id is required.");

        RuleFor(x => x.LimitKey)
            .NotEmpty().WithMessage("Limit key is required.")
            .Must(PlanLimitKeys.IsValid).WithMessage("Limit key must be a recognized plan-limit key.");

        // null value = unlimited override (allowed); a present value must be non-negative.
        RuleFor(x => x.Value).GreaterThanOrEqualTo(0)
            .When(x => x.Value.HasValue).WithMessage("Override value cannot be negative.");
    }
}
