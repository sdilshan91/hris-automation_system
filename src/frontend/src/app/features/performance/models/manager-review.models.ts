/**
 * US-PRF-003: MANAGER-side performance review models matching the (ASSUMED) backend
 * API contract. This is the manager rating their direct reports against goals after
 * self-assessment — distinct from US-PRF-002 (the employee self-rating themselves).
 *
 * The service layer (`ManagerReviewService`) is intentionally thin so a route/DTO
 * mismatch is a one-file fix once the backend lands (reconcile at US-PRF-004, like
 * US-PRF-001/002).
 *
 * ── ASSUMED backend contract (backend agent must confirm/reconcile) ────────────
 * `apiBaseUrl` already includes `/api/v1`. All under `/performance/manager-review`:
 *
 *   GET  /performance/manager-review/cycles/active/team
 *        → IManagerTeamRow[] (the manager's direct reports for the active cycle +
 *          their review status — drives the Team Reviews dashboard, AC-4). Manager +
 *          tenant resolved server-side from the session; RLS + Performance.Review.Team
 *          scope so only direct reports in the tenant come back (NFR-2 / BR-2).
 *
 *   GET  /performance/manager-review/employees/{employeeId}/active
 *        → IManagerReview (one employee's review for the active cycle: each goal with
 *          the employee's self-rating/comment alongside the manager's rating/comment,
 *          the tenant rating scale, the window/lock state, and the computed scores).
 *          One call loads the whole per-employee review screen (AC-1 / NFR-1 ≤400ms).
 *
 *   PUT  /performance/manager-review/{reviewId}/draft
 *        body ISaveManagerReviewRequest → IManagerReview
 *        Persists a partial draft (no gate beyond ownership; status stays the same).
 *
 *   POST /performance/manager-review/{reviewId}/submit
 *        body ISaveManagerReviewRequest → IManagerReview
 *        Validates all-goals-rated + each manager comment ≥20 chars, computes the
 *        weighted manager score + final combined score (FR-4 / BR-4), flips status to
 *        `ManagerReviewSubmitted`, locks edits, notifies the employee (AC-2). Rejects
 *        when the window is closed (BR-1). Returns the locked review.
 *
 * NOTE: All requests use withCredentials (httpOnly cookie auth) and are tenant-scoped
 * via the tenantInterceptor (X-Tenant-Subdomain header). The backend enforces
 * `Performance.Review.Team` (BR-2: only direct reports) or `Performance.Review.All`
 * (BR-3: HR can review anyone + reopen) + RLS (NFR-2) — the FE sends no tenant id.
 * The global ApiResponse unwrap interceptor (US-PLT-001) strips the `{ data }`
 * envelope — services consume BARE payloads. Enums are PascalCase STRINGS
 * (US-PLT-003), never integers.
 */

// ─── Enums ────────────────────────────────────────────────────

/**
 * Review-workflow status of an employee within a cycle (AC-4). Matches C# enum.
 * The Team Reviews dashboard color-codes by this:
 *  - PendingSelfAssessment: employee hasn't submitted self-assessment yet.
 *  - SelfAssessmentSubmitted: employee done; manager review pending (actionable).
 *  - ManagerReviewSubmitted: manager rated + submitted; awaiting completion.
 *  - Completed: review fully closed (e.g. HR sign-off / cycle closed).
 */
import type { Schema } from '@core/api';

export type ManagerReviewStatus =
  | 'PendingSelfAssessment'
  | 'SelfAssessmentSubmitted'
  | 'ManagerReviewSubmitted'
  | 'Completed';

export const MANAGER_REVIEW_STATUS_BADGE: Record<ManagerReviewStatus, string> =
  {
    PendingSelfAssessment: 'bg-neutral-100 text-neutral-600 ring-neutral-500/20',
    SelfAssessmentSubmitted: 'bg-sky-50 text-sky-700 ring-sky-600/20',
    ManagerReviewSubmitted: 'bg-amber-50 text-amber-700 ring-amber-600/20',
    Completed: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  };

export const MANAGER_REVIEW_STATUS_LABEL: Record<ManagerReviewStatus, string> =
  {
    PendingSelfAssessment: 'Pending self-assessment',
    SelfAssessmentSubmitted: 'Self-assessment submitted',
    ManagerReviewSubmitted: 'Manager review submitted',
    Completed: 'Completed',
  };

