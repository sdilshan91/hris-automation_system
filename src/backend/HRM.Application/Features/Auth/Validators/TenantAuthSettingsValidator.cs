using FluentValidation;
using HRM.Application.Common.Helpers;
using HRM.Application.Features.Auth.Commands;
using HRM.Domain.Authorization;

namespace HRM.Application.Features.Auth.Validators;

/// <summary>
/// Validates tenant auth settings update command.
/// MfaPolicy must be one of: off, optional, required.
/// MfaRequiredRoles must have non-empty role names when policy is "required".
///
/// <para>US-AUTH-012: adds the stateless SSO input rules (FR-4/FR-5/BR-5). GUID/domain format, the enforcement
/// mode enum, the privilege-ceiling role-name check, and the empty-allow-list block are enforced here. The
/// DB-dependent SSO rules — plan entitlement (403 sso_not_entitled), "role exists in this tenant", the merged
/// effective-state allow-list guard, and the break-glass precondition for sso_only — are enforced in
/// <c>AuthService.UpdateTenantAuthSettingsAsync</c> where the tenant/plan/role rows are available.</para>
/// </summary>
public sealed class TenantAuthSettingsValidator : AbstractValidator<UpdateTenantAuthSettingsCommand>
{
    private static readonly string[] ValidPolicies = ["off", "optional", "required"];

    /// <summary>NFR-4 requires ≥20 entries each; cap generously to bound abuse of the jsonb columns.</summary>
    private const int MaxListEntries = 100;

    public TenantAuthSettingsValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("Tenant ID is required.");

        RuleFor(x => x.Request)
            .NotNull().WithMessage("Request body is required.");

        RuleFor(x => x.Request.MfaPolicy)
            .NotEmpty().WithMessage("MFA policy is required.")
            .Must(p => ValidPolicies.Contains(p))
            .WithMessage("MFA policy must be one of: off, optional, required.");

        When(x => x.Request.MfaPolicy == "required", () =>
        {
            RuleFor(x => x.Request.MfaRequiredRoles)
                .NotEmpty().WithMessage("At least one role must be specified when MFA policy is 'required'.")
                .ForEach(role =>
                {
                    role.NotEmpty().WithMessage("Role name cannot be empty.");
                });
        });

        // ── US-AUTH-012 SSO input rules (stateless) ───────────────────────────

        // FR-4: every provided Entra directory (tenant) ID must be a well-formed GUID.
        When(x => x.Request.AllowedEntraTenantIds is not null, () =>
        {
            RuleFor(x => x.Request.AllowedEntraTenantIds!)
                .Must(list => list.Count <= MaxListEntries)
                .WithMessage($"At most {MaxListEntries} Entra directory IDs are allowed.");

            RuleForEach(x => x.Request.AllowedEntraTenantIds!)
                .Must(tid => Guid.TryParse(tid, out _))
                .WithMessage("Each Entra directory (tenant) ID must be a valid GUID.");
        });

        // FR-4: every provided email domain must be syntactically valid.
        When(x => x.Request.AllowedEmailDomains is not null, () =>
        {
            RuleFor(x => x.Request.AllowedEmailDomains!)
                .Must(list => list.Count <= MaxListEntries)
                .WithMessage($"At most {MaxListEntries} email domains are allowed.");

            RuleForEach(x => x.Request.AllowedEmailDomains!)
                .Must(DomainNameValidator.IsValid)
                .WithMessage("Each allowed email domain must be a valid domain name (e.g. contoso.com).");
        });

        // FR-5/BR-6: enforcement mode must be a known value when supplied.
        When(x => x.Request.EnforcementMode is not null, () =>
        {
            RuleFor(x => x.Request.EnforcementMode!)
                .Must(SsoEnforcementModes.IsValid)
                .WithMessage("Enforcement mode must be 'optional' or 'sso_only'.");
        });

        // FR-6/BR-5 (privilege ceiling, stateless part): the JIT default role, when supplied non-empty, must not
        // be a privileged admin/owner role. ("Role exists in this tenant" is checked in the service.)
        When(x => !string.IsNullOrWhiteSpace(x.Request.JitDefaultRole), () =>
        {
            RuleFor(x => x.Request.JitDefaultRole!)
                .Must(role => !PermissionCatalog.BuiltInRoles.PrivilegedForJit.Contains(role.Trim()))
                .WithMessage("The default SSO role cannot be a privileged admin or owner role.");
        });

        // AC-4/BR-3 (fast-path): if enabling SSO while explicitly providing empty allow-lists, reject immediately
        // with the story's exact message. The authoritative merged-state guard lives in the service.
        When(x => x.Request.SsoEnabled == true
                  && x.Request.AllowedEntraTenantIds is not null && x.Request.AllowedEntraTenantIds.Count == 0
                  && x.Request.AllowedEmailDomains is not null && x.Request.AllowedEmailDomains.Count == 0, () =>
        {
            RuleFor(x => x.Request)
                .Must(_ => false)
                .WithMessage("Add at least one trusted directory or email domain before enabling SSO.");
        });
    }
}
