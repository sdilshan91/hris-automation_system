/**
 * US-LV-002: Leave Entitlement models matching the backend API contract.
 *
 * Backend endpoint: /api/v1/tenant/leave-entitlements
 * The backend agent is building this in parallel. Assumed contract below.
 *
 * Assumption: "Job level" has no backend entity — the dimension is omitted.
 * Supported dimensions: leaveTypeId, departmentId, jobTitleId, employmentType,
 *   tenureMinMonths, tenureMaxMonths.
 */

// ─── Employment type (reuse from employee models but keep local for decoupling) ─
export type EntitlementEmploymentType = 'Full-Time' | 'Part-Time' | 'Contract' | 'Intern';

export const EMPLOYMENT_TYPE_OPTIONS: { value: EntitlementEmploymentType; label: string }[] = [
  { value: 'Full-Time', label: 'Full-Time' },
  { value: 'Part-Time', label: 'Part-Time' },
  { value: 'Contract', label: 'Contract' },
  { value: 'Intern', label: 'Intern' },
];

// ─── Entitlement Rule ────────────────────────────────────────

/** Entitlement rule entity returned by the API */
export interface IEntitlementRule {
  ruleId: string;
  tenantId: string;
  leaveTypeId: string;
  leaveTypeName: string;
  departmentId: string | null;
  departmentName: string | null;
  jobTitleId: string | null;
  jobTitleName: string | null;
  employmentType: EntitlementEmploymentType | null;
  tenureMinMonths: number | null;
  tenureMaxMonths: number | null;
  entitlementDays: number;
  priority: number;
  effectiveFrom: string;
  effectiveTo: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

/** Request payload for creating an entitlement rule (FR-1) */
export interface ICreateEntitlementRuleRequest {
  leaveTypeId: string;
  departmentId?: string | null;
  jobTitleId?: string | null;
  employmentType?: EntitlementEmploymentType | null;
  tenureMinMonths?: number | null;
  tenureMaxMonths?: number | null;
  entitlementDays: number;
  priority: number;
  effectiveFrom: string;
  effectiveTo?: string | null;
}

/** Request payload for updating an entitlement rule */
export interface IUpdateEntitlementRuleRequest extends ICreateEntitlementRuleRequest {}

/** Inline update for a single cell in the matrix */
export interface IInlineUpdateRequest {
  entitlementDays: number;
}

// ─── Per-Employee Override (AC-3) ────────────────────────────

/** Per-employee leave entitlement override */
export interface IEntitlementOverride {
  overrideId: string;
  tenantId: string;
  employeeId: string;
  employeeName?: string;
  leaveTypeId: string;
  leaveTypeName: string;
  leaveYear: number;
  entitlementDays: number;
  reason: string | null;
  createdAt: string;
  updatedAt: string;
}

/** Request payload for creating/upserting an override */
export interface IUpsertOverrideRequest {
  leaveTypeId: string;
  leaveYear: number;
  entitlementDays: number;
  reason?: string | null;
}

// ─── Computed Effective Entitlement ─────────────────────────

/** The effective entitlement for a specific employee + leave type */
export interface IEffectiveEntitlement {
  employeeId: string;
  leaveTypeId: string;
  leaveTypeName: string;
  entitlementDays: number;
  source: 'override' | 'rule' | 'default';
  ruleId?: string | null;
  overrideId?: string | null;
}

// ─── Bulk Assignment (FR-4) ─────────────────────────────────

/** Bulk assignment request */
export interface IBulkEntitlementRequest {
  leaveTypeId: string;
  entitlementDays: number;
  employeeIds: string[];
  leaveYear: number;
  reason?: string | null;
}

/** Bulk assignment response */
export interface IBulkEntitlementResponse {
  totalProcessed: number;
  totalSuccess: number;
  totalFailed: number;
}

// ─── Lookup types for dropdowns ─────────────────────────────

export interface ILookupItem {
  id: string;
  name: string;
}

// ─── Priority / specificity ─────────────────────────────────

/**
 * Rule priority/specificity ordering (FR-2, AC-2).
 * Higher priority number = more specific = wins.
 *
 * Per US-LV-002 FR-2:
 *   employee override (always wins)
 *   > department + job title + employment type (priority ~7)
 *   > department + job title (priority ~6)
 *   > department + employment type (priority ~5)
 *   > department only (priority ~4)
 *   > job title only (priority ~3)
 *   > employment type only (priority ~2)
 *   > default entitlement on leave type (priority ~1)
 *
 * "Job level" is omitted because there is no backend entity for it.
 */
export const PRIORITY_HELP_TEXT =
  'Rules are evaluated by specificity. A rule matching more dimensions ' +
  '(e.g. department + job title + employment type) overrides a less specific one. ' +
  'Per-employee overrides always take precedence over all rules. ' +
  'Higher priority number = more specific.';

/** Filter state for the rules matrix */
export interface IEntitlementRuleFilter {
  leaveTypeId?: string;
  departmentId?: string;
  employmentType?: EntitlementEmploymentType;
  activeOnly?: boolean;
}