/**
 * FR-6: a manager may flag the employee for a follow-up action. `None` is the default (no flag).
 *
 * GAP-012a: this was hand-written as `'None' | 'Recognition' | 'PromotionConsideration' | 'PIP'` under a
 * comment claiming it "matches C# ReviewFlag". It did not: the API emits `Promotion` and `Pip`, so EVERY
 * promotion-flagged submit 400'd and the PIP flag never round-tripped. Now DERIVED from the generated
 * contract, so the same drift cannot recur — a backend rename becomes a compile error here instead of a
 * runtime 400. `REVIEW_FLAG_BADGE` being a `Record<ReviewFlag, …>` means an added enum member also fails
 * the build until it is given a badge.
 */
export type ReviewFlag = Schema<'ReviewFlag'>;

export const REVIEW_FLAG_OPTIONS: { value: ReviewFlag; label: string }[] = [
  { value: 'None', label: 'No flag' },
  { value: 'Recognition', label: 'Recognition' },
  { value: 'Promotion', label: 'Promotion consideration' },
  { value: 'Pip', label: 'Performance improvement (PIP)' },
];

export const REVIEW_FLAG_BADGE: Record<ReviewFlag, string> = {
  None: 'bg-neutral-100 text-neutral-600 ring-neutral-500/20',
  Recognition: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  Promotion: 'bg-indigo-50 text-indigo-700 ring-indigo-600/20',
  Pip: 'bg-rose-50 text-rose-700 ring-rose-600/20',
};

// ─── DTOs ─────────────────────────────────────────────────────

/** One row on the Team Reviews dashboard (AC-4). */
export interface IManagerTeamRow {
  /** Review record id when one exists; null until the workflow creates it. */
  reviewId: string | null;
  employeeId: string;
  employeeName: string;
  jobTitle?: string | null;
  status: ManagerReviewStatus;
  /** Total goals assigned (for the "N goals" hint). */
  goalCount: number;
  /** ISO timestamp the employee submitted self-assessment; null if not yet. */
  selfSubmittedOn: string | null;
}

/**
 * One goal in a manager review: the employee's self-rating/comment (read-only, FR-1)
 * alongside the manager's rating/comment inputs (AC-1). The goal descriptor fields
 * (title…weight) are read-only here (set in US-PRF-001).
 */
export interface IManagerReviewGoal {
  goalId: string;
  title: string;
  description: string;
  /** Whole-percent weight (FR-1), used for the weighted-score preview. */
  weight: number;
  targetValue: string;
  measurementUnit: string;
  /** ISO date (yyyy-MM-dd). */
  dueDate: string;

  // ── employee self-assessment (read-only reference, FR-1) ──
  /** Employee's self-rating on the tenant scale; null if they didn't rate it. */
  selfRating: number | null;
  /** Employee's self-assessment comment (read-only). */
  selfComment: string;

  // ── manager-entered fields (nullable until rated) ──
  /** Manager rating on the tenant scale 1..ratingScaleMax (FR-2). null = unrated. */
  managerRating: number | null;
  /** Manager comment, min 20 chars to submit (FR-3). */
  managerComment: string;
}

/**
 * One employee's whole manager review for the active cycle — drives the entire
 * per-employee review screen (AC-1). The backend computes `windowOpen` from the
 * configured manager-review window dates (BR-1); the FE renders read-only off this
 * flag and off `status` (AC-5), it does NOT compute open/closed client-side.
 */
export interface IManagerReview {
  /** Manager-review record id (used for draft/submit routes). */
  id: string;
  cycleId: string;
  cycleName: string;
  employeeId: string;
  employeeName: string;
  jobTitle?: string | null;
  status: ManagerReviewStatus;
  /** Authoritative open/closed gate for the manager-review window (AC-5 / BR-1). */
  windowOpen: boolean;
  /** Tenant-configured rating scale max, e.g. 5 or 10 (FR-2). */
  ratingScaleMax: number;
  /** Employee's weighted self-score once they submitted (FR-4); null if pending. */
  selfScore: number | null;
  /** Weighted manager score once submitted (FR-4); null while a draft. */
  managerScore: number | null;
  /**
   * Final combined score the SERVER computes (BR-4: self*selfWeight +
   * manager*managerWeight). Display only — the FE never computes this. null until
   * the manager review is submitted.
   */
  finalScore: number | null;
  /** Manager's overall summary comment (FR-5, max 5000 chars). */
  summaryComment: string;
  /** Manager's follow-up flag (FR-6). */
  flag: ReviewFlag;
  /** ISO timestamp of manager submission (§7); null while a draft. */
  submittedOn: string | null;
  goals: IManagerReviewGoal[];
}

