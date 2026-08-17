/**
 * US-PRF-005: 360-degree feedback models matching the (ASSUMED) backend API contract.
 * Four reviewer perspectives — Self, Manager, Peer, Direct Report — give multi-rater
 * feedback against competencies/goals for an employee within an appraisal cycle.
 *
 * Sibling to PerformanceGoalService (US-PRF-001), SelfAssessmentService (US-PRF-002),
 * ManagerReviewService (US-PRF-003) and CycleService (US-PRF-004). The service layer
 * (`Feedback360Service`) is intentionally thin so a route/DTO mismatch is a one-file
 * fix once the backend lands (reconcile alongside the other Performance stories).
 *
 * ── ASSUMED backend contract (backend agent must confirm/reconcile) ────────────
 * `apiBaseUrl` already includes `/api/v1`. All under `/performance/feedback-360`:
 *
 *   GET  /performance/feedback-360/employees/{employeeId}/config
 *        → IReviewerConfig — the reviewer-nomination screen for an employee in the
 *          active 360-enabled cycle (AC-1): auto-assigned Self + Manager, suggested
 *          Peers (same dept) and Direct Reports (org tree), the candidate employee
 *          pool to add from, the per-category minimums (FR-3) and the anonymity flag.
 *
 *   PUT  /performance/feedback-360/employees/{employeeId}/reviewers
 *        body ISaveReviewersRequest → IReviewerConfig
 *        Full-replace of the manual reviewer set (Peer + Direct Report rows the HR
 *        Officer/manager added/removed, FR-2). Self + Manager are server-owned and not
 *        sent. Server re-validates BR-2 (self can't be a peer) + per-category minimums.
 *
 *   GET  /performance/feedback-360/employees/{employeeId}/tracker
 *        → ICompletionTracker — per-category submitted/pending/overdue counts (AC-3 §8).
 *
 *   GET  /performance/feedback-360/assignments/{assignmentId}/form
 *        → IFeedbackForm — the competency/goal question cards + tenant rating scale for
 *          ONE reviewer's assignment (FR-4 / AC-2 deep link). Reviewer + tenant resolved
 *          server-side; RLS ensures a reviewer only loads their own assignment.
 *
 *   POST /performance/feedback-360/assignments/{assignmentId}/submit
 *        body ISubmitFeedbackRequest → IFeedbackForm
 *        Saves the reviewer's ratings + comments, marks the assignment Completed (AC-3),
 *        enforces BR-3 (one submission per reviewer per employee per cycle). Returns the
 *        locked form.
 *
 *   GET  /performance/feedback-360/employees/{employeeId}/results
 *        → IFeedback360Results — the aggregated dashboard (AC-4): per-competency average
 *          ratings, per-category (self/manager/peer/report) comparison, composite score
 *          (FR-6), and individual comments. When tenant anonymity is on (FR-5/NFR-3) the
 *          comment rows OMIT any reviewer identifier — the FE renders only what's
 *          returned and NEVER reconstructs identity.
 *
 *   GET  /performance/feedback-360/employees/{employeeId}/results/export  (OPTIONAL)
 *        → application/pdf (HttpResponse<Blob>) — the FR-7 PDF summary. PDF generation
 *          is a BACKEND concern; the FE only wires a download button when the endpoint
 *          exists. `IFeedback360Results.exportAvailable` gates the button.
 *
 * NOTE: All requests use withCredentials (httpOnly cookie auth) and are tenant-scoped
 * via the tenantInterceptor (X-Tenant-Subdomain header). The backend enforces
 * `Performance.Review.All` (HR config) / reviewer ownership + RLS (NFR-2/NFR-3) — the
 * FE sends no tenant id. The global ApiResponse unwrap interceptor (US-PLT-001) strips
 * the `{ data }` envelope — services consume BARE payloads. Enums are PascalCase
 * STRINGS (US-PLT-003), never integers.
 */

// ─── Enums ────────────────────────────────────────────────────

/** FR-1: the four reviewer categories. Matches the C# enum (PascalCase strings). */
export type ReviewerCategory = 'Self' | 'Manager' | 'Peer' | 'DirectReport';

