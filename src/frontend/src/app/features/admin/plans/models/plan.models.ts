/**
 * US-ADM-009: System Admin Console — subscription plan models.
 *
 * These DTOs match the System Admin subscription-plan backend contract, rooted
 * at `/api/v1/system/plans` (the platform/system context, same root as the
 * US-ADM-003 impersonation service) — NOT the tenant-scoped `/api/v1/...`
 * feature namespaces.
 *
 * BR-3: a NULL value for any limit field means "Unlimited". The editor models
 * this with a per-field "Unlimited" toggle that nulls the numeric value.
 */

import type { Schema } from '@core/api';

/** Currency codes supported by the pricing fields (extensible; free-text on BE). */
export type Currency = 'USD' | 'EUR' | 'GBP' | 'AUD' | 'INR' | 'LKR';

/** SLA tiers offered per plan. */
export type SlaTier = 'standard' | 'business' | 'enterprise';

/**
 * Feature flags (JSONB object on the BE). Boolean entitlements toggled per plan.
 */
export interface IPlanFeatureFlags {
  sso: boolean;
  customDomain: boolean;
  whiteLabel: boolean;
  scim: boolean;
  sandbox: boolean;
}

/**
 * GET /api/v1/system/plans — plan list row (AC-1). A lightweight projection
 * with the active-tenant count (FR-5) for the card/table list + sorting.
 */
export interface IPlanSummary {
  id: string;
  code: string;
  name: string;
  priceMonthly: number | null;
  priceYearly: number | null;
  currency: string;
  isPublic: boolean;
  isActive: boolean;
  activeTenantCount: number;
}

/**
 * GET /api/v1/system/plans/{id} — full plan (AC-2/§8). Any limit field that is
 * `null` means "Unlimited" (BR-3).
 */
export interface IPlanDetail {
  id: string;
  code: string;
  name: string;
  description: string | null;
  isPublic: boolean;
  isActive: boolean;
  priceMonthly: number | null;
  priceYearly: number | null;
  currency: string;
  trialDays: number;
  maxEmployees: number | null;
  maxStorageGb: number | null;
  maxApiCallsPerMonth: number | null;
  maxEmailSendsPerMonth: number | null;
  maxCustomRoles: number | null;
  maxCustomFieldsPerEntity: number | null;
  maxWorkflows: number | null;
  enabledModules: string[];
  featureFlags: IPlanFeatureFlags;
  auditLogRetentionDays: number | null;
  slaTier: string;
}

/**
 * Create / update payload. `code` is included on create but is IMMUTABLE on
 * update (FR-3 / BR-5) — the editor renders it read-only when editing and the
 * BE ignores any code change.
 */
export interface IPlanUpsert {
  code: string;
  name: string;
  description: string | null;
  isPublic: boolean;
  isActive: boolean;
  priceMonthly: number | null;
  priceYearly: number | null;
  currency: string;
  trialDays: number;
  maxEmployees: number | null;
  maxStorageGb: number | null;
  maxApiCallsPerMonth: number | null;
  maxEmailSendsPerMonth: number | null;
  maxCustomRoles: number | null;
  maxCustomFieldsPerEntity: number | null;
  maxWorkflows: number | null;
  enabledModules: string[];
  featureFlags: IPlanFeatureFlags;
  auditLogRetentionDays: number | null;
  slaTier: string;
}

/**
 * Per-tenant plan limit override (AC-5 / FR-4). Runtime resolution order is:
 * override (if present and not expired) > plan field.
 */
export interface IPlanLimitOverride {
  /** Server-assigned id. `DELETE /system/plans/overrides/{overrideId}` keys on THIS, not on `limitKey`. */
  id: string;
  limitKey: string;
  value: number | null;
  expiresAt: string | null;
}

/** The fields a caller supplies when upserting an override; the id comes back from the server. */
export type IPlanLimitOverrideDraft = Omit<IPlanLimitOverride, 'id'>;

/**
 * Canonical module list for the module-entitlement toggles (FR-6). CoreHR is
 * always enabled and rendered as a disabled, checked toggle.
 */
