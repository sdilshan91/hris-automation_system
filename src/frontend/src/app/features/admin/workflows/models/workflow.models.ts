/**
 * US-ADM-007 — Tenant Admin Manages Approval Workflows.
 *
 * All model interfaces/enums live in this single file (per the implementation
 * brief) so the contract can be realigned in one place while the backend DTOs
 * are finalized in parallel. These are TENANT-scoped resources served under
 * `/api/v1/tenant/workflows...` (NOT the system-admin console root). Tenant
 * isolation is enforced server-side via ITenantContext + EF global filters;
 * the FE just carries the auth cookie + the X-Tenant-Subdomain header (added by
 * the tenantInterceptor). Services consume bare payloads (no ApiResponse unwrap),
 * matching US-ADM-005/006.
 *
 * NOTE: this FE configures workflow DEFINITIONS only. The runtime engine
 * (live routing / SLA timers / delegation execution) is backend-deferred.
 */

import type { Schema } from '@core/api';

// ─── Enums (string unions to mirror the backend) ─────────────────────────

/** The request type a workflow governs (FR-1). Grouped in the list UI (AC-1). */
export type WorkflowEntityType =
  | 'Leave'
  | 'Attendance'
  | 'Expense'
  | 'Offer'
  | 'SalaryRevision'
  | 'Overtime';

/** Lifecycle status of a workflow definition (AC-1). */
export type WorkflowStatus = 'Active' | 'Draft' | 'Archived';

/** How a step's approver is resolved at runtime (FR-2). */
export type ApproverType =
  | 'LineManager'
  | 'Role'
  | 'NamedUser'
  | 'DepartmentHead';

/** Comparison operators for a conditional step (FR-2, §10 simple expressions). */
export type ConditionOperator = '>' | '>=' | '<' | '<=' | '==' | '!=';

/** All selectable entity types, in display order (AC-1 grouping). */
export const WORKFLOW_ENTITY_TYPES: readonly WorkflowEntityType[] = [
  'Leave',
  'Attendance',
  'Expense',
  'Offer',
  'SalaryRevision',
  'Overtime',
] as const;

/** All approver types, in display order (step + escalation pickers). */
export const APPROVER_TYPES: readonly ApproverType[] = [
  'LineManager',
  'Role',
  'NamedUser',
  'DepartmentHead',
] as const;

/** Condition operators, in display order. */
export const CONDITION_OPERATORS: readonly ConditionOperator[] = [
  '>',
  '>=',
  '<',
  '<=',
  '==',
  '!=',
] as const;

/**
 * Approver types that reference an external identifier (role id / user id).
 * LineManager + DepartmentHead are resolved positionally at runtime and carry
 * no identifier — used by the UI to decide whether to show the picker.
 */
export const IDENTIFIER_APPROVER_TYPES: readonly ApproverType[] = [
  'Role',
  'NamedUser',
] as const;

/** True when the given approver type requires an explicit identifier. */
export function approverNeedsIdentifier(type: ApproverType): boolean {
  return IDENTIFIER_APPROVER_TYPES.includes(type);
}

// ─── Condition (optional per step) ───────────────────────────────────────

/**
 * A simple field/operator/value condition (FR-2). Serialized to `conditionJson`
 * on the wire, e.g. `{"field":"days_requested","operator":">","value":5}`.
 */
export interface IWorkflowCondition {
  field: string;
  operator: ConditionOperator;
  value: string | number;
}

// ─── Step ────────────────────────────────────────────────────────────────

/** A single ordered approval step (FR-2). */
export interface IWorkflowStep {
  stepOrder: number;
  approverType: ApproverType;
  /** role id / user id when approverType is Role / NamedUser (else null). */
  approverIdentifier?: string | null;
  /** When true, ALL configured approvers must approve before proceeding (BR-3). */
  isParallel: boolean;
  /** Extra approvers for a parallel step (BR-3). Primary stays in approverIdentifier. */
  parallelApproverIdentifiers?: string[];
  /** SLA window in hours; positive integer (FR-5). */
  slaHours: number;
  /** Escalation target on SLA breach (BR-4) — approver type. */
  escalationApproverType?: ApproverType | null;
  /** Escalation target identifier (role id / user id) when applicable. */
  escalationApproverIdentifier?: string | null;
  /** Serialized condition; step is skipped at runtime when condition is unmet (BR-5). */
  conditionJson?: string | null;
  /** AC-5: route to a backup approver when the primary is on leave. */
  delegationEnabled: boolean;
  /** Backup approver user id used when delegationEnabled (AC-5). */
  delegationBackupUserId?: string | null;
}

// ─── Workflow definition ─────────────────────────────────────────────────

