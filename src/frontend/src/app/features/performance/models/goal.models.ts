/**
 * US-PRF-001: Performance goal-setting models matching the backend API contract.
 *
 * Backend endpoints (ASSUMED contract — backend agent building in parallel; the
 * service layer is intentionally thin so a route mismatch is a one-file fix):
 *   GET    /api/v1/performance/cycles/active                                  - current open cycle + window state
 *   GET    /api/v1/performance/cycles/:cycleId/team                           - manager's team members + goal-setting status (AC-4)
 *   GET    /api/v1/performance/cycles/:cycleId/employees/:employeeId/goals    - goals for one employee in a cycle (AC-1)
 *   PUT    /api/v1/performance/cycles/:cycleId/employees/:employeeId/goals    - save (replace) the full goal set (AC-2)
 *
 * NOTE: `apiBaseUrl` already includes `/api/v1`, so the resource is
 * `${apiBaseUrl}/performance/...`. All requests are tenant-scoped via the
 * tenantInterceptor (X-Tenant-Subdomain header) and use withCredentials for the
 * httpOnly cookie auth. The backend stamps tenant_id + audit fields server-side
 * (§7) and enforces RLS + the Performance.SetGoal.Team permission (BR-4, NFR-2).
 *
 * ENUM CASING (US-PLT-003 — critical): every enum is a PascalCase string union
 * matching the C# member names (global JsonStringEnumConverter). The API returns
 * enums as STRINGS, never integers. The global ApiResponse unwrap interceptor
 * (US-PLT-001) already strips the `{ data }` envelope — services consume BARE
 * payloads. Do NOT double-unwrap.
 */

import type { Schema } from '@core/api';

// ─── Enums ────────────────────────────────────────────────────

/** Goal category (FR-2, §7). Matches C# `GoalCategory`. */
export type GoalCategory = 'KPI' | 'Competency' | 'Project';

export const GOAL_CATEGORY_OPTIONS: GoalCategory[] = [
  'KPI',
  'Competency',
  'Project',
];

/** Tailwind badge classes per category (§8). Single source of truth. */
export const GOAL_CATEGORY_BADGE: Record<GoalCategory, string> = {
  KPI: 'bg-indigo-50 text-indigo-700 ring-indigo-600/20',
  Competency: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  Project: 'bg-amber-50 text-amber-700 ring-amber-600/20',
};

/**
 * Goal-setting status of an employee within a cycle (AC-4). Matches C#
 * `GoalSettingStatus`. `Draft` = manager started, not submitted; `Submitted` =
 * goals assigned to the employee; `Acknowledged` = employee accepted (BR-5).
 */
export type GoalSettingStatus =
  | 'NotStarted'
  | 'Draft'
  | 'Submitted'
  | 'Acknowledged';

export const GOAL_STATUS_BADGE: Record<GoalSettingStatus, string> = {
  NotStarted: 'bg-neutral-100 text-neutral-600 ring-neutral-500/20',
  Draft: 'bg-amber-50 text-amber-700 ring-amber-600/20',
  Submitted: 'bg-sky-50 text-sky-700 ring-sky-600/20',
  Acknowledged: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
};

export const GOAL_STATUS_LABEL: Record<GoalSettingStatus, string> = {
  NotStarted: 'Not started',
  Draft: 'Draft',
  Submitted: 'Submitted',
  Acknowledged: 'Acknowledged',
};

/**
 * BUG-056: lifecycle status of a SINGLE persisted goal (distinct from the
 * cycle-level `GoalSettingStatus` above). `Finalized` is the new locked terminal
 * value — once a manager/HR finalizes an employee's goal set for a cycle (weights
 * summing to exactly 100%), the backend stamps every goal `Finalized` and the set
 * becomes read-only. Tolerant of unknown/future values (US-PLT-003): only the exact
 * `Finalized` string locks the UI; any other value is treated as still-editable.
 */
export type GoalRecordStatus = 'Draft' | 'Active' | 'Finalized' | (string & {});

/** The one status value that locks a goal set (BUG-056). Single source of truth. */
export const GOAL_FINALIZED_STATUS = 'Finalized';

// ─── DTOs ─────────────────────────────────────────────────────

/** A single persisted goal (§7 output). */
export interface IGoal {
  id: string;
  title: string;
  description: string;
  category: GoalCategory;
  /** Whole-percent weight, always a multiple of 5 (BR-3). */
  weight: number;
  targetValue: string;
  measurementUnit: string;
  /** ISO date (yyyy-MM-dd). */
  dueDate: string;
  /** Optional parent goal for cascading (FR-4). Not edited in this story's core UI. */
  parentGoalId?: string | null;
  /**
   * BUG-056: lifecycle status of the goal. When `Finalized`, the whole set is locked
   * (read-only) in the UI. May be absent on payloads predating the finalize feature.
   */
  status?: GoalRecordStatus;
}