export interface IModuleOption {
  key: string;
  label: string;
  alwaysOn: boolean;
}

export const CANONICAL_MODULES: IModuleOption[] = [
  { key: 'CoreHR', label: 'Core HR', alwaysOn: true },
  { key: 'Leave', label: 'Leave', alwaysOn: false },
  { key: 'Attendance', label: 'Attendance', alwaysOn: false },
  { key: 'Recruitment', label: 'Recruitment', alwaysOn: false },
  { key: 'Onboarding', label: 'Onboarding', alwaysOn: false },
  { key: 'Payroll', label: 'Payroll', alwaysOn: false },
  { key: 'Performance', label: 'Performance', alwaysOn: false },
  { key: 'Training', label: 'Training', alwaysOn: false },
  { key: 'Asset', label: 'Asset', alwaysOn: false },
  { key: 'Benefits', label: 'Benefits', alwaysOn: false },
  { key: 'Reporting', label: 'Reporting', alwaysOn: false },
  { key: 'CustomReportBuilder', label: 'Custom Report Builder', alwaysOn: false },
  { key: 'PublicCareersPage', label: 'Public Careers Page', alwaysOn: false },
];

/**
 * The numeric limit fields shared by the editor and the comparison view. Each
 * is "Unlimited" when null (BR-3).
 *
 * BUG-472 — this used to also carry a `limitKey` that the overrides UI reused as the
 * BE override vocabulary. It is NOT the same vocabulary, and conflating them is the
 * bug: plan COLUMNS are camelCase and include `auditLogRetentionDays`; override KEYS
 * are snake_case, exclude audit-log retention, and include a key with no plan column
 * at all (`max_template_language_variants`). The two lists are now separate —
 * see OVERRIDE_LIMIT_FIELDS.
 */
export interface ILimitField {
  /** Form control name on the editor's limits group. */
  controlName: keyof IPlanLimitNumbers;
  label: string;
}

/** The numeric, nullable limit fields of a plan (BR-3). */
export interface IPlanLimitNumbers {
  maxEmployees: number | null;
  maxStorageGb: number | null;
  maxApiCallsPerMonth: number | null;
  maxEmailSendsPerMonth: number | null;
  maxCustomRoles: number | null;
  maxCustomFieldsPerEntity: number | null;
  maxWorkflows: number | null;
  auditLogRetentionDays: number | null;
}

export const LIMIT_FIELDS: ILimitField[] = [
  { controlName: 'maxEmployees', label: 'Max employees' },
  { controlName: 'maxStorageGb', label: 'Max storage (GB)' },
  { controlName: 'maxApiCallsPerMonth', label: 'Max API calls / month' },
  { controlName: 'maxEmailSendsPerMonth', label: 'Max email sends / month' },
  { controlName: 'maxCustomRoles', label: 'Max custom roles' },
  { controlName: 'maxCustomFieldsPerEntity', label: 'Max custom fields / entity' },
  { controlName: 'maxWorkflows', label: 'Max workflows' },
  { controlName: 'auditLogRetentionDays', label: 'Audit log retention (days)' },
];

/** One selectable key in the per-tenant overrides UI (AC-5 / FR-4). */
export interface IOverrideLimitField {
  /** Canonical BE override key — snake_case, validated against PlanLimitKeys. */
  limitKey: string;
  label: string;
}

/**
 * The ONLY keys `POST /system/plans/overrides` accepts (BUG-472).
 *
 * Mirrors `HRM.Domain.Authorization.PlanLimitKeys`; `UpsertPlanLimitOverrideValidator`
 * rejects anything else with `limit_key_invalid`. Keep this list in sync with that class —
 * note `audit_log_retention_days` is deliberately absent (it is a plan column, not an
 * overridable limit) and `max_template_language_variants` has no plan column of its own.
 */
