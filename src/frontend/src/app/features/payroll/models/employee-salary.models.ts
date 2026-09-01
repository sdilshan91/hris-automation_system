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

import type { Schema } from '@core/api';

import { COMPONENT_TYPE_OPTIONS, SalaryComponentType } from './payroll.models';

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
  /**
   * GAP-010: the wire name is `employees`, not `rows`. Sending `rows` meant the API bound an EMPTY
   * collection and the bulk assign silently did nothing for every row.
   */
  employees: IBulkAssignmentRow[];
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

// ─── Wire contract → view-model mappers (D1 payroll slice) ───────────────────
//
// `http.post<ICtcBreakdown>(…)` was an unchecked ASSERTION. Three of the five payloads on this surface
// use DIFFERENT field names from the view-models below, so the compiler was agreeing with a shape the
// server has never sent. These aliases bind the view-models to the GENERATED contract, and every rename
// is done explicitly in a mapper rather than by renaming a view-model field.
//
// RENAMES (wire → view-model) — never "fixed" by renaming the view-model, always mapped:
//   PayrollCtcBreakdownDto.components            → ICtcBreakdown.lines
//   PayrollCtcBreakdownDto.totalAnnualEarnings   → ICtcBreakdown.totalAnnual
//   PayrollCtcBreakdownDto.totalMonthlyEarnings  → ICtcBreakdown.totalMonthly
//   PayrollEmployeeCompensationDto.components    → IEmployeeCompensation.lines
//   PayrollSalaryRevisionDto.id                  → ISalaryRevision.revisionId
//   PayrollBulkAssignResultDto.succeededCount    → IBulkAssignmentResult.successCount
//   PayrollBulkAssignResultDto.failedCount       → IBulkAssignmentResult.failureCount
//
// DEFAULTING POLICY (this surface defines an employee's actual PAY — a default is a decision):
//  - Money that the view-model can express as UNKNOWN (`number | null`, i.e. every field on
//    IEmployeeCompensation) defaults to NULL. The Compensation tab renders "no value" differently from
//    zero (BR-5 "Payroll Incomplete"), so a fabricated 0 would be a wrong salary shown to an employee.
//  - Money the view-model types as a plain `number` defaults to 0 ONLY where the schema marks the field
//    non-nullable and the server always computes it — each such case says so at the line.
//  - Flags (`balanced`, `isOverride`, `success`) default to FALSE. Failing closed here means: do not
//    claim a CTC reconciles, do not label a calculated line an HR override, do not report an assignment
//    as succeeded.
//  - Fields with NO WIRE SOURCE are marked `⚠ NO WIRE SOURCE` at the line and flagged in the D1 report.
//    They are not quietly nulled.

export type CtcBreakdownWire = Schema<'PayrollCtcBreakdownDto'>;
export type CtcComponentLineWire = Schema<'PayrollCtcComponentLineDto'>;
export type EmployeeCompensationWire = Schema<'PayrollEmployeeCompensationDto'>;
export type SalaryRevisionWire = Schema<'PayrollSalaryRevisionDto'>;
export type BulkAssignResultWire = Schema<'PayrollBulkAssignResultDto'>;
export type BulkAssignResultItemWire = Schema<'PayrollBulkAssignResultItemDto'>;

/**
 * Narrow the CTC line's component type.
 *
 * Unlike `PayrollSalaryComponentDto.type`, the wire field here is a bare `string`, not the
 * `SalaryComponentType` enum — the backend builds it with `component?.Type.ToString() ?? string.Empty`
 * (`SalaryAssignmentService.cs`), so an EMPTY STRING is a genuinely reachable value when the component
 * lookup misses, not just a generator artifact.
 *
 * A value the FE does not recognise is passed through as `''` rather than coerced to a member: `type`
 * decides whether a line ADDS to or SUBTRACTS from pay, so guessing it is a wrong claim about money.
 * Nothing currently renders this field, and the `COMPONENT_TYPE_BADGE` lookup degrades to an unstyled
 * cell, so the failure is visible rather than confidently wrong.
 */
function componentTypeFromWire(
  raw: string | null | undefined,
): SalaryComponentType {
  const value = raw ?? '';
  return (
    COMPONENT_TYPE_OPTIONS.includes(value as SalaryComponentType) ? value : ''
  ) as SalaryComponentType;
}

export function mapSalaryComponentLine(
  w: CtcComponentLineWire,
): ISalaryComponentLine {
  return {
    salaryComponentId: w.salaryComponentId ?? '',
    componentName: w.componentName ?? '',
    componentType: componentTypeFromWire(w.componentType),
    // Both amounts are non-nullable `decimal` on the wire and always computed server-side, and the
    // view-model types them as plain `number` (the Compensation tab SUMS them). `?? 0` therefore fills a
    // Swashbuckle optionality artifact, not a real "amount unknown" state.
    annualAmount: w.annualAmount ?? 0,
    monthlyAmount: w.monthlyAmount ?? 0,
    // Fail CLOSED: never label a server-calculated line as a manual HR override (AC-3).
    isOverride: w.isOverride ?? false,
  };
}

export function mapCtcBreakdown(w: CtcBreakdownWire): ICtcBreakdown {
  return {
    // Non-nullable on the wire, echoed straight back from the request — `?? 0` is artifact-filling.
    annualCtc: w.annualCtc ?? 0,
    // RENAME: `totalAnnualEarnings` → `totalAnnual`. Note the wire name is narrower than the view-model
    // name suggests — it is the EARNINGS total, not net of deductions.
    totalAnnual: w.totalAnnualEarnings ?? 0,
    // RENAME: `totalMonthlyEarnings` → `totalMonthly`.
    totalMonthly: w.totalMonthlyEarnings ?? 0,
    // Fail CLOSED: an absent FR-6 reconciliation flag must not tell HR the CTC balances.
    balanced: w.balanced ?? false,
    // RENAME: `components` → `lines`.
    lines: (w.components ?? []).map(mapSalaryComponentLine),
  };
}

