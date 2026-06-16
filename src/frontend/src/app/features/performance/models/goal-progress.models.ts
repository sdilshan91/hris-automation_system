/**
 * US-PRF-009: Goal Tracking with Progress Updates models matching the (ASSUMED)
 * backend API contract. Two personas:
 *  - EMPLOYEE "My Goals" — goal cards with progress %, status, last update, an
 *    append-only update history timeline, and an "Add Update" form (slider + status
 *    + notes + optional attachments).
 *  - MANAGER "Team Goal Progress" — a summary table of direct reports (overall
 *    completion %, # goals at risk, last-update date) with drill-down to an
 *    employee's goals + update timeline, plus the ability to comment on updates.
 *
 * The service layer (`GoalProgressService`) is intentionally thin so a route/DTO
 * mismatch is a one-file fix once the backend lands (like US-PRF-001..008).
 *
 * ── ASSUMED backend contract (backend agent must confirm/reconcile) ────────────
 * `apiBaseUrl` already includes `/api/v1`. All under `/performance/goal-progress`.
 * Tenant + acting user resolved server-side from the session (FE sends no tenant/
 * employee id for the self views); `Performance.Read.Self` (employee, AC-1/AC-2/AC-3)
 * + RLS for "my goals"; `Performance.Review.Team` (manager, AC-4) / `.All` (HR) for
 * the team-progress views. Progress history is APPEND-ONLY server-side (NFR-3); the
 * FE never edits/deletes a posted update.
 *
 *   GET   /performance/goal-progress/my-goals
 *         → IMyGoals — the whole "My Goals" screen in one call (AC-1): the active
 *           cycle window flag, the weighted overall completion %, and the goal cards
 *           (each with current progress %, status, lastUpdatedOn). `windowOpen`
 *           is the authoritative gate (BR-1) — the FE renders read-only off this
 *           flag, NOT off the cycle dates.
 *
 *   GET   /performance/goal-progress/goals/{goalId}/updates
 *         → IGoalUpdate[] (AC-3 timeline: chronological updates with progress change,
 *           notes, attachments, and the manager comment thread per update, FR-8).
 *           Tolerates a `{ data }` page.
 *
 *   POST  /performance/goal-progress/goals/{goalId}/updates  body IAddGoalUpdateRequest
 *         → IGoalUpdate (AC-2/FR-2: the appended, server-timestamped update). Multipart
 *           when files are attached (repeated field `files`, ≤3, FR-2); JSON otherwise.
 *           Server notifies the manager (FR-5) and, when status=Blocked, HR too (BR-3).
 *
 *   POST  /performance/goal-progress/updates/{updateId}/comments  body { comment }
 *         → IGoalComment (FR-8: a manager/HR comment on an employee's update).
 *
 *   GET   /performance/goal-progress/team
 *         → ITeamGoalProgressRow[] (AC-4: the manager's direct reports with overall
 *           completion %, goalsAtRisk, lastUpdatedOn). Tolerates a `{ data }` page.
 *
 *   GET   /performance/goal-progress/team/{employeeId}
 *         → IEmployeeGoalProgress (AC-4 drill-down: one report's goals + the same
 *           per-goal update timeline the employee sees, for the manager to comment).
 *
 * Bare payloads (US-PLT-001 unwrap); PascalCase enum strings (US-PLT-003), never ints.
 */

// ─── Enums ────────────────────────────────────────────────────

/**
 * Goal progress status (FR-2/FR-7). Matches the C# enum (PascalCase strings).
 *  - NotStarted: gray — no progress yet.
 *  - InProgress: blue — actively being worked.
 *  - Completed:  green — 100% / done (BR-2 auto-selects this at 100%).
 *  - AtRisk:     amber — progressing but may not meet the target.
 *  - Blocked:    red — blocked; notifies manager + HR (BR-3).
 */
export type GoalProgressStatus =
  | 'NotStarted'
  | 'InProgress'
  | 'Completed'
  | 'AtRisk'
  | 'Blocked';

/** All statuses in display/transition order (drives the AC-2 status selector). */
export const GOAL_PROGRESS_STATUSES: readonly GoalProgressStatus[] = [
  'NotStarted',
  'InProgress',
  'Completed',
  'AtRisk',
  'Blocked',
] as const;