export const OVERRIDE_LIMIT_FIELDS: IOverrideLimitField[] = [
  { limitKey: 'max_employees', label: 'Max employees' },
  { limitKey: 'max_storage_gb', label: 'Max storage (GB)' },
  { limitKey: 'max_api_calls_per_month', label: 'Max API calls / month' },
  { limitKey: 'max_email_sends_per_month', label: 'Max email sends / month' },
  { limitKey: 'max_custom_roles', label: 'Max custom roles' },
  { limitKey: 'max_custom_fields_per_entity', label: 'Max custom fields / entity' },
  { limitKey: 'max_workflows', label: 'Max workflows' },
  { limitKey: 'max_template_language_variants', label: 'Max template language variants' },
];

/** Feature-flag descriptors for the toggle grid + comparison view. */
export interface IFeatureFlagField {
  key: keyof IPlanFeatureFlags;
  label: string;
  /**
   * ISSUE-358 — set when the flag is sellable in this editor but enforced by NOTHING, because the feature
   * behind it does not exist yet.
   *
   * Of the five flags, only `sso` and `whiteLabel` gate real behaviour. The other three are inert:
   * `ScimEntitlementMiddleware` matches no controller, the `customDomain` branch in
   * `TenantResolutionMiddleware` is self-documented as inert, and `TenantProvisioningService` hard-codes
   * `requestedSandbox = false`, making its branch unreachable.
   *
   * They are DISABLED rather than deleted: removing them would strand plans that already carry the flag with
   * no way to see or unset it, and the flags remain part of the backend contract. Disabling stops them being
   * sold while keeping the data honest — the original ISSUE-358 charge was that the console sells what the
   * platform does not deliver.
   */
  unavailableReason?: string;
}

const NOT_IMPLEMENTED = 'Not available yet — no feature is gated by this flag';

export const FEATURE_FLAG_FIELDS: IFeatureFlagField[] = [
  { key: 'sso', label: 'Single Sign-On (SSO)' },
  { key: 'customDomain', label: 'Custom domain', unavailableReason: NOT_IMPLEMENTED },
  { key: 'whiteLabel', label: 'White-label' },
  { key: 'scim', label: 'SCIM provisioning', unavailableReason: NOT_IMPLEMENTED },
  { key: 'sandbox', label: 'Sandbox environment', unavailableReason: NOT_IMPLEMENTED },
];

export const SLA_TIERS: SlaTier[] = ['standard', 'business', 'enterprise'];

export const CURRENCIES: Currency[] = ['USD', 'EUR', 'GBP', 'AUD', 'INR', 'LKR'];

/** A blank feature-flag set (all off) for a new plan. */
export function emptyFeatureFlags(): IPlanFeatureFlags {
  return {
    sso: false,
    customDomain: false,
    whiteLabel: false,
    scim: false,
    sandbox: false,
  };
}

/** Display a nullable limit as a number or the "Unlimited" sentinel. */
export function limitDisplay(value: number | null): string {
  return value === null || value === undefined ? 'Unlimited' : `${value}`;
}

// ─── Wire contract → view-model mappers (D1 admin slice 1) ────────────────────
//
// `http.get<IPlanDetail>(…)` was an unchecked cast: TypeScript accepted whatever the
// server sent and called it an `IPlanDetail`. The reads below consume the GENERATED
// contract types instead, so a backend DTO rename is a compile error here rather than
// a silent `undefined` in the plan editor.
//
// Three drifts this surfaced (reported, NOT silently papered over):
//   • POST /system/plans returns `CreatePlanResultDto` = { id, code } — NOT a full
//     plan. `create()` claimed `IPlanDetail`; every field but those two was undefined.
//   • PUT  /system/plans/{id} returns a bare `ApiResponse` with no `data` at all.
//     `update()` claimed `IPlanDetail`; the whole object was undefined.
//   • The three override routes were addressed at a per-tenant path the API has never
//     served, so every call 404'd (BUG-471). Fixed: the service now calls the real
//     `/system/plans/overrides` routes and these wire types are their DTOs.

