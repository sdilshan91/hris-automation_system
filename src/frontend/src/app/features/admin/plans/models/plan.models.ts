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
  limitKey: string;
  value: number | null;
  expiresAt: string | null;
}

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
 * is "Unlimited" when null (BR-3). `limitKey` matches the BE override key so the
 * overrides UI can reuse the same vocabulary.
 */
export interface ILimitField {
  /** Form control name on the editor's limits group. */
  controlName: keyof IPlanLimitNumbers;
  /** Override key (FR-4) — same vocabulary the BE uses for plan_limit_override. */
  limitKey: string;
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
  { controlName: 'maxEmployees', limitKey: 'maxEmployees', label: 'Max employees' },
  { controlName: 'maxStorageGb', limitKey: 'maxStorageGb', label: 'Max storage (GB)' },
  { controlName: 'maxApiCallsPerMonth', limitKey: 'maxApiCallsPerMonth', label: 'Max API calls / month' },
  { controlName: 'maxEmailSendsPerMonth', limitKey: 'maxEmailSendsPerMonth', label: 'Max email sends / month' },
  { controlName: 'maxCustomRoles', limitKey: 'maxCustomRoles', label: 'Max custom roles' },
  { controlName: 'maxCustomFieldsPerEntity', limitKey: 'maxCustomFieldsPerEntity', label: 'Max custom fields / entity' },
  { controlName: 'maxWorkflows', limitKey: 'maxWorkflows', label: 'Max workflows' },
  { controlName: 'auditLogRetentionDays', limitKey: 'auditLogRetentionDays', label: 'Audit log retention (days)' },
];

/** Feature-flag descriptors for the toggle grid + comparison view. */
export interface IFeatureFlagField {
  key: keyof IPlanFeatureFlags;
  label: string;
}

export const FEATURE_FLAG_FIELDS: IFeatureFlagField[] = [
  { key: 'sso', label: 'Single Sign-On (SSO)' },
  { key: 'customDomain', label: 'Custom domain' },
  { key: 'whiteLabel', label: 'White-label' },
  { key: 'scim', label: 'SCIM provisioning' },
  { key: 'sandbox', label: 'Sandbox environment' },
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
