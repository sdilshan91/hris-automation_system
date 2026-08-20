import type { Schema } from '@core/api';

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
// Wire values match the C# enum member names (PascalCase) per US-PLT-003; labels stay pretty.
export type EntitlementEmploymentType = 'FullTime' | 'PartTime' | 'Contract' | 'Intern';

export const EMPLOYMENT_TYPE_OPTIONS: { value: EntitlementEmploymentType; label: string }[] = [
  { value: 'FullTime', label: 'Full-Time' },
  { value: 'PartTime', label: 'Part-Time' },
  { value: 'Contract', label: 'Contract' },
  { value: 'Intern', label: 'Intern' },
];

/** Pretty display label for an employment-type wire value (e.g. 'FullTime' → 'Full-Time'). */
export function entitlementEmploymentTypeLabel(
  value: EntitlementEmploymentType | string | null | undefined
): string {
  if (!value) return '';
  return EMPLOYMENT_TYPE_OPTIONS.find((o) => o.value === value)?.label ?? String(value);
}

// ─── Entitlement Rule ────────────────────────────────────────

/** Entitlement rule entity returned by the API */
export interface IEntitlementRule {
  ruleId: string;
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
}

// ─── Generated wire types + mappers ─────────────────────────
//
// Each response the FE reads is typed as its GENERATED contract type and mapped explicitly, so a backend
// rename is a compile error here rather than a silently-blank cell. The mapper INPUT is the generated type.

/**
 * Entitlement-rule wire shape — `Schema<'LeaveEntitlementsLeaveEntitlementRuleDto'>`.
 *
 * Reconciled against `LeaveEntitlementRuleDto`:
 *   - `ruleId` (VM) ← `id` (wire).
 *   - `tenantId` was never sent and nothing renders it — removed from `IEntitlementRule`.
 *   - the wire also carries `jobLevelId`, which this screen does not model ("Job level has no backend entity")
 *     — intentionally dropped.
 *   - `employmentType` arrives as a plain string, narrowed to its union here.
 */
export type EntitlementRuleWire = Schema<'LeaveEntitlementsLeaveEntitlementRuleDto'>;

/** Map a wire entitlement rule onto the matrix view-model. */
export function mapEntitlementRule(w: EntitlementRuleWire): IEntitlementRule {
  return {
    ruleId: w.id ?? '',
    leaveTypeId: w.leaveTypeId ?? '',
    leaveTypeName: w.leaveTypeName ?? '',
    departmentId: w.departmentId ?? null,
    departmentName: w.departmentName ?? null,
    jobTitleId: w.jobTitleId ?? null,
    jobTitleName: w.jobTitleName ?? null,
    employmentType: (w.employmentType ?? null) as EntitlementEmploymentType | null,
    tenureMinMonths: w.tenureMinMonths ?? null,
    tenureMaxMonths: w.tenureMaxMonths ?? null,
    entitlementDays: w.entitlementDays ?? 0,
    priority: w.priority ?? 0,
    effectiveFrom: w.effectiveFrom ?? '',
    effectiveTo: w.effectiveTo ?? null,
    isActive: w.isActive ?? false,
    createdAt: w.createdAt ?? '',
    updatedAt: w.updatedAt ?? '',
  };
}

/**
 * Per-employee override wire shape — `Schema<'LeaveEntitlementsLeaveEntitlementOverrideDto'>`.
 * `overrideId` (VM) ← `id` (wire); `tenantId` was never sent and nothing renders it — removed.
 */
export type EntitlementOverrideWire = Schema<'LeaveEntitlementsLeaveEntitlementOverrideDto'>;

/** Map a wire override onto the view-model the overrides list renders. */
export function mapEntitlementOverride(w: EntitlementOverrideWire): IEntitlementOverride {
  return {
    overrideId: w.id ?? '',
    employeeId: w.employeeId ?? '',
    employeeName: w.employeeName ?? undefined,
    leaveTypeId: w.leaveTypeId ?? '',
    leaveTypeName: w.leaveTypeName ?? '',
    leaveYear: w.leaveYear ?? 0,
    entitlementDays: w.entitlementDays ?? 0,
    reason: w.reason ?? null,
    createdAt: w.createdAt ?? '',
    updatedAt: w.updatedAt ?? '',
  };
}

/**
 * Effective-entitlement wire shape — `Schema<'LeaveEntitlementsEffectiveEntitlementDto'>`.
 *
 * This one is a STRUCTURAL mismatch, not a rename — reconciled against `EffectiveEntitlementDto`:
 *   - the FE's single `entitlementDays` has NO wire twin. The wire splits it into `baseEntitlementDays`
 *     (full-year) and `proratedEntitlementDays` (after mid-year pro-rata, BR-2). The screen shows one
 *     "effective" number, so we derive it from `proratedEntitlementDays` — the value the employee is actually
 *     entitled to this year (equal to base for full-year employees). `baseEntitlementDays`, `currentBalance`
 *     and `leaveYear` are sent but not rendered on this screen — intentionally dropped.
 *   - `source` arrives as `"override"`, `"rule:{ruleId}"`, or `"leave_type_default"`. The UI's badge compares
 *     against the union `'override' | 'rule' | 'default'`, so we NORMALIZE here (a `"rule:…"` value used to
 *     fall through to "Default" because the raw string never equalled `"rule"`).
 *   - `ruleId` / `overrideId` were never sent as fields (the rule id is embedded in `source`) and nothing
 *     rendered them — removed from `IEffectiveEntitlement`.
 */
export type EffectiveEntitlementWire = Schema<'LeaveEntitlementsEffectiveEntitlementDto'>;

/** Narrow the wire `source` string (`"override"` | `"rule:{id}"` | `"leave_type_default"`) to the UI union. */
function normalizeEffectiveSource(source: string | null | undefined): IEffectiveEntitlement['source'] {
  if (source === 'override') {
    return 'override';
  }
  if (source?.startsWith('rule')) {
    return 'rule';
  }
  return 'default';
}

/** Map a wire effective entitlement onto the view-model the summary cards render. */
export function mapEffectiveEntitlement(w: EffectiveEntitlementWire): IEffectiveEntitlement {
  return {
    employeeId: w.employeeId ?? '',
    leaveTypeId: w.leaveTypeId ?? '',
    leaveTypeName: w.leaveTypeName ?? '',
    // Derived: the "effective" entitlement is the prorated value (BR-2); the wire has no flat `entitlementDays`.
    entitlementDays: w.proratedEntitlementDays ?? 0,
    source: normalizeEffectiveSource(w.source),
  };
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
