using System.Text.RegularExpressions;
using FluentValidation;
using HRM.Application.Features.Tenants.Commands;

namespace HRM.Application.Features.Tenants.Validators;

/// <summary>
/// Validates <see cref="ProvisionTenantCommand"/> field shape (US-ADM-001 AC-2/AC-5/FR-1).
///
/// SCOPE: these are the rules verifiable from the command's fields alone — name/email shape, the subdomain
/// FORMAT + RESERVED-list rule (AC-2/AC-5), a non-empty plan id and non-negative trial days. The rules that
/// need the database — subdomain ALREADY-TAKEN uniqueness (AC-2/BR-2) and the plan being ACTIVE — cannot be
/// checked from a single command, so they are enforced in <see cref="HRM.Application.Common.Interfaces.ITenantProvisioningService"/>
/// against the persisted rows. Putting them here would be a false guarantee (and validators in this codebase
/// do not touch the DbContext / configuration). The reserved-subdomain list is the AC-2 enumerated set; the
/// service additionally honours the configured <c>Platform:ReservedSubdomains</c> override (same source the
/// TenantResolutionMiddleware uses) as the authoritative server-side guard.
/// </summary>
public sealed class ProvisionTenantValidator : AbstractValidator<ProvisionTenantCommand>
{
    // Lowercase alphanumeric + hyphens; must start and end with an alphanumeric. Single-char ("abc"→ok,
    // "a"→fails length) handled by the length rule. Mirrors the tenants table check constraint.
    private static readonly Regex SubdomainFormat = new(
        "^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", RegexOptions.Compiled);

    // AC-2 reserved subdomains.
    private static readonly HashSet<string> ReservedSubdomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "api", "admin", "app", "mail", "status", "docs", "help", "support",
        "static", "cdn", "dev", "stage", "prod", "test", "qa"
    };

    public ProvisionTenantValidator()
    {
        var reserved = ReservedSubdomains;

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Organization name is required.")
            .MaximumLength(200).WithMessage("Organization name cannot exceed 200 characters.");

        RuleFor(x => x.Subdomain)
            .NotEmpty().WithMessage("Subdomain is required.")
            .MinimumLength(3).WithMessage("Subdomain must be at least 3 characters.")
            .MaximumLength(50).WithMessage("Subdomain cannot exceed 50 characters.")
            .Must(s => s is not null && SubdomainFormat.IsMatch(s))
                .WithMessage("Subdomain may contain only lowercase letters, numbers, and hyphens, and must start and end with a letter or number.")
            .Must(s => s is null || !reserved.Contains(s))
                .WithMessage("Subdomain is unavailable.");

        RuleFor(x => x.OwnerEmail)
            .NotEmpty().WithMessage("Owner email is required.")
            .EmailAddress().WithMessage("Owner email must be a valid email address.")
            .MaximumLength(150).WithMessage("Owner email cannot exceed 150 characters.");

        RuleFor(x => x.SubscriptionPlanId)
            .NotEmpty().WithMessage("A subscription plan is required.");

        RuleFor(x => x.Region)
            .NotEmpty().WithMessage("Region is required.")
            .MaximumLength(100).WithMessage("Region cannot exceed 100 characters.");

        RuleFor(x => x.TrialDays)
            .GreaterThanOrEqualTo(0).When(x => x.TrialDays.HasValue)
            .WithMessage("Trial days cannot be negative.");
    }
}