/** A row in the grouped workflow list (AC-1). */
export interface IWorkflowSummary {
  /** The VERSION id. `GET /{id}`, archive, restore and delete key on this. */
  id: string;
  /**
   * The lineage id — the stable identity across versions. `PUT /tenant/workflows/
   * {lineageId}` keys on THIS, not on `id`. The wire has always sent it; the FE
   * model dropped it, which is why the editor's save targets the wrong key (see
   * the OUT-OF-LANE note). Captured here so the fix has something to use.
   */
  lineageId: string;
  name: string;
  entityType: WorkflowEntityType;
  version: number;
  status: WorkflowStatus;
  /** Seeded-default workflow, editable but badged "Default" (AC-1). */
  isDefault: boolean;
  stepCount: number;
  /**
   * RENAMED from the wire's `lastModifiedAt`, and now nullable: the wire sends
   * `null` for a workflow that has never been edited. It was declared non-null
   * `string`, so the list's date cell rendered `undefined` for every fresh row.
   */
  updatedAt: string | null;
  /** US-ADM-011c FR-10: count of live (InProgress) runtime instances on this lineage. */
  inFlightCount?: number;
}

/** Full workflow definition with its ordered steps (editor payload, AC-2). */
export interface IWorkflowDefinition {
  /** The VERSION id. */
  id: string;
  /** The lineage id — the key `PUT /tenant/workflows/{lineageId}` expects. */
  lineageId: string;
  name: string;
  entityType: WorkflowEntityType;
  version: number;
  status: WorkflowStatus;
  isDefault: boolean;
  steps: IWorkflowStep[];
}

/** Body for creating a workflow (AC-2). Status/version assigned server-side. */
export interface ICreateWorkflowRequest {
  name: string;
  entityType: WorkflowEntityType;
  steps: IWorkflowStep[];
}

/** Body for updating a workflow → creates a NEW version server-side (AC-3). */
export interface IUpdateWorkflowRequest {
  name: string;
  steps: IWorkflowStep[];
}

// ─── Approver picker sources (reused endpoints) ──────────────────────────

/** A role option for the approver picker (reuses /api/v1/tenant/roles). */
export interface IApproverRoleOption {
  id: string;
  name: string;
}

/** A user option for the approver / delegation picker (reuses /api/v1/users). */
export interface IApproverUserOption {
  id: string;
  displayName: string;
  email: string;
}

// ─── Helpers (pure, unit-testable) ───────────────────────────────────────

/**
 * Serialize a condition object to the wire `conditionJson` string, or null when
 * the condition is incomplete (no field). Keeps numeric values numeric.
 */
export function serializeCondition(
  condition: IWorkflowCondition | null | undefined
): string | null {
  if (!condition || !condition.field?.trim()) {
    return null;
  }
  const rawValue = condition.value;
  const numeric =
    typeof rawValue === 'number'
      ? rawValue
      : rawValue !== '' && !isNaN(Number(rawValue))
        ? Number(rawValue)
        : rawValue;
  return JSON.stringify({
    field: condition.field.trim(),
    operator: condition.operator,
    value: numeric,
  });
}

/** Parse a wire `conditionJson` string back into a condition object (or null). */
export function parseCondition(
  conditionJson: string | null | undefined
): IWorkflowCondition | null {
  if (!conditionJson) {
    return null;
  }
  try {
    const parsed = JSON.parse(conditionJson) as Partial<IWorkflowCondition>;
    if (!parsed.field || !parsed.operator) {
      return null;
    }
    return {
      field: parsed.field,
      operator: parsed.operator as ConditionOperator,
      value: parsed.value as string | number,
    };
  } catch {
    return null;
  }
}

/** Group workflow summaries by entity type, preserving display order (AC-1). */
export function groupByEntityType(
  workflows: readonly IWorkflowSummary[]
): { entityType: WorkflowEntityType; workflows: IWorkflowSummary[] }[] {
  return WORKFLOW_ENTITY_TYPES.map((entityType) => ({
    entityType,
    workflows: workflows.filter((w) => w.entityType === entityType),
  })).filter((g) => g.workflows.length > 0);
}

// ─── Wire contract → view-model mappers (D1 admin slice 1) ────────────────────
//
// The unions above mirror the C# enums EXACTLY — `WorkflowEntityType`,
// `WorkflowStatus` and `ApproverType` all reach the wire via `.ToString()`, so the
// PascalCase member names match member-for-member (verified against
// HRM.Domain/Enums). The wire nevertheless types them `string | null`, so the
// mappers below NARROW with a runtime guard rather than an `as` cast: a member
// added to the backend enum lands on a documented default instead of silently
// claiming to be a value the FE understands (BUG-311's lesson).

