/**
 * US-REC-006: Interview scorecard models matching the backend API contract.
 *
 * The scorecard shape, the criterion ratings, the submit request body, and the
 * recruiter's consolidated view live HERE so a backend DTO mismatch is a one-place
 * fix. Responses are the BARE `T` — the global apiEnvelopeInterceptor (US-PLT-001)
 * unwraps the `ApiResponse<T>` envelope, so the service never touches `.data`.
 *
 * CONTRACT (reconciled with the backend agent's InterviewsController +
 * ScorecardDtos; `apiBaseUrl` already includes `/api/v1`):
 *
 *   GET  /recruitment/scorecard-criteria
 *        -> IScorecardCriterion[]   { key, label }       (FR-1 / BR-2 defaults)
 *   GET  /recruitment/interviews/:interviewId/scorecards
 *        -> IScorecardBoard (ScorecardSummaryDto)         (AC-2 / AC-3 / FR-6)
 *   POST /recruitment/interviews/:interviewId/scorecard   (SINGULAR path)
 *        body IScorecardRequest -> IScorecard (201)        (AC-1 / FR-1; also EDITS)
 *
 * There is NO separate PUT endpoint — re-POSTing to the same interview edits the
 * current interviewer's scorecard within the lock window (FR-2 / BR-4); the
 * backend 409s once locked.
 *
 * All requests use `withCredentials` (httpOnly cookie) and are tenant-scoped via
 * the tenantInterceptor (X-Tenant-Subdomain header) + backend RLS (AC-4 / NFR-2).
 *
 * Enum string values MUST match the C# member names (PascalCase) — the API uses a
 * global JsonStringEnumConverter (US-PLT-003). Do NOT use lowercase/kebab.
 */

// ─── Enums ────────────────────────────────────────────────────

/**
 * Overall hiring recommendation (FR-1 / BR-3, mandatory). Matches the C#
 * `OverallRecommendation` member names (PascalCase, US-PLT-003).
 */
export type OverallRecommendation =
  | 'StrongHire'
  | 'Hire'
  | 'NoHire'
  | 'StrongNoHire';

/** The fixed 1-5 rating scale (FR-1, fixed in Phase 1). */
export type RatingScore = 1 | 2 | 3 | 4 | 5;

/**
 * Ordered options for the recommendation segmented control (§8 color coding):
 * Strong Hire=green, Hire=light green, No Hire=orange, Strong No Hire=red.
 */
export const RECOMMENDATION_OPTIONS: readonly {
  value: OverallRecommendation;
  label: string;
  /** Tailwind classes for the SELECTED segment. */
  activeClass: string;
  /** Tailwind ring/text for the swatch dot (consolidated table). */
  dotClass: string;
}[] = [
  {
    value: 'StrongHire',
    label: 'Strong Hire',
    activeClass: 'bg-green-600 text-white ring-green-600',
    dotClass: 'bg-green-500',
  },
  {
    value: 'Hire',
    label: 'Hire',
    activeClass: 'bg-emerald-400 text-emerald-950 ring-emerald-400',
    dotClass: 'bg-emerald-400',
  },
  {
    value: 'NoHire',
    label: 'No Hire',
    activeClass: 'bg-orange-500 text-white ring-orange-500',
    dotClass: 'bg-orange-500',
  },
  {
    value: 'StrongNoHire',
    label: 'Strong No Hire',
    activeClass: 'bg-red-600 text-white ring-red-600',
    dotClass: 'bg-red-600',
  },
];

/** Human labels for the recommendation (cards/tables). */
export const RECOMMENDATION_LABELS: Record<OverallRecommendation, string> = {
  StrongHire: 'Strong Hire',
  Hire: 'Hire',
  NoHire: 'No Hire',
  StrongNoHire: 'Strong No Hire',
};

/** Badge classes per recommendation (consolidated table chip). */
export const RECOMMENDATION_BADGE: Record<OverallRecommendation, string> = {
  StrongHire: 'bg-green-50 text-green-700 ring-green-200',
  Hire: 'bg-emerald-50 text-emerald-700 ring-emerald-200',
  NoHire: 'bg-orange-50 text-orange-700 ring-orange-200',
  StrongNoHire: 'bg-red-50 text-red-700 ring-red-200',
};

/** Labels for the 1-5 rating scale (FR-1). */
export const SCORE_LABELS: Record<RatingScore, string> = {
  1: 'Poor',
  2: 'Below Average',
  3: 'Average',
  4: 'Good',
  5: 'Excellent',
};

/** All scores, ascending — backs the rating button row. */
export const SCORE_VALUES: readonly RatingScore[] = [1, 2, 3, 4, 5];

// ─── Entities ─────────────────────────────────────────────────

/**
 * One tenant-configured evaluation criterion (FR-1 / BR-2). `key` is the stable
 * identifier the request sends back; `label` is the display text. Defaults:
 * Technical Skills, Communication, Problem Solving, Cultural Fit. Matches the
 * backend `ScorecardCriterionDto { key, label }`.
 */
export interface IScorecardCriterion {
  /** Stable identifier sent back in the request. */
  key: string;
  /** Display label, e.g. "Technical Skills". */
  label: string;
}

/**
 * A single criterion rating within a scorecard (FR-1). On READ the backend also
 * supplies `criterionLabel` (display text); on WRITE only key/score/comment are
 * sent.
 */
export interface ICriterionRating {
  criterionKey: string;
  /** 1-5 (FR-1). */
  score: RatingScore;
  /** Optional per-criterion comment. */
  comment?: string | null;
  /** Display label resolved by the backend on reads (e.g. "Technical Skills"). */
  criterionLabel?: string | null;
}