export const REVIEWER_CATEGORY_ORDER: ReviewerCategory[] = [
  'Self',
  'Manager',
  'Peer',
  'DirectReport',
];

export const REVIEWER_CATEGORY_LABEL: Record<ReviewerCategory, string> = {
  Self: 'Self',
  Manager: 'Manager',
  Peer: 'Peers',
  DirectReport: 'Direct reports',
};

/** Per-category accent classes for chips / bars (Notion muted palette). */
export const REVIEWER_CATEGORY_BADGE: Record<ReviewerCategory, string> = {
  Self: 'bg-violet-50 text-violet-700 ring-violet-600/20',
  Manager: 'bg-sky-50 text-sky-700 ring-sky-600/20',
  Peer: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  DirectReport: 'bg-amber-50 text-amber-700 ring-amber-600/20',
};

/** Bar fill colour per category for the perspective-comparison visual (AC-4 radar). */
export const REVIEWER_CATEGORY_BAR: Record<ReviewerCategory, string> = {
  Self: '#7c3aed',
  Manager: '#0284c7',
  Peer: '#059669',
  DirectReport: '#d97706',
};

/**
 * Submission status of a single reviewer assignment (AC-3 / §8 tracker).
 *  - Pending: not yet submitted, deadline not passed.
 *  - Completed: feedback submitted.
 *  - Overdue: not submitted and the deadline has passed.
 */
export type AssignmentStatus = 'Pending' | 'Completed' | 'Overdue';

export const ASSIGNMENT_STATUS_BADGE: Record<AssignmentStatus, string> = {
  Pending: 'bg-neutral-100 text-neutral-600 ring-neutral-500/20',
  Completed: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  Overdue: 'bg-rose-50 text-rose-700 ring-rose-600/20',
};

/** FR-4: a feedback question is either competency-based or tied to a goal. */
export type QuestionKind = 'Competency' | 'Goal';

// ─── Reviewer-nomination DTOs (AC-1) ──────────────────────────

/** A nominatable / candidate employee (the searchable pool, §8). */
export interface IEmployeeOption {
  employeeId: string;
  name: string;
  jobTitle?: string | null;
  departmentId: string;
  departmentName: string;
  /** Optional avatar URL; FE falls back to initials when null. */
  avatarUrl?: string | null;
}

/** One assigned reviewer within a category (AC-1). */
export interface IReviewer {
  reviewerId: string;
  name: string;
  jobTitle?: string | null;
  departmentName?: string | null;
  category: ReviewerCategory;
  avatarUrl?: string | null;
  /**
   * True for server-owned rows (Self + the auto-assigned Manager) that the HR Officer
   * cannot remove (FR-2). Manual Peer / Direct Report rows are removable.
   */
  locked: boolean;
}

/** Per-category minimum reviewer count (FR-3). */
export interface ICategoryMinimum {
  category: ReviewerCategory;
  minimum: number;
}

/**
 * The whole reviewer-nomination screen for one employee in the active 360 cycle (AC-1).
 * Drives the nomination UI in a single call (NFR-1).
 */
export interface IReviewerConfig {
  cycleId: string;
  cycleName: string;
  employeeId: string;
  employeeName: string;
  /** Currently assigned reviewers across all four categories. */
  reviewers: IReviewer[];
  /** System-suggested peers (same department) the HR Officer can add (AC-1). */
  suggestedPeers: IEmployeeOption[];
  /** System-suggested direct reports (from the org tree) the HR Officer can add. */
  suggestedDirectReports: IEmployeeOption[];
  /** Full candidate pool for manual search/add across departments (§8). */
  candidatePool: IEmployeeOption[];
  /** Per-category minimums (FR-3) used to warn before results release (BR-4). */
  minimums: ICategoryMinimum[];
  /** Tenant anonymity setting for this cycle (FR-5); display-only context. */
  anonymous: boolean;
  /** Window/lock gate — reviewer changes are blocked once the feedback phase locks. */
  editable: boolean;
}

/** One reviewer row sent on save (manual Peer / Direct Report only, FR-2). */
export interface IReviewerInput {
  reviewerId: string;
  category: ReviewerCategory;
}

