/**
 * US-PRF-002: Employee self-assessment models matching the (ASSUMED) backend API
 * contract. The service layer is intentionally thin so a route/DTO mismatch is a
 * one-file fix once the backend lands (reconcile at US-PRF-004, like US-PRF-001).
 *
 * ── ASSUMED backend contract (backend agent must confirm/reconcile) ────────────
 * `apiBaseUrl` already includes `/api/v1`. All under `/performance/self-assessment`:
 *
 *   GET  /performance/self-assessment/active
 *        → ISelfAssessment (the authenticated employee's self-assessment for the
 *          active cycle + the assigned goals + the window/lock state). One call
 *          loads the whole "My Review" screen (NFR-1 ≤400ms). Tenant + employee
 *          are resolved server-side from the session — the FE sends no ids.
 *
 *   PUT  /performance/self-assessment/{assessmentId}/draft
 *        body ISaveSelfAssessmentRequest → ISelfAssessment
 *        Persists a partial draft (AC-3 / FR-6 / NFR-3 auto-save). No validation
 *        gate server-side beyond ownership; status stays `Draft`.
 *
 *   POST /performance/self-assessment/{assessmentId}/submit
 *        body ISaveSelfAssessmentRequest → ISelfAssessment
 *        Validates all-goals-rated + each comment ≥20 chars, computes the weighted
 *        self-score, flips status to `Submitted`, locks edits, notifies the manager
 *        (AC-2 / BR-2 / BR-3 / FR-4). Rejects when the window is closed (BR-1).
 *
 *   POST /performance/self-assessment/{assessmentId}/goals/{goalId}/attachments
 *        multipart/form-data field `file` → IAssessmentAttachment
 *        Uploads one evidence file for a goal (FR-5: ≤5 files, ≤10MB each). The
 *        backend virus-scans + stores in tenant-scoped storage (NFR-4). Returns the
 *        created attachment row. (Contract is a best-guess — see service stub note.)
 *
 *   DELETE /performance/self-assessment/{assessmentId}/attachments/{attachmentId}
 *        → 204. Remove an uploaded evidence file.
 *
 * NOTE: All requests use withCredentials (httpOnly cookie auth) and are tenant-scoped
 * via the tenantInterceptor (X-Tenant-Subdomain header). The backend enforces
 * `Performance.Read.Self` + RLS so an employee only sees their OWN assessment
 * (NFR-2). The global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` envelope — services consume BARE payloads. Enums are PascalCase STRINGS
 * (US-PLT-003), never integers.
 */

// ─── Enums ────────────────────────────────────────────────────

/** Lifecycle of a self-assessment record (§7, AC-2/AC-3). Matches C# enum. */
export type SelfAssessmentStatus = 'NotStarted' | 'Draft' | 'Submitted';

export const SELF_ASSESSMENT_STATUS_BADGE: Record<
  SelfAssessmentStatus,
  string
> = {
  NotStarted: 'bg-neutral-100 text-neutral-600 ring-neutral-500/20',
  Draft: 'bg-amber-50 text-amber-700 ring-amber-600/20',
  Submitted: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
};

export const SELF_ASSESSMENT_STATUS_LABEL: Record<
  SelfAssessmentStatus,
  string
> = {
  NotStarted: 'Not started',
  Draft: 'Draft saved',
  Submitted: 'Self-assessment submitted',
};

// ─── DTOs ─────────────────────────────────────────────────────

/** One evidence file attached to a goal (FR-5). */
export interface IAssessmentAttachment {
  id: string;
  fileName: string;
  /** Bytes — used for the size label only. */
  sizeBytes: number;
  uploadedOn: string;
}

/**
 * One assigned goal + the employee's self-rating for it (FR-1). The goal fields
 * (title…dueDate) are read-only here (set by the manager in US-PRF-001); the
 * rating/achievement/comment/attachments are what the employee edits.
 */
export interface ISelfAssessmentGoal {
  /** The goal id (stable key for save/attachment routes). */
  goalId: string;
  title: string;
  description: string;
  /** Whole-percent weight (FR-1, used for the weighted-progress indicator). */
  weight: number;
  targetValue: string;
  measurementUnit: string;
  /** ISO date (yyyy-MM-dd). */
  dueDate: string;

  // ── employee-entered fields (nullable until rated) ──
  /** Self-rating on the tenant scale 1..ratingScaleMax (FR-2). null = unrated. */
  selfRating: number | null;
  /** Achievement percent 0-100 (FR-3 / §7). */
  achievementPercent: number | null;
  /** Self-assessment comment, min 20 chars to submit (FR-3). */
  comment: string;
  /** Evidence files already uploaded for this goal (FR-5). */
  attachments: IAssessmentAttachment[];
}

