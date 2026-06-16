/**
 * US-PAY-012: Models for the payroll HISTORY list and the AUDIT TRAIL.
 *
 * This story has two read surfaces:
 *  - Payroll History (AC-1): a chronological list of all payroll runs with the
 *    audit columns (period, status, employee count, total net, initiated by,
 *    approved by, finalized date), sortable + filterable by year/status.
 *  - Audit Trail (AC-2/AC-4, FR-4/FR-6/FR-8): a vertical timeline of every
 *    payroll-related action (config changes, run events, approvals, adjustments,
 *    payslip generation, email distribution) with a per-change before/after DIFF
 *    view, a filter bar (date range, action type, actor, resource type), and a
 *    CSV/Excel export (FR-5).
 *
 * Backend contract (REAL, reconciled to PayrollAuditController / PayrollAuditDtos):
 *  - history items are `PayrollRunHistoryItemDto` (runId, period, status, counts,
 *    totals, initiatedBy/approvedBy GUIDs + their timestamps, finalizedAt);
 *  - audit entries are `PayrollAuditEntryDto` (timestamp, actorUserId,
 *    actorEmployeeNo, action, resourceType, resourceId, before/after as raw JSON
 *    STRINGS, ipAddress, userAgent, traceId).
 * Route strings live ONLY in `audit.service.ts`; the action-name values below
 * mirror the §7 "Payroll Action Types" table in the story.
 *
 * ENVELOPE: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so services consume the BARE page payload
 * (`{ items, totalCount, page, pageSize }`). Binary exports use
 * `responseType: 'blob'` + `observe: 'response'` and bypass the envelope.
 *
 * ENUM CASING (US-PLT-003): `action` / `resourceType` are PascalCase wire strings
 * matching the C# audit_log column values; they arrive as STRINGS.
 */

import { PayrollRunStatus } from './payroll-run.models';

// ─── Page envelope (the BE returns paginated lists) ────────────

/**
 * A paginated list page as returned by the BE (`PayrollRunHistoryPageDto` /
 * `PayrollAuditPageDto`): `{ items, totalCount, page, pageSize }`. The FE table
 * reads `.items`; the page meta backs paging when added.
 */
export interface IPage<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// ─── Payroll History (AC-1, PayrollRunHistoryItemDto) ──────────

/**
 * One payroll run as shown in the Payroll History table (AC-1), matching
 * `PayrollRunHistoryItemDto`. `initiatedBy` / `approvedBy` are user GUIDs (the BE
 * does not resolve names here) — the table displays the id. `approvedBy` /
 * `approvedAt` / `finalizedAt` are null until the run is approved / finalized.
 */
export interface IPayrollHistoryRun {
  runId: string;
  payMonth: number;
  payYear: number;
  /** "YYYY-MM" period string from the BE. */
  period: string;
  status: PayrollRunStatus;
  employeeCount: number;
  totalNet: number;
  totalGross: number;
  totalDeductions: number;
  /** Initiating user GUID (not a display name). */
  initiatedBy: string;
  initiatedAt: string;
  /** Approving user GUID; null until approved. */
  approvedBy: string | null;
  approvedAt: string | null;
  /** ISO timestamp; null until the run is finalized (AC-1 finalized date). */
  finalizedAt: string | null;
}

// ─── Audit action / resource taxonomy (§7) ─────────────────────

/**
 * Standardized payroll audit action names (§7 "Payroll Action Types"). PascalCase
 * `{Resource}.{Verb}` wire strings — the source of truth for the action-type
 * filter dropdown (FR-4) and the timeline badge colours.
 */
export type PayrollAuditAction =
  | 'SalaryComponent.Created'
  | 'SalaryComponent.Updated'
  | 'SalaryComponent.Deleted'
  | 'SalaryStructure.Created'
  | 'SalaryStructure.Updated'
  | 'EmployeeSalary.Assigned'
  | 'EmployeeSalary.Revised'
  | 'StatutoryRule.Created'
  | 'StatutoryRule.Updated'
  | 'PayrollRun.Initiated'
  | 'PayrollRun.Completed'
  | 'PayrollRun.SubmittedForApproval'
  | 'PayrollRun.Approved'
  | 'PayrollRun.Rejected'
  | 'PayrollRun.Finalized'
  | 'PayrollRun.Cancelled'
  | 'PayrollAdjustment.Created'
  | 'PayrollAdjustment.Cancelled'
  | 'PayslipPDF.Generated'
  | 'PayslipEmail.Sent';

/**
 * The resource types an audit entry can target (§7 resource_type). Used by the
 * resource-type filter (FR-4). The wire is an open string (the backend may add
 * resource types), so the entry's `resourceType` is typed as a plain string and
 * this list only drives the filter dropdown.
 */
