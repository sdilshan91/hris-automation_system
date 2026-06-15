/**
 * US-PAY-002: Models for assigning a salary structure to an employee — the CTC
 * breakdown preview, the assignment payload, salary revision history, and bulk
 * assignment.
 *
 * Backend endpoints (assumed contract — backend agent building in parallel; the
 * service layer is intentionally thin so a route mismatch is a one-file fix).
 * `apiBaseUrl` already includes `/api/v1`, so the resource is
 * `${apiBaseUrl}/payroll/...`:
 *   POST   /payroll/salary-assignments/preview                  - CTC breakdown preview (FR-3)
 *   POST   /payroll/salary-assignments                          - assign to one employee (FR-1)
 *   POST   /payroll/salary-assignments/bulk                     - bulk assign (FR-5)
 *   GET    /payroll/employees/:employeeId/compensation          - current compensation (§8)
 *   GET    /payroll/employees/:employeeId/revision-history      - salary revision history (FR-4)
 *
 * All requests are tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain
 * header) and use withCredentials for the httpOnly cookie auth. The backend
 * stamps tenant_id + audit fields and enforces RLS (FR-8, AC-5) — the FE never
 * sends them.
 *
 * ENVELOPE: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so the service consumes BARE payloads.
 *
 * ENUM CASING (US-PLT-003 — critical): enums are PascalCase string unions
 * matching the C# member names (global JsonStringEnumConverter); they arrive as
 * STRINGS, never integers.
 */

import { SalaryComponentType } from './payroll.models';

// ─── CTC breakdown line ────────────────────────────────────────

/**
 * One computed line of a CTC breakdown (FR-2, §8). Returned by the preview
 * endpoint and stored in the current compensation. `isOverride` marks a value
 * the HR officer set manually instead of the structure's calculated value (AC-3).
 */
export interface ISalaryComponentLine {
  salaryComponentId: string;
  componentName: string;
  componentType: SalaryComponentType;
  annualAmount: number;
  monthlyAmount: number;
  isOverride: boolean;
}

/**
 * Full CTC breakdown preview (FR-3, AC-1). The server is the source of truth for
 * the calculation; `totalAnnual` / `totalMonthly` are the summed totals it
 * computed, and `balanced` reflects the FR-6 tolerance check (sum == declared CTC
 * within tolerance) so the UI can warn before confirm.
 */
export interface ICtcBreakdown {
  annualCtc: number;
  totalAnnual: number;
  totalMonthly: number;
  /** FR-6: server-side check that component sum reconciles to declared CTC. */
  balanced: boolean;
  lines: ISalaryComponentLine[];
}

// ─── Assignment ────────────────────────────────────────────────

/** A single per-component override the HR officer entered (AC-3). */
export interface IComponentOverride {
  salaryComponentId: string;
  /** The custom ANNUAL amount that supersedes the calculated value. */
  annualAmount: number;
}

/**
 * Request to preview a CTC breakdown (FR-3) and to assign (FR-1). The same shape
 * backs both endpoints so the previewed numbers are exactly what gets saved.
 * `reason` is optional and only meaningful on assign (revision history, BR-3).
 */
export interface ISalaryAssignmentRequest {
  employeeId: string;
  salaryStructureId: string;
  annualCtc: number;
  /** ISO date (yyyy-MM-dd). Future dates schedule the change (AC-2, BR-2). */
  effectiveFrom: string;
  overrides: IComponentOverride[];
  reason?: string | null;
}

/** Result of a single assignment (FR-1). Echoes the persisted breakdown. */
export interface ISalaryAssignmentResult {
  employeeId: string;
  salaryStructureId: string;
  annualCtc: number;
  effectiveFrom: string;
  breakdown: ICtcBreakdown;
}

// ─── Current compensation (§8 Compensation tab) ────────────────

/**
 * The employee's CURRENT active compensation shown on the Compensation tab (§8).
 * `null`-able fields cover BR-5: an employee with no structure assigned is
 * flagged "Payroll Incomplete".
 */
export interface IEmployeeCompensation {
  employeeId: string;
  /** null when no structure has been assigned yet (BR-5 "Payroll Incomplete"). */
  salaryStructureId: string | null;
  salaryStructureName: string | null;
  annualCtc: number | null;
  monthlyCtc: number | null;
  effectiveFrom: string | null;
  lines: ISalaryComponentLine[];
}

// ─── Salary revision history (FR-4, BR-3) ──────────────────────

/**
 * One entry in the salary revision timeline (FR-4, §8). Captures the before/after
 * the expandable row compares (BR-3): old vs new structure + CTC, effective date,
 * who changed it and when, and the reason.
 */
export interface ISalaryRevision {
  revisionId: string;
  oldStructureId: string | null;
  oldStructureName: string | null;
  oldAnnualCtc: number | null;
  newStructureId: string;
  newStructureName: string;
  newAnnualCtc: number;
  effectiveFrom: string;
  reason: string | null;
  changedByName: string | null;
  changedAt: string;
}

// ─── Bulk assignment (FR-5, AC-4) ──────────────────────────────

/** One row of a bulk assignment (FR-5): an employee + their individual CTC. */
export interface IBulkAssignmentRow {
  employeeId: string;
  annualCtc: number;
}

/** Bulk assignment request (FR-5, AC-4): one structure, per-employee CTCs. */
export interface IBulkAssignmentRequest {
  salaryStructureId: string;
  effectiveFrom: string;
  rows: IBulkAssignmentRow[];
}

/** Per-row outcome of a bulk assignment so the UI can show success/failure. */
export interface IBulkAssignmentItemResult {
  employeeId: string;
  success: boolean;
  /** Human-readable failure reason when success === false. */
  error?: string | null;
}

/** Aggregate result of a bulk assignment, drives the progress indicator (AC-4). */
export interface IBulkAssignmentResult {
  totalRequested: number;
  successCount: number;
  failureCount: number;
  results: IBulkAssignmentItemResult[];
}
