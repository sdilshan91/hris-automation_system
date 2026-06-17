namespace HRM.Application.Features.SubscriptionPlans.DTOs;

/// <summary>Per-plan feature flags (US-ADM-009 FR-2). Mirrors <c>HRM.Domain.Entities.PlanFeatureFlags</c>.</summary>
public sealed record PlanFeatureFlagsDto(
    bool Sso,
    bool CustomDomain,
    bool WhiteLabel,
    bool Scim,
    bool Sandbox);

/// <summary>
/// A row in the System Admin plans list (US-ADM-009 AC-1/FR-5). Includes the ACTIVE TENANT COUNT on the plan.
/// </summary>
public sealed record PlanListItemDto(
    Guid Id,
    string Code,
    string Name,
    decimal PriceMonthly,
    decimal PriceYearly,
    string Currency,
    bool IsPublic,
    bool IsActive,
    int ActiveTenantCount);

/// <summary>Full plan detail (all fields) for the plan editor (US-ADM-009 AC-2/FR-2).</summary>
public sealed record PlanDetailDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    decimal PriceMonthly,
    decimal PriceYearly,
    string Currency,
    int TrialDays,
    bool IsPublic,
    bool IsActive,
    int? MaxEmployees,
    int? MaxStorageGb,
    int? MaxApiCallsPerMonth,
    int? MaxEmailSendsPerMonth,
    int? MaxCustomRoles,
    int? MaxCustomFieldsPerEntity,
    int? MaxWorkflows,
    int AuditLogRetentionDays,
    string SlaTier,
    IReadOnlyList<string> EnabledModules,
    PlanFeatureFlagsDto FeatureFlags,
    int ActiveTenantCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>
/// The mutable plan fields shared by Create and Update (US-ADM-009 FR-2). <c>Code</c> is supplied only on Create
/// (it is immutable thereafter, FR-3/BR-5), so it lives on the create command, not here.
/// </summary>
public sealed record PlanEditableFields(
    string Name,
    string? Description,
    decimal PriceMonthly,
    decimal PriceYearly,
    string Currency,
    int TrialDays,
    bool IsPublic,
    int? MaxEmployees,
    int? MaxStorageGb,
    int? MaxApiCallsPerMonth,
    int? MaxEmailSendsPerMonth,
    int? MaxCustomRoles,
    int? MaxCustomFieldsPerEntity,
    int? MaxWorkflows,
    int AuditLogRetentionDays,
    string SlaTier,
    IReadOnlyList<string> EnabledModules,
    PlanFeatureFlagsDto FeatureFlags);

/// <summary>A per-tenant plan-limit override row (US-ADM-009 AC-5/FR-4).</summary>
public sealed record PlanLimitOverrideDto(
    Guid Id,
    Guid TenantId,
    string LimitKey,
    long? Value,
    DateTime? ExpiresAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>Result of a plan create (US-ADM-009 AC-2).</summary>
public sealed record CreatePlanResultDto(Guid Id, string Code);