/**
 * Map the response of `POST /payroll/salary-assignments`.
 *
 * ⚠ CONTRACT MISMATCH: the endpoint returns a bare `PayrollCtcBreakdownDto` — there is no
 * assignment-result DTO on the API at all. `employeeId` and `effectiveFrom` have NO WIRE SOURCE and are
 * mapped to `''` because that is the only honest value; they are NOT server-confirmed echoes of what was
 * saved. The current caller (`employee-compensation.component.ts` `confirmAssign`) ignores the result
 * entirely and re-fetches, so nothing renders these today — but the view-model is a promise the API does
 * not keep. Flagged in the D1 report.
 */
export function mapSalaryAssignmentResult(
  w: CtcBreakdownWire,
): ISalaryAssignmentResult {
  return {
    // ⚠ NO WIRE SOURCE — the breakdown carries no employee id.
    employeeId: '',
    salaryStructureId: w.salaryStructureId ?? '',
    annualCtc: w.annualCtc ?? 0,
    // ⚠ NO WIRE SOURCE — the breakdown carries no effective date.
    effectiveFrom: '',
    breakdown: mapCtcBreakdown(w),
  };
}

/**
 * Map `GET /payroll/employees/{id}/compensation`.
 *
 * Every money/structure field is `… | null` in the view-model and defaults to NULL, not 0/'': the tab
 * distinguishes "no structure assigned yet" (BR-5, "Payroll Incomplete") from a zero salary, and a
 * fabricated 0 here is a wrong number about someone's pay.
 *
 * Note the API models "no compensation" as a 404, not a 200 with nulls — the nullable view-model is
 * defensive, and the caller's error branch is what actually drives BR-5 today.
 */
export function mapEmployeeCompensation(
  w: EmployeeCompensationWire,
): IEmployeeCompensation {
  return {
    employeeId: w.employeeId ?? '',
    salaryStructureId: w.salaryStructureId ?? null,
    salaryStructureName: w.salaryStructureName ?? null,
    annualCtc: w.annualCtc ?? null,
    monthlyCtc: w.monthlyCtc ?? null,
    effectiveFrom: w.effectiveFrom ?? null,
    // RENAME: `components` → `lines`.
    lines: (w.components ?? []).map(mapSalaryComponentLine),
  };
}

/**
 * Map one row of `GET /payroll/employees/{id}/revision-history`.
 *
 * ⚠ THREE view-model fields have NO WIRE SOURCE. `PayrollSalaryRevisionDto` sends ids only
 * (`oldStructureId`, `newStructureId`, `changedBy`) and no display names:
 *   - `newStructureName` is the TITLE of every timeline row and will render EMPTY;
 *   - `oldStructureName` feeds the expanded "before" cell, which falls back to "None" — so a real
 *     previous structure reads as "None";
 *   - `changedByName` feeds an `@if`, so the "· by <person>" suffix simply never appears.
 * They are mapped to the least-claiming value rather than invented, and flagged in the D1 report. The
 * fix is either a backend DTO change or a client-side join against the structures list — both outside
 * this task's lane.
 */
export function mapSalaryRevision(w: SalaryRevisionWire): ISalaryRevision {
  return {
    // RENAME: `id` → `revisionId` (also the `@for` track key on the timeline).
    revisionId: w.id ?? '',
    oldStructureId: w.oldStructureId ?? null,
    // ⚠ NO WIRE SOURCE — the DTO sends `oldStructureId` only.
    oldStructureName: null,
    // Schema-nullable: null genuinely means "no previous CTC" (first assignment), not "unknown".
    oldAnnualCtc: w.oldAnnualCtc ?? null,
    newStructureId: w.newStructureId ?? '',
    // ⚠ NO WIRE SOURCE — the DTO sends `newStructureId` only. Renders as an empty row title.
    newStructureName: '',
    // Non-nullable `decimal` on the wire and always written when a revision is recorded; the view-model
    // types it as a plain `number` and the timeline renders it as currency, so `?? 0` fills the
    // Swashbuckle artifact rather than inventing a salary.
    newAnnualCtc: w.newAnnualCtc ?? 0,
    effectiveFrom: w.effectiveFrom ?? '',
    reason: w.reason ?? null,
    // ⚠ NO WIRE SOURCE — the DTO sends `changedBy` (a user UUID), not a display name.
    changedByName: null,
    changedAt: w.changedAt ?? '',
  };
}

export function mapBulkAssignmentItemResult(
  w: BulkAssignResultItemWire,
): IBulkAssignmentItemResult {
  return {
    employeeId: w.employeeId ?? '',
    // Fail CLOSED: an absent flag must not report an employee's salary as successfully assigned.
    success: w.success ?? false,
    error: w.error ?? null,
  };
}

export function mapBulkAssignmentResult(
  w: BulkAssignResultWire,
): IBulkAssignmentResult {
  return {
    // Server-computed counters, non-nullable on the wire; they drive a progress indicator, not money.
    totalRequested: w.totalRequested ?? 0,
    // RENAME: `succeededCount` → `successCount`.
    successCount: w.succeededCount ?? 0,
    // RENAME: `failedCount` → `failureCount`.
    failureCount: w.failedCount ?? 0,
    results: (w.results ?? []).map(mapBulkAssignmentItemResult),
  };
}