export const AUDIT_RESOURCE_TYPES = [
  'PayrollRun',
  'SalaryComponent',
  'SalaryStructure',
  'EmployeeSalary',
  'StatutoryRule',
  'PayrollAdjustment',
  'PayrollSlip',
] as const;

/** The action-type options for the FR-4 dropdown, grouped for readability. */
export const AUDIT_ACTION_OPTIONS: { value: PayrollAuditAction; label: string }[] = [
  { value: 'SalaryComponent.Created', label: 'Salary component created' },
  { value: 'SalaryComponent.Updated', label: 'Salary component updated' },
  { value: 'SalaryComponent.Deleted', label: 'Salary component deleted' },
  { value: 'SalaryStructure.Created', label: 'Salary structure created' },
  { value: 'SalaryStructure.Updated', label: 'Salary structure updated' },
  { value: 'EmployeeSalary.Assigned', label: 'Salary assigned' },
  { value: 'EmployeeSalary.Revised', label: 'Salary revised' },
  { value: 'StatutoryRule.Created', label: 'Statutory rule created' },
  { value: 'StatutoryRule.Updated', label: 'Statutory rule updated' },
  { value: 'PayrollRun.Initiated', label: 'Run initiated' },
  { value: 'PayrollRun.Completed', label: 'Run completed' },
  { value: 'PayrollRun.SubmittedForApproval', label: 'Run submitted for approval' },
  { value: 'PayrollRun.Approved', label: 'Run approved' },
  { value: 'PayrollRun.Rejected', label: 'Run rejected' },
  { value: 'PayrollRun.Finalized', label: 'Run finalized' },
  { value: 'PayrollRun.Cancelled', label: 'Run cancelled' },
  { value: 'PayrollAdjustment.Created', label: 'Adjustment created' },
  { value: 'PayrollAdjustment.Cancelled', label: 'Adjustment cancelled' },
  { value: 'PayslipPDF.Generated', label: 'Payslips generated' },
  { value: 'PayslipEmail.Sent', label: 'Payslips emailed' },
];

/**
 * Timeline node colour for an action, keyed by the resource prefix of the action
 * (so a new action verb still gets a sensible colour). Returns a Tailwind bg
 * class for the timeline dot. Falls back to neutral for unknown resources.
 */
export function auditDotClass(action: string): string {
  const resource = action.split('.')[0];
  switch (resource) {
    case 'PayrollRun':
      return 'bg-violet-500';
    case 'SalaryComponent':
    case 'SalaryStructure':
      return 'bg-sky-500';
    case 'EmployeeSalary':
      return 'bg-emerald-500';
    case 'StatutoryRule':
      return 'bg-indigo-500';
    case 'PayrollAdjustment':
      return 'bg-amber-500';
    case 'PayslipPDF':
    case 'PayslipEmail':
      return 'bg-teal-500';
    default:
      return 'bg-neutral-400';
  }
}

/**
 * Human-readable label for an action wire string. Uses the explicit option label
 * when known, else de-camelCases the verb (e.g. "PayrollRun.SubmittedForApproval"
 * → "Payroll run · Submitted for approval") so unknown future actions still read.
 */
export function auditActionLabel(action: string): string {
  const known = AUDIT_ACTION_OPTIONS.find((o) => o.value === action);
  if (known) {
    return known.label;
  }
  const [resource, verb] = action.split('.');
  const space = (s: string) =>
    (s ?? '').replace(/([a-z])([A-Z])/g, '$1 $2').replace(/\./g, ' ');
  return verb ? `${space(resource)} · ${space(verb)}` : space(action);
}

// ─── Audit entry + diff (FR-3, FR-8) ───────────────────────────

/**
 * A single audit_log entry, matching `PayrollAuditEntryDto`. `before` / `after`
 * are the raw JSON snapshot STRINGS the backend stored (parsed by
 * {@link parseSnapshot} for the diff view); either may be null (null `before` =
 * create, null `after` = delete). `actorUserId` is the actor's user GUID (the BE
 * does not resolve a display name here); `actorEmployeeNo` is shown when present.
 * `ipAddress` / `userAgent` back the non-repudiation columns (BR-5). The FE never
 * relies on `tenantId` for scoping — it is enforced server-side by RLS (AC-5).
 */
export interface IAuditEntry {
  id: string;
  tenantId?: string | null;
  timestamp: string;
  actorUserId: string | null;
  actorEmployeeNo: string | null;
  action: string;
  resourceType: string | null;
  resourceId: string | null;
  /** Raw JSON snapshot string; null for a create. */
  before: string | null;
  /** Raw JSON snapshot string; null for a delete. */
  after: string | null;
  ipAddress: string | null;
  userAgent: string | null;
  traceId: string | null;
}