/** §8 color-coded chip classes per status (gray/blue/green/amber/red). */
export const GOAL_STATUS_BADGE: Record<GoalProgressStatus, string> = {
  NotStarted: 'bg-neutral-100 text-neutral-600 ring-neutral-500/20',
  InProgress: 'bg-blue-50 text-blue-700 ring-blue-600/20',
  Completed: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  AtRisk: 'bg-amber-50 text-amber-700 ring-amber-600/20',
  Blocked: 'bg-rose-50 text-rose-700 ring-rose-600/20',
};

/** Human label per status. */
export const GOAL_STATUS_LABEL: Record<GoalProgressStatus, string> = {
  NotStarted: 'Not started',
  InProgress: 'In progress',
  Completed: 'Completed',
  AtRisk: 'At risk',
  Blocked: 'Blocked',
};

/** Progress-bar fill color per status (animated CSS bar, §8). */
export const GOAL_STATUS_BAR: Record<GoalProgressStatus, string> = {
  NotStarted: 'bg-neutral-300',
  InProgress: 'bg-blue-500',
  Completed: 'bg-emerald-500',
  AtRisk: 'bg-amber-500',
  Blocked: 'bg-rose-500',
};

// ─── Validation constants (mirror the backend) ────────────────

/** FR-2: update notes max length. */
export const GOAL_UPDATE_NOTES_MAX = 2000;

/** FR-2: max attachments per update. */
export const GOAL_UPDATE_MAX_FILES = 3;

/** FR-2: per-file max size (10MB). */
export const GOAL_UPDATE_MAX_FILE_BYTES = 10 * 1024 * 1024;

/** §10: manager comments are lightweight. */
export const GOAL_COMMENT_MAX = 500;

/** Closed-window message (QA may assert verbatim). */
export const GOAL_WINDOW_CLOSED_MESSAGE =
  'Goal progress updates are closed for this appraisal cycle.';

// ─── DTOs ─────────────────────────────────────────────────────

/** A manager/HR comment on an employee's progress update (FR-8). */
export interface IGoalComment {
  commentId: string;
  /** Author display name. */
  authorName: string;
  comment: string;
  createdOn: string;
}

/** An attachment posted with an update (FR-2). */
export interface IGoalAttachment {
  attachmentId: string;
  fileName: string;
  /** Optional download URL the server exposes. */
  url?: string | null;
}

/**
 * One progress update in the append-only history (AC-3/FR-3). Updates are immutable
 * once posted (NFR-3); the FE never edits or deletes them.
 */
export interface IGoalUpdate {
  updateId: string;
  /** Progress % after this update (0..100). */
  progressPercent: number;
  /** Progress % before this update — for the "X% → Y%" delta in the timeline. */
  previousProgressPercent: number | null;
  status: GoalProgressStatus;
  notes: string | null;
  /** Who posted it (employee display name). */
  authorName: string;
  /** Server timestamp (ISO) — append-only, set on save (AC-2). */
  createdOn: string;
  attachments: IGoalAttachment[];
  /** The manager comment thread on this update (FR-8). */
  comments: IGoalComment[];
}

/** A goal card on the "My Goals" / drill-down screen (AC-1). */
export interface IGoalProgress {
  goalId: string;
  title: string;
  /** The measurable target / description (AC-1). */
  target: string;
  /** Weight (whole %) — used for the weighted overall completion (FR-4). */
  weight: number;
  /** Current progress % (0..100). */
  progressPercent: number;
  status: GoalProgressStatus;
  /** Last update timestamp (ISO); null if never updated (AC-1). */
  lastUpdatedOn: string | null;
  /** AC-5: server-flagged stale goal ("Needs Attention"). */
  needsAttention: boolean;
}

/** The whole "My Goals" screen payload (AC-1). */
export interface IMyGoals {
  cycleId: string;
  cycleName: string;
  /** Authoritative open/closed gate (BR-1) — FE renders read-only off this. */
  windowOpen: boolean;
  /** Weighted overall completion % (FR-4); server-computed, FE displays it. */
  overallCompletionPercent: number;
  goals: IGoalProgress[];
}