/** Full-replace of the manual reviewer set (AC-1). Self + Manager are not sent. */
export interface ISaveReviewersRequest {
  reviewers: IReviewerInput[];
}

// ─── Completion-tracker DTOs (AC-3 / §8) ──────────────────────

/** Per-category progress counts for the completion tracker. */
export interface ICategoryProgress {
  category: ReviewerCategory;
  submitted: number;
  pending: number;
  overdue: number;
  /** Required minimum for this category (FR-3) — shown alongside the bar. */
  minimum: number;
}

/** The completion tracker across all categories (AC-3). */
export interface ICompletionTracker {
  employeeId: string;
  categories: ICategoryProgress[];
}

// ─── Feedback-form DTOs (FR-4 / AC-2) ─────────────────────────

/** One competency- or goal-based question on the feedback form (FR-4). */
export interface IFeedbackQuestion {
  questionId: string;
  kind: QuestionKind;
  /** Competency name or goal title shown as the card heading. */
  title: string;
  /** Optional helper/description text under the title. */
  description?: string | null;
  /** Reviewer's rating on the tenant scale 1..ratingScaleMax; null = unrated. */
  rating: number | null;
  /** Optional free-text comment per competency (FR-4). */
  comment: string;
}

/**
 * ONE reviewer's feedback form for an employee in the active cycle (FR-4 / AC-2 deep
 * link). The reviewer is resolved server-side; the form never reveals the reviewee's
 * other reviewers.
 */
export interface IFeedbackForm {
  assignmentId: string;
  cycleId: string;
  cycleName: string;
  /** The employee being reviewed. */
  revieweeId: string;
  revieweeName: string;
  revieweeJobTitle?: string | null;
  /** This reviewer's category for the reviewee (Self/Manager/Peer/DirectReport). */
  category: ReviewerCategory;
  /** Tenant-configured rating scale max, e.g. 5 or 10 (FR-4). */
  ratingScaleMax: number;
  /** Authoritative submitted/lock gate (BR-3); once true the form is read-only. */
  submitted: boolean;
  /** ISO timestamp of submission; null while pending. */
  submittedOn: string | null;
  /** Whether this cycle's feedback is anonymous (FR-5) — shown as reviewer context. */
  anonymous: boolean;
  questions: IFeedbackQuestion[];
}

/** One question's answer sent on submit. */
export interface IFeedbackAnswerInput {
  questionId: string;
  rating: number | null;
  comment: string;
}

/** Submit-feedback request body (AC-3). */
/**
 * GAP-012: the wire field is `items`, not `answers` — every 360 submission POSTed a body the API could not
 * bind, so the answers were silently dropped. `overallComment` is accepted by
 * PerformanceSubmitFeedback360Request and the FE never sent it.
 */
export interface ISubmitFeedbackRequest {
  items: IFeedbackAnswerInput[];
  overallComment?: string | null;
}

// ─── Results-dashboard DTOs (AC-4) ────────────────────────────

/** Average rating for ONE category within a competency (AC-4 comparison). */
export interface ICategoryAverage {
  category: ReviewerCategory;
  /** Mean rating on the tenant scale; null when no reviewer in this category rated. */
  average: number | null;
  /** Number of reviewers in this category who rated this competency. */
  responseCount: number;
}

/** Per-competency aggregation: overall average + the self/manager/peer/report split. */
export interface ICompetencyResult {
  questionId: string;
  title: string;
  kind: QuestionKind;
  /** Overall average across all categories (AC-4 bar chart). */
  overallAverage: number | null;
  /** The per-category averages for the perspective comparison (AC-4 radar). */
  byCategory: ICategoryAverage[];
}

/**
 * One anonymized comment row (AC-4). CRITICAL (FR-5/NFR-3): when anonymity is on the
 * backend OMITS `reviewerName`/`reviewerId` entirely — the FE must NEVER reconstruct
 * identity. `category` may still be present (the perspective is not the identity).
 */
export interface IFeedbackComment {
  /** The competency/goal this comment is about. */
  questionTitle: string;
  category: ReviewerCategory;
  comment: string;
  /**
   * Present ONLY when anonymity is OFF and the backend chose to include it. When
   * anonymity is on this is undefined/omitted — never render a fallback identity.
   */
  reviewerName?: string;
}