/**
 * Parse a raw before/after JSON snapshot STRING into an object for the diff view.
 * Guards malformed/non-object JSON: returns null on parse failure or when the
 * parsed value isn't a plain object (so {@link buildDiff} treats it as absent).
 */
export function parseSnapshot(
  raw: string | null | undefined,
): Record<string, unknown> | null {
  if (raw === null || raw === undefined || raw === '') {
    return null;
  }
  try {
    const parsed = JSON.parse(raw);
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
      ? (parsed as Record<string, unknown>)
      : null;
  } catch {
    return null;
  }
}

/** The kind of change a diff row represents (drives the green/red/amber colour). */
export type DiffKind = 'added' | 'removed' | 'modified' | 'unchanged';

/** One field row in the side-by-side before/after diff view (FR-8). */
export interface IDiffRow {
  field: string;
  before: string | null;
  after: string | null;
  kind: DiffKind;
}

/**
 * Build the side-by-side before/after diff rows for one audit entry (FR-8). Pure
 * + isolated from the component so it is trivially unit-testable.
 *
 * - A key present only in `after`  → 'added'   (green).
 * - A key present only in `before` → 'removed' (red).
 * - A key in both with different values → 'modified' (amber).
 * - A key in both with equal values → 'unchanged'.
 *
 * Keys are the union of both objects, sorted, so the two columns line up. Values
 * are stringified for display (objects/arrays via JSON, null → null sentinel).
 * Returns [] when there is nothing to compare (both null) — a create with only
 * `after` still yields rows (all 'added'), and a delete with only `before` yields
 * rows (all 'removed').
 */
export function buildDiff(
  before: Record<string, unknown> | null | undefined,
  after: Record<string, unknown> | null | undefined,
): IDiffRow[] {
  const b = before ?? {};
  const a = after ?? {};
  const keys = Array.from(new Set([...Object.keys(b), ...Object.keys(a)])).sort();
  const rows: IDiffRow[] = [];
  for (const field of keys) {
    const hasB = Object.prototype.hasOwnProperty.call(b, field);
    const hasA = Object.prototype.hasOwnProperty.call(a, field);
    const bv = hasB ? stringifyValue(b[field]) : null;
    const av = hasA ? stringifyValue(a[field]) : null;
    let kind: DiffKind;
    if (hasB && !hasA) {
      kind = 'removed';
    } else if (!hasB && hasA) {
      kind = 'added';
    } else if (bv !== av) {
      kind = 'modified';
    } else {
      kind = 'unchanged';
    }
    rows.push({ field, before: bv, after: av, kind });
  }
  return rows;
}

/** Whether an audit entry has any before/after JSON worth diffing (FR-8). */
export function hasDiff(entry: IAuditEntry): boolean {
  return !!parseSnapshot(entry.before) || !!parseSnapshot(entry.after);
}

/** Stringify a JSON value for the diff display (objects → compact JSON). */
function stringifyValue(value: unknown): string {
  if (value === null || value === undefined) {
    return '—';
  }
  if (typeof value === 'object') {
    return JSON.stringify(value);
  }
  return String(value);
}

/** Tailwind classes for a diff row's colour by change kind (FR-8). */
export const DIFF_KIND_CLASS: Record<DiffKind, string> = {
  added: 'bg-emerald-50 text-emerald-800',
  removed: 'bg-rose-50 text-rose-800',
  modified: 'bg-amber-50 text-amber-800',
  unchanged: 'text-neutral-600',
};

// ─── Audit trail query (FR-4) + export (FR-5) ──────────────────

/**
 * The audit-trail filter bar (FR-4), matching `PayrollAuditTrailFilter`. All
 * fields optional; the service omits any empty param so the backend applies no
 * filter. `fromUtc` / `toUtc` are ISO date strings (YYYY-MM-DD); `action` is one
 * of {@link PayrollAuditAction}; `resourceType` is one of
 * {@link AUDIT_RESOURCE_TYPES}; `actorUserId` is an actor user GUID; `resourceId`
 * scopes to a single resource.
 */
export interface IAuditTrailFilters {
  fromUtc: string | null;
  toUtc: string | null;
  action: string | null;
  actorUserId: string | null;
  resourceType: string | null;
  resourceId: string | null;
}

/** A blank filter set (the page's initial state). */
export function emptyAuditFilters(): IAuditTrailFilters {
  return {
    fromUtc: null,
    toUtc: null,
    action: null,
    actorUserId: null,
    resourceType: null,
    resourceId: null,
  };
}

/** Export formats for the audit trail (FR-5). Lowercase wire values (BE accepts csv|xlsx|pdf). */
export type AuditExportFormat = 'csv' | 'xlsx' | 'pdf';

/** Month dropdown options for the history year filter are derived from the data. */