/** Add-update body (AC-2/FR-2). Files (if any) are sent multipart, field `files`. */
export interface IAddGoalUpdateRequest {
  progressPercent: number;
  status: GoalProgressStatus;
  notes: string;
}

/** A row on the manager Team Goal Progress table (AC-4). */
export interface ITeamGoalProgressRow {
  employeeId: string;
  employeeName: string;
  jobTitle?: string | null;
  /** Weighted overall completion % across the report's goals (FR-4). */
  overallCompletionPercent: number;
  goalCount: number;
  /** # goals currently AtRisk or Blocked (AC-4). */
  goalsAtRisk: number;
  /** Last update across all the report's goals (ISO); null if none. */
  lastUpdatedOn: string | null;
}

/** The manager drill-down into one report's goals (AC-4). */
export interface IEmployeeGoalProgress {
  employeeId: string;
  employeeName: string;
  jobTitle?: string | null;
  overallCompletionPercent: number;
  goals: IGoalProgress[];
}

// ─── Pure helpers (testable without a component) ──────────────

/**
 * BR-2: setting progress to 100% auto-selects Completed (overridable). Returns the
 * status the form should default to given the slider value and the prior status.
 * Only forces Completed when hitting 100 from a non-completed status; below 100 it
 * leaves the chosen status untouched (so the employee can mark AtRisk/Blocked at any %).
 */
export function statusForProgress(
  progressPercent: number,
  currentStatus: GoalProgressStatus,
): GoalProgressStatus {
  if (progressPercent >= 100) {
    return 'Completed';
  }
  // Demote a stale "Completed" if the slider drops below 100 (e.g. correcting a typo).
  if (currentStatus === 'Completed' && progressPercent < 100) {
    return progressPercent > 0 ? 'InProgress' : 'NotStarted';
  }
  return currentStatus;
}

/**
 * FR-4: weighted overall completion % across a goal set. Weighted average of each
 * goal's progress by its weight; falls back to a simple average if weights are all 0.
 * Pure + clamped so the donut always renders sensibly. The server is authoritative
 * (IMyGoals.overallCompletionPercent) — this is the FE fallback when only goal cards
 * are available (e.g. an optimistic recompute after posting an update).
 */
export function weightedOverallCompletion(
  goals: readonly Pick<IGoalProgress, 'progressPercent' | 'weight'>[],
): number {
  if (goals.length === 0) {
    return 0;
  }
  const totalWeight = goals.reduce((sum, g) => sum + (g.weight || 0), 0);
  if (totalWeight <= 0) {
    const avg =
      goals.reduce((sum, g) => sum + clampPercent(g.progressPercent), 0) /
      goals.length;
    return Math.round(avg);
  }
  const weighted = goals.reduce(
    (sum, g) => sum + clampPercent(g.progressPercent) * (g.weight || 0),
    0,
  );
  return Math.round(weighted / totalWeight);
}

/** Clamp a value into [0, 100]. */
export function clampPercent(value: number): number {
  if (Number.isNaN(value)) {
    return 0;
  }
  return Math.max(0, Math.min(100, value));
}

/**
 * Donut SVG geometry for a 0–100 completion rate (§8 overall completion widget).
 * Returns stroke-dasharray circumference + dashoffset for a circle of the given
 * radius so the template stays declarative — pure SVG, NO chart library (consistent
 * with US-PRF-003/004/005/007, see the no-chart-lib memory).
 */
export function completionDonut(
  ratePercent: number,
  radius: number,
): { circumference: number; dashOffset: number } {
  const pct = clampPercent(ratePercent);
  const circumference = 2 * Math.PI * radius;
  const dashOffset = circumference * (1 - pct / 100);
  return { circumference, dashOffset };
}

/** Tailwind ring/text class for the donut center label by completion band. */
export function completionBandClass(ratePercent: number): string {
  const pct = clampPercent(ratePercent);
  if (pct >= 80) {
    return 'text-emerald-600';
  }
  if (pct >= 40) {
    return 'text-blue-600';
  }
  if (pct > 0) {
    return 'text-amber-600';
  }
  return 'text-neutral-400';
}