/**
 * The aggregated 360 results for one employee (AC-4) in the FE's own vocabulary. Drives
 * the results dashboard in a single call (NFR-4 ≤2s for ≤20 reviewers). This is NOT the
 * wire shape — `Feedback360Service.getResults()` runs `mapFeedback360Results()` to
 * translate the backend `Feedback360ResultsDto` (see `IFeedback360ResultsRaw`) into this,
 * so the templates stay decoupled from the API's field names (ISSUE-373 / GAP-012 S-1).
 */
export interface IFeedback360Results {
  cycleId: string;
  cycleName: string;
  /** Reviewee id (backend `revieweeEmployeeId`). */
  employeeId: string;
  /** Reviewee name (backend `revieweeName`). */
  employeeName: string;
  jobTitle?: string | null;
  /** Tenant rating scale max for the visuals (AC-4). */
  ratingScaleMax: number;
  /** Whether anonymity is on for this cycle (FR-5, backend `isAnonymousFeedback`). */
  anonymous: boolean;
  /** True once the results have been released to the reviewee (BR-4/FR-3). */
  released: boolean;
  /** ISO instant the results were released; null while unreleased (BR-4/FR-3). */
  releasedAt: string | null;
  /** FR-6 composite score the SERVER computes (weighted across categories). Display-only
   *  — the FE NEVER computes this. */
  compositeScore: number | null;
  /** Per-competency aggregation (AC-4 bars). NOTE: the backend `competencyAverages` is
   *  flat (no per-category split), so `ICompetencyResult.byCategory` is always []. */
  competencies: ICompetencyResult[];
  /** Per-category overall averages (the radar/comparison rings, AC-4). */
  categoryAverages: ICategoryAverage[];
  /** Anonymized (or attributed) individual comments (AC-4), flattened from the backend
   *  per-reviewer `entries`. */
  comments: IFeedbackComment[];
  /** True when the backend PDF export endpoint is available for this caller (FR-7). */
  exportAvailable: boolean;
  /** BR-4 peer-threshold gate: the cycle's minimum peer reviewers... */
  minPeerReviewers: number;
  /** ...and how many peers have actually submitted. */
  peerResponseCount: number;
  /** True once `peerResponseCount >= minPeerReviewers` (BR-4). SERVER-computed — the FE
   *  reflects this flag, it does not derive it. Gates the "Release results" action. */
  minPeerThresholdMet: boolean;
  /** Server warning string when the peer threshold is not met; null otherwise (BR-4). */
  releaseWarning: string | null;
}

// ─── Raw backend wire shapes (Feedback360ResultsDto) ──────────
// These mirror the ASP.NET DTOs exactly (camelCased by the serializer). The adapter
// below maps them into the FE vocabulary above; the templates never see these.

/** One competency/goal average as the backend emits it (`CompetencyAverageDto`). */
export interface ICompetencyAverageRaw {
  goalId: string | null;
  competencyKey: string | null;
  label: string;
  averageRating: number;
  responseCount: number;
}

/** One per-category average as the backend emits it (`CategoryAverageDto`). */
export interface ICategoryAverageRaw {
  category: ReviewerCategory;
  categoryName: string;
  averageRating: number | null;
  responseCount: number;
  weight: number;
}

/** One competency/goal rating inside a result entry (`Feedback360ResultItemDto`). */
export interface IFeedback360ResultItemRaw {
  goalId: string | null;
  competencyKey: string | null;
  label: string;
  rating: number;
  comment: string | null;
}

/**
 * One submitted feedback entry as the backend emits it (`Feedback360ResultEntryDto`).
 * When anonymity is on the backend NULLS `reviewerEmployeeId`/`reviewerName` in the
 * projection (NFR-3/FR-5) — the adapter simply carries that through, never reconstructs.
 */
export interface IFeedback360ResultEntryRaw {
  feedbackId: string;
  category: ReviewerCategory;
  categoryName: string;
  isAnonymous: boolean;
  reviewerEmployeeId: string | null;
  reviewerName: string | null;
  overallComment: string | null;
  submittedAt: string;
  items: IFeedback360ResultItemRaw[];
}