export type PlanListItemWire = Schema<'SubscriptionPlansPlanListItemDto'>;
export type PlanDetailWire = Schema<'SubscriptionPlansPlanDetailDto'>;
export type PlanFeatureFlagsWire = Schema<'SubscriptionPlansPlanFeatureFlagsDto'>;
export type PlanCreateResultWire = Schema<'SubscriptionPlansCreatePlanResultDto'>;
export type PlanLimitOverrideWire = Schema<'SubscriptionPlansPlanLimitOverrideDto'>;
export type PlanLimitOverrideUpsertWire =
  Schema<'SubscriptionPlansUpsertPlanLimitOverrideCommand'>;

/**
 * What `POST /system/plans` ACTUALLY returns. The plan editor discards the response
 * and navigates back to the list, so nothing was visibly broken — but the declared
 * `IPlanDetail` described 20 fields the endpoint has never sent.
 */
export interface IPlanCreateResult {
  id: string;
  code: string;
}

/** Every flag is optional on the wire (Swashbuckle emits no `required`); absent = off. */
export function mapPlanFeatureFlags(
  w: PlanFeatureFlagsWire | undefined,
): IPlanFeatureFlags {
  return {
    sso: w?.sso ?? false,
    customDomain: w?.customDomain ?? false,
    whiteLabel: w?.whiteLabel ?? false,
    scim: w?.scim ?? false,
    sandbox: w?.sandbox ?? false,
  };
}

/** Field names match the wire 1:1; the mapper only supplies defaults for optionals. */
export function mapPlanSummary(w: PlanListItemWire): IPlanSummary {
  return {
    id: w.id ?? '',
    code: w.code ?? '',
    name: w.name ?? '',
    priceMonthly: w.priceMonthly ?? null,
    priceYearly: w.priceYearly ?? null,
    currency: w.currency ?? '',
    isPublic: w.isPublic ?? false,
    isActive: w.isActive ?? false,
    activeTenantCount: w.activeTenantCount ?? 0,
  };
}

/**
 * Field names match the wire 1:1. Note `auditLogRetentionDays` is a non-nullable
 * `int` on the wire but nullable here (BR-3 "Unlimited"); `?? null` preserves the
 * FE's Unlimited sentinel without inventing a number the server did not send.
 */
export function mapPlanDetail(w: PlanDetailWire): IPlanDetail {
  return {
    id: w.id ?? '',
    code: w.code ?? '',
    name: w.name ?? '',
    description: w.description ?? null,
    isPublic: w.isPublic ?? false,
    isActive: w.isActive ?? false,
    priceMonthly: w.priceMonthly ?? null,
    priceYearly: w.priceYearly ?? null,
    currency: w.currency ?? '',
    trialDays: w.trialDays ?? 0,
    maxEmployees: w.maxEmployees ?? null,
    maxStorageGb: w.maxStorageGb ?? null,
    maxApiCallsPerMonth: w.maxApiCallsPerMonth ?? null,
    maxEmailSendsPerMonth: w.maxEmailSendsPerMonth ?? null,
    maxCustomRoles: w.maxCustomRoles ?? null,
    maxCustomFieldsPerEntity: w.maxCustomFieldsPerEntity ?? null,
    maxWorkflows: w.maxWorkflows ?? null,
    enabledModules: w.enabledModules ?? [],
    featureFlags: mapPlanFeatureFlags(w.featureFlags),
    auditLogRetentionDays: w.auditLogRetentionDays ?? null,
    slaTier: w.slaTier ?? '',
  };
}

export function mapPlanCreateResult(w: PlanCreateResultWire): IPlanCreateResult {
  return { id: w.id ?? '', code: w.code ?? '' };
}

/**
 * `id` / `limitKey` / `value` / `expiresAt` all exist on the wire DTO 1:1; `id` is what
 * `DELETE /system/plans/overrides/{overrideId}` keys on, so the view-model carries it.
 * `tenantId` / `createdAt` / `updatedAt` are dropped — nothing renders them.
 */
export function mapPlanLimitOverride(
  w: PlanLimitOverrideWire,
): IPlanLimitOverride {
  return {
    id: w.id ?? '',
    limitKey: w.limitKey ?? '',
    value: w.value ?? null,
    expiresAt: w.expiresAt ?? null,
  };
}
