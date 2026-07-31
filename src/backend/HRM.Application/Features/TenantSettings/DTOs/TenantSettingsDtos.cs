namespace HRM.Application.Features.TenantSettings.DTOs;

// ── Read model (GET settings) — US-ADM-006 FR-1 ──────────────────────────────

/// <summary>Full company-settings snapshot for the current tenant (US-ADM-006 GET settings).</summary>
public sealed record TenantSettingsDto(
    OrgProfileDto OrgProfile,
    BrandingDto Branding,
    LocalizationDto Localization,
    PasswordPolicyDto PasswordPolicy,
    SessionPolicyDto SessionPolicy,
    // US-REC-010 FR-5/BR-7 (ISSUE-140): auto-create a login account when an applicant is hired. APPENDED —
    // positional record; trailing default keeps existing construction valid.
    bool AutoCreateUserOnHire = false,
    // US-ADM-006 BR-3 / ISSUE-358: the plan-gating signal the frontend has always been coded to read
    // (branding-section.component.ts checks plan()?.lockedFeatures) but which the backend never emitted, so
    // the "Upgrade your plan" affordance could never fire and no setting was ever gated. Nullable + trailing
    // default so every existing construction site stays valid; the FE already no-ops when it is absent.
    PlanGatingDto? Plan = null);

/// <summary>
/// US-ADM-006 BR-3 (ISSUE-358): which settings the tenant's plan does NOT permit. Feature keys are the
/// well-known strings the frontend matches on (currently <c>branding.customColor</c>).
///
/// <para>Scope note: of the five <c>PlanFeatureFlags</c>, only <c>WhiteLabel</c> is represented here, because
/// it is the only one with a real feature behind it (tenant branding). <c>CustomDomain</c>, <c>Scim</c> and
/// <c>Sandbox</c> have ZERO implementing code, so gating them would enforce entitlement to nothing — the
/// ISSUE-356 lesson. They stay unrepresented until the features exist.</para>
/// </summary>
public sealed record PlanGatingDto(string? Tier, IReadOnlyList<string> LockedFeatures);

public sealed record OrgProfileDto(
    string Name,
    string? LegalName,
    string? RegistrationNumber,
    string? Address,
    string? Industry,
    string? CompanySize,
    int FiscalYearStartMonth,
    // Multi-country tax foundation (settings key org.default_country_code): fallback tax country.
    string? DefaultCountryCode,
    // ISSUE-304 (US-CHR-009 BR-6): tenant probation period in days from date of joining. APPENDED — these are
    // positional records. A Location may override it (Location.ProbationPeriodDays).
    int ProbationPeriodDays = 90,
    // ISSUE-159 (US-PAY-004 BR-3): tenant-configurable payslip footer disclaimer. Null/blank → the payslip
    // renderer uses the standard wording (PayslipBranding.DefaultFooterDisclaimer). APPENDED positional.
    string? PayslipFooterDisclaimer = null,
    // ISSUE-229 (US-PAY-011 BR-4): tenant-configurable "From" address for payslip distribution emails. Null/blank
    // → payslip emails use the system default sender. APPENDED positional.
    string? PayrollFromEmail = null,
    // DF-20/ISSUE-044 (US-LV-010 FR-7): tenant leave-cancellation notice window in days. An employee may
    // self-cancel an APPROVED leave only when StartDate > today + N. Default 0 = cancellable strictly before
    // start. APPENDED positional.
    int LeaveCancellationWindowDays = 0);

public sealed record BrandingDto(
    string? LogoUrl,
    string? EmailLogoUrl,
    string? FaviconUrl,
    string? PrimaryColor);

public sealed record LocalizationDto(
    string DefaultLanguage,
    string DateFormat,
    string NumberFormat,
    string TimeZone,
    string Currency);

public sealed record PasswordPolicyDto(
    int MinLength,
    bool RequireUppercase,
    bool RequireLowercase,
    bool RequireDigit,
    bool RequireSpecialCharacter,
    int HistoryCount,
    int? MaxAgeDays);

public sealed record SessionPolicyDto(
    int IdleTimeoutMinutes,
    int AbsoluteTimeoutHours,
    int MaxConcurrentSessions,
    string ConcurrentSessionStrategy);

/// <summary>US-REC-010 FR-5/BR-7 (ISSUE-140): tenant hiring settings — auto-create a login account on hire.</summary>
public sealed record HiringSettingsDto(bool AutoCreateUserOnHire);

// ── Update request bodies ────────────────────────────────────────────────────

public sealed record UpdateOrgProfileRequest(
    string Name,
    string? LegalName,
    string? RegistrationNumber,
    string? Address,
    string? Industry,
    string? CompanySize,
    int FiscalYearStartMonth,
    // Multi-country tax foundation (settings key org.default_country_code): fallback tax country.
    string? DefaultCountryCode,
    // ISSUE-304 (US-CHR-009 BR-6): tenant probation period in days from date of joining. APPENDED — these are
    // positional records. A Location may override it (Location.ProbationPeriodDays).
    int ProbationPeriodDays = 90,
    // ISSUE-159 (US-PAY-004 BR-3): tenant-configurable payslip footer disclaimer. Null/blank clears it (renderer
    // falls back to the standard wording). APPENDED positional.
    string? PayslipFooterDisclaimer = null,
    // ISSUE-229 (US-PAY-011 BR-4): tenant-configurable "From" address for payslip distribution emails. Null/blank
    // clears it (payslip emails fall back to the system default sender). Validated as a well-formed email on
    // write. APPENDED positional.
    string? PayrollFromEmail = null,
    // DF-20/ISSUE-044 (US-LV-010 FR-7): tenant leave-cancellation notice window in days (0-90). An employee may
    // self-cancel an APPROVED leave only when StartDate > today + N. Default 0 = cancellable strictly before
    // start. APPENDED positional.
    int LeaveCancellationWindowDays = 0);

public sealed record UpdateLocalizationRequest(
    string DefaultLanguage,
    string DateFormat,
    string NumberFormat,
    string TimeZone,
    string Currency);

public sealed record UpdatePasswordPolicyRequest(
    int MinLength,
    bool RequireUppercase,
    bool RequireLowercase,
    bool RequireDigit,
    bool RequireSpecialCharacter,
    int HistoryCount,
    int? MaxAgeDays);

public sealed record UpdateSessionPolicyRequest(
    int IdleTimeoutMinutes,
    int AbsoluteTimeoutHours,
    int MaxConcurrentSessions,
    string? ConcurrentSessionStrategy);

/// <summary>US-REC-010 FR-5/BR-7 (ISSUE-140): update the tenant's auto-create-user-on-hire toggle.</summary>
public sealed record UpdateHiringSettingsRequest(bool AutoCreateUserOnHire);

public sealed record UpdatePrimaryColorRequest(string PrimaryColor);

/// <summary>Result of a branding upload: the stored, tenant-scoped URL the FE renders (US-ADM-006 AC-2).</summary>
public sealed record BrandingUploadResultDto(string AssetKind, string Url);

/// <summary>
/// The buffered bytes + content-type of a stored branding asset, streamed back to the caller by the public
/// branding-serve endpoints (ISSUE-204). Logos are ≤2 MB (BR-6), so buffering is cheap.
/// </summary>
public sealed record BrandingAssetContentDto(byte[] Content, string ContentType);