/** The full aggregated-results wire payload (`Feedback360ResultsDto`). */
export interface IFeedback360ResultsRaw {
  cycleId: string;
  cycleName: string;
  revieweeEmployeeId: string;
  revieweeName: string;
  jobTitle: string;
  isAnonymousFeedback: boolean;
  ratingScaleMax: number;
  compositeScore: number;
  competencyAverages: ICompetencyAverageRaw[];
  categoryAverages: ICategoryAverageRaw[];
  entries: IFeedback360ResultEntryRaw[];
  minPeerReviewers: number;
  peerResponseCount: number;
  minPeerThresholdMet: boolean;
  releaseWarning: string | null;
  released: boolean;
  releasedAt: string | null;
  exportAvailable: boolean;
}

/** Result of the release action (`Feedback360ReleaseDto`, BR-4/FR-3). */
export interface IFeedback360ReleaseResult {
  cycleId: string;
  revieweeEmployeeId: string;
  releasedAt: string;
  releasedByEmployeeId: string;
}

/**
 * ISSUE-373 / GAP-012 (S-1): translate the backend `Feedback360ResultsDto` into the FE
 * `IFeedback360Results`. This is the ONE place the wire↔UI field-name drift is
 * reconciled, so the templates stay stable. Rules:
 *  - straight renames per the drift table (revieweeName→employeeName, etc.);
 *  - `categoryAverages[].averageRating` → `.average`;
 *  - `competencyAverages` (flat) → `competencies` with `byCategory: []` — the backend
 *    sends NO per-competency category split, so that field has no source (reported);
 *  - `entries` (per-reviewer, nested items) → `comments` (flat rows): the reviewer's
 *    overall comment plus each item comment become individual rows, carrying the
 *    (already-nulled-under-anonymity) reviewer name UNCHANGED — never reconstructed.
 */
export function mapFeedback360Results(
  raw: IFeedback360ResultsRaw,
): IFeedback360Results {
  return {
    cycleId: raw.cycleId,
    cycleName: raw.cycleName,
    employeeId: raw.revieweeEmployeeId,
    employeeName: raw.revieweeName,
    jobTitle: raw.jobTitle || null,
    ratingScaleMax: raw.ratingScaleMax,
    anonymous: raw.isAnonymousFeedback,
    released: raw.released,
    releasedAt: raw.releasedAt,
    compositeScore: raw.compositeScore ?? null,
    competencies: (raw.competencyAverages ?? []).map((c) => ({
      questionId: c.goalId ?? c.competencyKey ?? c.label,
      title: c.label,
      kind: c.goalId != null ? ('Goal' as const) : ('Competency' as const),
      overallAverage: c.averageRating,
      // No per-competency category split exists on the wire (GAP — see S-1 report).
      byCategory: [] as ICategoryAverage[],
    })),
    categoryAverages: (raw.categoryAverages ?? []).map((c) => ({
      category: c.category,
      average: c.averageRating,
      responseCount: c.responseCount,
    })),
    comments: flattenResultEntries(raw.entries ?? []),
    exportAvailable: raw.exportAvailable,
    minPeerReviewers: raw.minPeerReviewers,
    peerResponseCount: raw.peerResponseCount,
    minPeerThresholdMet: raw.minPeerThresholdMet,
    releaseWarning: raw.releaseWarning,
  };
}

/**
 * Flatten the backend per-reviewer `entries` into the flat comment rows the results view
 * renders. Each entry contributes its overall comment (if any) and one row per rated item
 * that carries a comment. `reviewerName` is passed through verbatim — the backend NULLS
 * it under anonymity (NFR-3/FR-5); this NEVER invents or reconstructs an identity.
 */
function flattenResultEntries(
  entries: IFeedback360ResultEntryRaw[],
): IFeedbackComment[] {
  const rows: IFeedbackComment[] = [];
  for (const e of entries) {
    const reviewerName = e.reviewerName ?? undefined;
    if (e.overallComment && e.overallComment.trim().length > 0) {
      rows.push({
        questionTitle: '',
        category: e.category,
        comment: e.overallComment,
        reviewerName,
      });
    }
    for (const item of e.items ?? []) {
      if (item.comment && item.comment.trim().length > 0) {
        rows.push({
          questionTitle: item.label,
          category: e.category,
          comment: item.comment,
          reviewerName,
        });
      }
    }
  }
  return rows;
}

