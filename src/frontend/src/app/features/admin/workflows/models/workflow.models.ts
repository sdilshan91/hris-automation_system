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
  id: string;
  name: string;
  entityType: WorkflowEntityType;
  version: number;
  status: WorkflowStatus;
  /** Seeded-default workflow, editable but badged "Default" (AC-1). */
  isDefault: boolean;
  stepCount: number;
  updatedAt: string;
  /** US-ADM-011c FR-10: count of live (InProgress) runtime instances on this lineage. */
  inFlightCount?: number;
}

/** Full workflow definition with its ordered steps (editor payload, AC-2). */
export interface IWorkflowDefinition {
  id: string;
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