/** One goal's manager rating sent on draft-save / submit. */
export interface IManagerReviewGoalInput {
  goalId: string;
  managerRating: number | null;
  managerComment: string;
}

/**
 * Draft-save / submit request body (AC-2 / AC-3). Matches the backend
 * `SaveManagerReviewRequest` (cycleId + employeeId + items in the body — BUG-243).
 */
export interface ISaveManagerReviewRequest {
  cycleId: string;
  employeeId: string;
  items: IManagerReviewGoalInput[];
  summaryComment: string;
  flag: ReviewFlag;
}

// ─── Validation constants (mirror the backend) ────────────────

/** FR-3: minimum manager comment length per goal to submit. */
export const MANAGER_COMMENT_MIN = 20;
/** FR-5: max length of the overall summary comment. */
export const SUMMARY_COMMENT_MAX = 5000;

// ─── Pure helpers (testable without a component) ──────────────

/** True when a goal has a manager rating AND a comment ≥ MANAGER_COMMENT_MIN chars. */
export function managerGoalIsComplete(goal: {
  managerRating: number | null;
  managerComment: string;
}): boolean {
  return (
    goal.managerRating != null &&
    goal.managerRating > 0 &&
    (goal.managerComment ?? '').trim().length >= MANAGER_COMMENT_MIN
  );
}

/** Count of fully-completed goals (rated + valid comment) — drives "N of M" (§8). */
export function ratedManagerGoalCount(
  goals: { managerRating: number | null; managerComment: string }[],
): number {
  return goals.filter((g) => managerGoalIsComplete(g)).length;
}

/**
 * AC-2 submit gate: every goal must be complete (rated + comment ≥20 chars) and
 * there must be at least one goal. Pure so it can be unit-tested directly.
 */
export function canSubmitManagerReview(
  goals: { managerRating: number | null; managerComment: string }[],
): boolean {
  if (goals.length === 0) {
    return false;
  }
  return goals.every((g) => managerGoalIsComplete(g));
}

/**
 * AC-3: titles of the goals that are NOT yet fully rated/commented, in order. The
 * validation error lists these so the manager knows exactly what's outstanding.
 * Pure + exported so QA can assert the list without a component.
 */
export function unratedManagerGoalTitles(
  goals: {
    title: string;
    managerRating: number | null;
    managerComment: string;
  }[],
): string[] {
  return goals.filter((g) => !managerGoalIsComplete(g)).map((g) => g.title);
}

/**
 * FR-4 preview: weighted manager score = Σ(rating/scaleMax × weight) / Σ(weight) ×
 * 100, scaled to a 0-100 percentage. Goals with no rating contribute 0. Returns 0
 * when there is no weight to divide by. The backend is authoritative on submit; this
 * mirrors it for the live preview only. (The FINAL combined score, BR-4, is NEVER
 * computed here — it depends on tenant self/manager weights the server owns.)
 */
export function weightedManagerScore(
  goals: { managerRating: number | null; weight: number }[],
  ratingScaleMax: number,
): number {
  const totalWeight = goals.reduce((acc, g) => acc + (Number(g.weight) || 0), 0);
  if (totalWeight <= 0 || ratingScaleMax <= 0) {
    return 0;
  }
  const earned = goals.reduce((acc, g) => {
    const rating = Number(g.managerRating) || 0;
    const weight = Number(g.weight) || 0;
    return acc + (rating / ratingScaleMax) * weight;
  }, 0);
  return Math.round((earned / totalWeight) * 100);
}

/**
 * §8 color band for a rating badge given the value and the scale max: green for the
 * top third, yellow for the middle, red for the bottom. Returns Tailwind classes.
 * Used for both self and manager rating badges so they're comparable.
 */
export function ratingBandClasses(
  rating: number | null,
  ratingScaleMax: number,
): string {
  if (rating == null || rating <= 0 || ratingScaleMax <= 0) {
    return 'bg-neutral-100 text-neutral-500 ring-neutral-400/20';
  }
  const ratio = rating / ratingScaleMax;
  if (ratio >= 2 / 3) {
    return 'bg-emerald-50 text-emerald-700 ring-emerald-600/20';
  }
  if (ratio >= 1 / 3) {
    return 'bg-amber-50 text-amber-700 ring-amber-600/20';
  }
  return 'bg-rose-50 text-rose-700 ring-rose-600/20';
}