/**
 * The employee's whole self-assessment for the active cycle — drives the entire
 * "My Review" screen (AC-1). The backend computes `windowOpen` from the configured
 * self-assessment window dates (BR-1); the FE renders read-only off this flag and
 * off `status === 'Submitted'` (AC-2/AC-4), it does NOT compute open/closed from
 * the dates client-side.
 */
export interface ISelfAssessment {
  /** Self-assessment record id (used for draft/submit/attachment routes). */
  id: string;
  cycleId: string;
  cycleName: string;
  status: SelfAssessmentStatus;
  /** Authoritative open/closed gate for the self-assessment window (AC-4 / BR-1). */
  windowOpen: boolean;
  /** Tenant-configured rating scale max, e.g. 5 or 10 (FR-2). */
  ratingScaleMax: number;
  /** ISO date the window closes — shown as a hint, not used for the gate. */
  windowClosesOn: string;
  /** Weighted self-score once submitted (FR-4); null while a draft. */
  weightedScore: number | null;
  /** ISO timestamp of submission (§7); null while a draft. */
  submittedOn: string | null;
  goals: ISelfAssessmentGoal[];
}

/** One goal's self-rating sent on draft-save / submit. */
export interface ISelfAssessmentGoalInput {
  goalId: string;
  selfRating: number | null;
  achievementPercent: number | null;
  comment: string;
}

/** Draft-save / submit request body (AC-2 / AC-3). */
export interface ISaveSelfAssessmentRequest {
  goals: ISelfAssessmentGoalInput[];
}

// ─── Validation constants (mirror the backend) ────────────────

/** FR-3: minimum self-assessment comment length to submit. */
export const SELF_COMMENT_MIN = 20;
/** FR-5: max evidence files per goal. */
export const ATTACHMENT_MAX_FILES = 5;
/** FR-5: max evidence file size (10MB). */
export const ATTACHMENT_MAX_BYTES = 10 * 1024 * 1024;
/** NFR-3: draft auto-save interval (ms). */
export const AUTO_SAVE_INTERVAL_MS = 60_000;
/** AC-4: closed-window message — QA asserts this string verbatim. */
export const WINDOW_CLOSED_MESSAGE =
  'The self-assessment period for this cycle has ended';

// ─── Pure helpers (testable without a component) ──────────────

/** True when a goal has a self-rating AND a comment ≥ SELF_COMMENT_MIN chars. */
export function goalIsComplete(goal: {
  selfRating: number | null;
  comment: string;
}): boolean {
  return (
    goal.selfRating != null &&
    goal.selfRating > 0 &&
    (goal.comment ?? '').trim().length >= SELF_COMMENT_MIN
  );
}

/** Count of fully-completed goals (rated + valid comment) — drives "N of M" (§8). */
export function ratedGoalCount(
  goals: { selfRating: number | null; comment: string }[],
): number {
  return goals.filter((g) => goalIsComplete(g)).length;
}

/**
 * AC-2 / BR-2 submit gate: every goal must be complete (rated + comment ≥20 chars)
 * and there must be at least one goal. Pure so it can be unit-tested directly.
 */
export function canSubmitSelfAssessment(
  goals: { selfRating: number | null; comment: string }[],
): boolean {
  if (goals.length === 0) {
    return false;
  }
  return goals.every((g) => goalIsComplete(g));
}

/**
 * FR-4: weighted self-score = Σ(rating/scaleMax × weight) / Σ(weight) × 100, scaled
 * to a 0-100 percentage. Goals with no rating contribute 0. Returns 0 when there is
 * no weight to divide by. The backend is authoritative on submit; this mirrors it
 * for the live progress preview.
 */
export function weightedSelfScore(
  goals: { selfRating: number | null; weight: number }[],
  ratingScaleMax: number,
): number {
  const totalWeight = goals.reduce((acc, g) => acc + (Number(g.weight) || 0), 0);
  if (totalWeight <= 0 || ratingScaleMax <= 0) {
    return 0;
  }
  const earned = goals.reduce((acc, g) => {
    const rating = Number(g.selfRating) || 0;
    const weight = Number(g.weight) || 0;
    return acc + (rating / ratingScaleMax) * weight;
  }, 0);
  return Math.round((earned / totalWeight) * 100);
}

/** Human file-size label for the attachment list. */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(0)} KB`;
  }
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** Client-side guard before an attachment upload (FR-5). Returns null when OK. */
export function validateAttachment(
  file: File,
  existingCount: number,
): 'too_many' | 'too_large' | null {
  if (existingCount >= ATTACHMENT_MAX_FILES) {
    return 'too_many';
  }
  if (file.size > ATTACHMENT_MAX_BYTES) {
    return 'too_large';
  }
  return null;
}