export type WorkflowListItemWire = Schema<'WorkflowsWorkflowListItemDto'>;
export type WorkflowDetailWire = Schema<'WorkflowsWorkflowDetailDto'>;
export type WorkflowStepWire = Schema<'WorkflowsWorkflowStepDto'>;
export type RoleWire = Schema<'RolesRoleDto'>;
export type TenantUserPageWire = Schema<'PagedResultOfUsersTenantUserListItemDto'>;

function isWorkflowEntityType(v: string): v is WorkflowEntityType {
  return (WORKFLOW_ENTITY_TYPES as readonly string[]).includes(v);
}

function isWorkflowStatus(v: string): v is WorkflowStatus {
  return v === 'Active' || v === 'Draft' || v === 'Archived';
}

function isApproverType(v: string): v is ApproverType {
  return (APPROVER_TYPES as readonly string[]).includes(v);
}

/** Unrecognised → `'Leave'`, the first grouping bucket (AC-1 renders every group). */
export function mapWorkflowEntityType(
  wire: string | null | undefined,
): WorkflowEntityType {
  const v = wire ?? '';
  return isWorkflowEntityType(v) ? v : 'Leave';
}

/** Unrecognised → `'Draft'`, the non-live state — the conservative reading. */
export function mapWorkflowStatus(
  wire: string | null | undefined,
): WorkflowStatus {
  const v = wire ?? '';
  return isWorkflowStatus(v) ? v : 'Draft';
}

/**
 * Unrecognised → `'LineManager'`, the only approver type that needs no identifier,
 * so an unknown value cannot leave the editor showing a picker it cannot populate.
 */
export function mapApproverType(
  wire: string | null | undefined,
): ApproverType {
  const v = wire ?? '';
  return isApproverType(v) ? v : 'LineManager';
}

/** Escalation is optional — an absent/unknown wire value stays `null`, not defaulted. */
function mapOptionalApproverType(
  wire: string | null | undefined,
): ApproverType | null {
  const v = wire ?? '';
  return isApproverType(v) ? v : null;
}

export function mapWorkflowStep(w: WorkflowStepWire): IWorkflowStep {
  return {
    stepOrder: w.stepOrder ?? 0,
    approverType: mapApproverType(w.approverType),
    approverIdentifier: w.approverIdentifier ?? null,
    isParallel: w.isParallel ?? false,
    parallelApproverIdentifiers: w.parallelApproverIdentifiers ?? [],
    slaHours: w.slaHours ?? 0,
    escalationApproverType: mapOptionalApproverType(w.escalationApproverType),
    escalationApproverIdentifier: w.escalationApproverIdentifier ?? null,
    conditionJson: w.conditionJson ?? null,
    delegationEnabled: w.delegationEnabled ?? false,
    delegationBackupUserId: w.delegationBackupUserId ?? null,
  };
}

export function mapWorkflowSummary(w: WorkflowListItemWire): IWorkflowSummary {
  return {
    id: w.id ?? '',
    lineageId: w.lineageId ?? '',
    name: w.name ?? '',
    entityType: mapWorkflowEntityType(w.entityType),
    version: w.version ?? 0,
    status: mapWorkflowStatus(w.status),
    isDefault: w.isDefault ?? false,
    stepCount: w.stepCount ?? 0,
    updatedAt: w.lastModifiedAt ?? null,
    inFlightCount: w.inFlightCount ?? 0,
  };
}

/**
 * The detail DTO carries no `stepCount` (the count is `steps.length`) and no
 * `inFlightCount`; both are list-only fields, so they are simply absent here.
 */
export function mapWorkflowDefinition(
  w: WorkflowDetailWire,
): IWorkflowDefinition {
  return {
    id: w.id ?? '',
    lineageId: w.lineageId ?? '',
    name: w.name ?? '',
    entityType: mapWorkflowEntityType(w.entityType),
    version: w.version ?? 0,
    status: mapWorkflowStatus(w.status),
    isDefault: w.isDefault ?? false,
    steps: (w.steps ?? []).map(mapWorkflowStep),
  };
}

/** The roles endpoint's DTO projected onto the approver-picker option. */
export function mapApproverRoleOption(w: RoleWire): IApproverRoleOption {
  return { id: w.id ?? '', name: w.name ?? '' };
}

/** RENAME: the picker keys a user option by `id`; the wire calls it `userTenantId`. */
export function mapApproverUserOptions(
  w: TenantUserPageWire,
): IApproverUserOption[] {
  return (w.items ?? []).map((u) => ({
    id: u.userTenantId ?? '',
    displayName: u.displayName ?? '',
    email: u.email ?? '',
  }));
}