/** One goal row sent to the server on save (no id for new rows). */
export interface IGoalInput {
  /** Present when editing an existing goal; omitted/undefined for a new one. */
  id?: string;
  title: string;
  description: string;
  category: GoalCategory;
  weight: number;
  targetValue: string;
  measurementUnit: string;
  dueDate: string;
  parentGoalId?: string | null;
}

/** Save request: the FULL goal set for an employee within a cycle (AC-2). */
export interface ISaveGoalsRequest {
  goals: IGoalInput[];
}

/**
 * Active appraisal cycle + goal-setting window state (drives AC-1 / AC-5).
 * `goalSettingOpen` is the authoritative gate computed by the backend from the
 * configured window dates (BR-1); the FE shows read-only when it is false.
 */
export interface IAppraisalCycle {
  id: string;
  name: string;
  /** ISO date the goal-setting window opens. */
  goalSettingOpensOn: string;
  /** ISO date the goal-setting window closes. */
  goalSettingClosesOn: string;
  /** Authoritative open/closed gate (AC-5). */
  goalSettingOpen: boolean;
}

/** One team member row on the dashboard (AC-4). */
export interface ITeamGoalStatus {
  employeeId: string;
  employeeName: string;
  jobTitle?: string | null;
  status: GoalSettingStatus;
  goalCount: number;
  /** Sum of assigned weights (0-100) for the progress indicator. */
  totalWeight: number;
}

// ─── Validation constants (mirror the backend) ────────────────

export const GOAL_TITLE_MAX = 200;
export const GOAL_DESCRIPTION_MAX = 2000;
export const GOAL_WEIGHT_STEP = 5;
export const GOAL_MIN_COUNT = 1;
export const GOAL_MAX_COUNT = 10;
export const GOAL_WEIGHT_TOTAL = 100;

/**
 * Pure helper (testable without a component): true when the supplied weights sum
 * to exactly 100% (AC-3 / FR-3). Empty list is NOT valid (a min of 1 goal is
 * required, BR-2). Tolerant of floating noise though weights are whole multiples
 * of 5.
 */
export function weightsTotalCorrect(weights: number[]): boolean {
  if (weights.length === 0) {
    return false;
  }
  const sum = weights.reduce((acc, w) => acc + (Number(w) || 0), 0);
  return Math.abs(sum - GOAL_WEIGHT_TOTAL) < 0.001;
}

/**
 * BUG-056: a goal set is locked when it has been finalized. The backend marks every
 * goal in a finalized set with status === 'Finalized'; the FE treats the set as
 * read-only when ANY goal carries that status (a finalized set locks all rows). An
 * empty set is never locked.
 */
export function isGoalSetLocked(goals: IGoal[]): boolean {
  return goals.some((g) => g?.status === GOAL_FINALIZED_STATUS);
}

/** Stacked-bar segment colors cycled per goal (§8 weight distribution bar). */
export const WEIGHT_BAR_COLORS = [
  '#6366f1',
  '#10b981',
  '#f59e0b',
  '#ec4899',
  '#0ea5e9',
  '#8b5cf6',
  '#ef4444',
  '#14b8a6',
  '#f97316',
  '#84cc16',
];

// ─── Wire contract → view-model mapper (US-PRF-001 D-perf slice 3) ────────────
//
// `getActiveCycle` consumes the GENERATED `PerformanceActiveCycleDto`, not a
// hand-written guess. **Drift reconciled here:** `IAppraisalCycle` declared flat
// `goalSettingOpensOn`/`goalSettingClosesOn` date fields; the wire carries NO such
// fields — the goal-setting window lives inside the `phases[]` array (the phase whose
// type is `GoalSetting`). The un-migrated code returned the raw payload as
// `IAppraisalCycle`, so both window dates were `undefined`. This mapper derives them
// from the `GoalSetting` phase — the closest faithful reading, not a fabricated value.
export type ActiveCycleWire = Schema<'PerformanceActiveCycleDto'>;

export function mapActiveCycle(w: ActiveCycleWire): IAppraisalCycle {
  const goalSetting = (w.phases ?? []).find(
    (p) => (p.phaseTypeName ?? p.phaseType) === 'GoalSetting',
  );
  return {
    id: w.id ?? '',
    name: w.name ?? '',
    // No dedicated wire field — derived from the GoalSetting phase window (reported).
    goalSettingOpensOn: (goalSetting?.startDate ?? '').slice(0, 10),
    goalSettingClosesOn: (goalSetting?.endDate ?? '').slice(0, 10),
    goalSettingOpen: w.goalSettingOpen ?? false,
  };
}