// ─── Pure helpers (testable without a component) ──────────────

/**
 * BR-2 guard rendered client-side: an employee cannot be nominated as a PEER of
 * themselves (self-assessment is a separate category). Returns true when the candidate
 * is a valid peer for the reviewee. Self for OTHER categories (e.g. listing) is
 * irrelevant; this only blocks self-as-peer. The backend re-validates on save.
 */
export function isValidPeerNomination(
  revieweeId: string,
  candidateId: string,
): boolean {
  return revieweeId !== candidateId;
}

/**
 * AC-1: whether a reviewer row can be removed in the UI. Self + the auto-assigned
 * Manager are locked (server-owned); manual Peer / Direct Report rows are removable.
 */
export function isRemovableReviewer(reviewer: {
  locked: boolean;
  category: ReviewerCategory;
}): boolean {
  return !reviewer.locked && reviewer.category !== 'Self';
}

/**
 * §8 completion-tracker math: total reviewers and the completion percentage for a
 * category. `submitted / (submitted + pending + overdue)` rounded to a whole percent;
 * returns 0% when there are no reviewers in the category. Pure + exported for QA.
 */
export function categoryCompletionPercent(progress: {
  submitted: number;
  pending: number;
  overdue: number;
}): number {
  const total = progress.submitted + progress.pending + progress.overdue;
  if (total <= 0) {
    return 0;
  }
  return Math.round((progress.submitted / total) * 100);
}

/** Total reviewers across a category's three buckets (drives the "N of M" hint). */
export function categoryReviewerTotal(progress: {
  submitted: number;
  pending: number;
  overdue: number;
}): number {
  return progress.submitted + progress.pending + progress.overdue;
}

/**
 * BR-4 warning: true when a category has fewer reviewers than its required minimum, so
 * HR is warned before releasing results. Pure so QA can assert it directly.
 */
export function isBelowMinimum(progress: {
  submitted: number;
  pending: number;
  overdue: number;
  minimum: number;
}): boolean {
  return categoryReviewerTotal(progress) < progress.minimum;
}

/**
 * AC-3 submit gate: every question must be rated (no all-comment requirement here, FR-4
 * comments are optional). At least one question. Pure + exported so QA can assert it
 * without a component.
 */
export function canSubmitFeedback(
  questions: { rating: number | null }[],
): boolean {
  if (questions.length === 0) {
    return false;
  }
  return questions.every((q) => q.rating != null && q.rating > 0);
}

/** Titles of questions still unrated (drives the blocked-submit hint, AC-3). */
export function unratedQuestionTitles(
  questions: { title: string; rating: number | null }[],
): string[] {
  return questions
    .filter((q) => q.rating == null || q.rating <= 0)
    .map((q) => q.title);
}

/**
 * AC-4 visual: bar width as a 0-100 percentage for a rating on the tenant scale. Used
 * by both the per-competency bars and the per-category comparison (the radar rendered
 * as a non-chart CSS visual). Returns 0 when the value is null/invalid.
 */
export function ratingBarPercent(
  rating: number | null,
  ratingScaleMax: number,
): number {
  if (rating == null || rating <= 0 || ratingScaleMax <= 0) {
    return 0;
  }
  return Math.round(Math.min(rating / ratingScaleMax, 1) * 100);
}

/**
 * §8 color band for a rating given the scale max: green top third, amber middle, red
 * bottom. Returns Tailwind classes. Shared across the results visuals so perspectives
 * are comparable (same convention as US-PRF-003 `ratingBandClasses`).
 */
export function ratingBand360Classes(
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

/** Initials fallback for an avatar chip when no avatarUrl is provided (§8). */
export function initials(name: string): string {
  const parts = (name ?? '').trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) {
    return '?';
  }
  if (parts.length === 1) {
    return parts[0].charAt(0).toUpperCase();
  }
  return (
    parts[0].charAt(0).toUpperCase() +
    parts[parts.length - 1].charAt(0).toUpperCase()
  );
}