/**
 * A submitted scorecard for one interviewer on one interview (AC-2 / AC-3),
 * matching the backend `ScorecardDto`. `averageScore` is server-computed (FR-3).
 * `isLocked` reflects whether the lock window has passed (FR-2 / BR-4); the FE
 * renders read-only when true.
 */
export interface IScorecard {
  id: string;
  interviewId: string;
  interviewerEmployeeId: string;
  /** Display name of the interviewer (backend joins from the employee record). */
  interviewerName: string;
  overallRecommendation: OverallRecommendation;
  /** PascalCase enum name echoed by the backend (display convenience). */
  overallRecommendationName?: string | null;
  ratings: ICriterionRating[];
  generalNotes?: string | null;
  /** Mean of the criterion scores, server-computed (FR-3). */
  averageScore: number;
  submittedAt: string;
  /** ISO lock timestamp (when edits stop being allowed, BR-4). */
  lockedAt?: string | null;
  /** True once the lock period has passed — the scorecard is immutable (BR-4). */
  isLocked?: boolean;
}

/**
 * The consolidated scorecards view for one interview or applicant (AC-2 / AC-3 /
 * FR-6), matching the backend `ScorecardSummaryDto`.
 *
 * Anti-bias (FR-6 / BR-5): when the caller is an assigned interviewer who has NOT
 * yet submitted, the backend OMITS the other interviewers' scorecards and sets
 * `antiBiasApplies = true` (and `hiddenCount` to how many are hidden). The FE
 * renders a "Submit your scorecard first" overlay in that case. The backend is
 * the authority; the FE only reflects the flag (the FE `IUser` has no employeeId
 * to derive interviewer identity itself).
 */
export interface IScorecardBoard {
  /** Every submitted scorecard the caller is permitted to see. */
  scorecards: IScorecard[];
  /** Aggregate mean across the VISIBLE scorecards' averages (FR-3), or null. */
  aggregateAverageScore: number | null;
  /** How many scorecards exist but are hidden from this caller (FR-6 / BR-5). */
  hiddenCount: number;
  /**
   * Anti-bias gate (FR-6 / BR-5): true means the caller is an assigned
   * interviewer who has not submitted, so other scorecards are hidden behind the
   * overlay. False for recruiters (see everything) and interviewers who already
   * submitted. Defaults to false when absent so a recruiter view is never gated.
   */
  antiBiasApplies: boolean;
}

/**
 * Submit / edit request (FR-1). `interviewId` is in the path; the body carries the
 * ratings, recommendation, and optional general notes. The interviewer identity
 * comes from the auth context server-side (BR-1) — never sent by the FE. Re-POST
 * to the same interview edits within the lock window (FR-2 — no separate PUT).
 */
export interface IScorecardRequest {
  ratings: ICriterionRating[];
  overallRecommendation: OverallRecommendation;
  generalNotes?: string | null;
}

// ─── Helpers ──────────────────────────────────────────────────

/** Human label for a recommendation, tolerating unknown values. */
export function recommendationLabel(
  value: OverallRecommendation | string,
): string {
  return RECOMMENDATION_LABELS[value as OverallRecommendation] ?? String(value);
}

/** Badge classes for a recommendation, falling back to the No-Hire style. */
export function recommendationBadge(
  value: OverallRecommendation | string,
): string {
  return (
    RECOMMENDATION_BADGE[value as OverallRecommendation] ??
    RECOMMENDATION_BADGE.NoHire
  );
}

/** Label for a 1-5 score (e.g. "4 · Good"); tolerates out-of-range values. */
export function scoreLabel(score: number): string {
  const lbl = SCORE_LABELS[score as RatingScore];
  return lbl ? `${score} · ${lbl}` : String(score);
}

/**
 * Mean of a list of criterion scores, rounded to one decimal. Returns 0 for an
 * empty list. Pure so it is unit-testable and so the FE can show a live average
 * in the form before the server computes the authoritative value (FR-3).
 */
export function averageOf(ratings: ICriterionRating[]): number {
  const scores = (ratings ?? [])
    .map((r) => r.score)
    .filter((s) => typeof s === 'number');
  if (scores.length === 0) {
    return 0;
  }
  const sum = scores.reduce((a, b) => a + b, 0);
  return Math.round((sum / scores.length) * 10) / 10;
}

/**
 * Aggregate mean across all scorecards' average scores (FR-3), rounded to one
 * decimal. Returns null for an empty list. Pure/unit-testable.
 */
export function aggregateOf(scorecards: IScorecard[]): number | null {
  const avgs = (scorecards ?? [])
    .map((s) => s.averageScore)
    .filter((a) => typeof a === 'number');
  if (avgs.length === 0) {
    return null;
  }
  const sum = avgs.reduce((a, b) => a + b, 0);
  return Math.round((sum / avgs.length) * 10) / 10;
}

/**
 * Whether a scorecard is still editable (FR-2 / BR-4). Treats `isLocked === true`
 * OR a present `lockedAt` as locked; otherwise editable. The backend is the
 * authority (it re-checks on submit); this is just the UI gate.
 */
export function isScorecardEditable(card: IScorecard): boolean {
  if (card.isLocked === true) {
    return false;
  }
  if (card.lockedAt) {
    return false;
  }
  return true;
}

/**
 * Initials from a display name (avatar fallback) — local copy so the scorecard
 * components don't depend on the interview models.
 */
export function scorecardInitials(name: string): string {
  const parts = (name ?? '').trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) {
    return '';
  }
  const first = parts[0][0] ?? '';
  const last = parts.length > 1 ? parts[parts.length - 1][0] ?? '' : '';
  return `${first}${last}`.toUpperCase();
}
