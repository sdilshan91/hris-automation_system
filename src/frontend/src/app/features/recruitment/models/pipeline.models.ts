/**
 * US-REC-003: Recruiter applicant-pipeline (Kanban) models matching the backend
 * API contract.
 *
 * The pipeline board, applicant detail, and stage-transition contract live HERE so
 * a backend DTO mismatch is a one-place fix. Responses are the BARE `T` — the
 * global apiEnvelopeInterceptor (US-PLT-001) unwraps the `ApiResponse<T>`
 * envelope, so the service never touches `.data`.
 *
 * CONTRACT (assumed — reconcile with backend; `apiBaseUrl` already includes
 * `/api/v1`):
 *
 *   GET  /recruitment/vacancies/:vacancyId/pipeline?stage=&source=&from=&to=&search=
 *        -> IPipelineBoard { vacancyId, stages: IPipelineStage[], total }
 *   GET  /recruitment/applicants/:id
 *        -> IApplicantDetail (full profile + stageHistory[])
 *   POST /recruitment/applicants/:id/stage   body IStageChangeRequest
 *        -> IApplicantCard (the updated card; stamps audit + history server-side)
 *   GET  /recruitment/applicants/:id/resume  -> resume blob (download)
 *
 * All requests use withCredentials (httpOnly cookie) and are tenant-scoped via the
 * tenantInterceptor (X-Tenant-Subdomain header) + backend RLS (AC-5 / NFR-3).
 */

import { ApplicantStage, ApplicantSource } from './applicant.models';

// ─── Stage ordering / display (FR-1) ──────────────────────────

/**
 * Default pipeline stages in left-to-right sequence (FR-1). Tenants can rename /
 * add / remove stages later (§10); the board renders whatever stages the backend
 * returns, but this ordering is the fallback when the board is empty or for the
 * filter dropdown.
 */
export const PIPELINE_STAGES: readonly ApplicantStage[] = [
  'Applied',
  'Screening',
  'Interview',
  'Offer',
  'Hired',
  'Rejected',
] as const;

/** Stages that are "terminal" — moving here is a strong action (BR-6). */
export const TERMINAL_STAGES: readonly ApplicantStage[] = ['Hired', 'Rejected'];

/** Tailwind ring/text classes per stage column header chip (§8). */
export const STAGE_BADGE: Record<ApplicantStage, string> = {
  Applied: 'bg-neutral-100 text-neutral-700 ring-neutral-200',
  Screening: 'bg-blue-50 text-blue-700 ring-blue-200',
  Interview: 'bg-violet-50 text-violet-700 ring-violet-200',
  Offer: 'bg-amber-50 text-amber-700 ring-amber-200',
  Hired: 'bg-green-50 text-green-700 ring-green-200',
  Rejected: 'bg-red-50 text-red-700 ring-red-200',
};

/** Source pill colors (FR-2 — public/internal/referral). */
export const SOURCE_BADGE: Record<ApplicantSource, string> = {
  public: 'bg-sky-50 text-sky-700 ring-sky-200',
  internal: 'bg-indigo-50 text-indigo-700 ring-indigo-200',
  referral: 'bg-teal-50 text-teal-700 ring-teal-200',
};

/**
 * Structured rejection reason (US-REC-004 AC-4). These string values MUST match
 * the backend's `rejectionReason` enum exactly — the dialog sends the enum value
 * (`RejectionReason`), not the human label. Keep this list and the label map in
 * sync with the backend's enum; it is the single source of truth on the FE.
 */
export type RejectionReason =
  | 'NotQualified'
  | 'PositionFilled'
  | 'Withdrew'
  | 'Other';

/** Human-readable labels for the rejection dropdown (AC-4). */
export const REJECTION_REASON_LABELS: Record<RejectionReason, string> = {
  NotQualified: 'Not qualified',
  PositionFilled: 'Position filled',
  Withdrew: 'Withdrew',
  Other: 'Other',
};

/** Dropdown options in display order (AC-4). */
export const REJECTION_REASON_OPTIONS: readonly {
  value: RejectionReason;
  label: string;
}[] = (Object.keys(REJECTION_REASON_LABELS) as RejectionReason[]).map(
  (value) => ({ value, label: REJECTION_REASON_LABELS[value] }),
);

/**
 * Legacy free-text reason list (US-REC-003). Retained for the backward-move dialog
 * which still uses a free-text reason; rejection now uses the structured enum above.
 */
export const REJECTION_REASONS: readonly string[] = [
  'Not qualified',
  'Position filled',
  'Withdrew application',
  'Failed interview',
  'Salary expectations',
  'Other',
];

// ─── Entities ─────────────────────────────────────────────────

/** A compact applicant summary rendered as a Kanban card (FR-2). */
export interface IApplicantCard {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  source: ApplicantSource;
  isInternal: boolean;
  /** ISO timestamp — drives "applied X days ago" relative time (§8). */
  appliedAt: string;
  stage: ApplicantStage;
  /**
   * ISO timestamp of when the applicant entered the current stage (US-REC-004 §8).
   * Optional: when the backend omits it the FE falls back to `appliedAt` for the
   * "In <stage> N days" time-in-stage badge.
   */
  enteredStageAt?: string | null;
}

/** One Kanban column — a stage plus its ordered applicant cards + count (FR-5). */
export interface IPipelineStage {
  stage: ApplicantStage;
  count: number;
  applicants: IApplicantCard[];
}

/** The full Kanban board for a vacancy (FR-1 / FR-5). */
export interface IPipelineBoard {
  vacancyId: string;
  vacancyTitle?: string | null;
  stages: IPipelineStage[];
  /** Total applicants across all stages (FR-5). */
  total: number;
}

/** One stage-transition audit record shown in the detail timeline (BR-5 / FR-7). */
export interface IStageTransition {
  fromStage: ApplicantStage | null;
  toStage: ApplicantStage;
  changedByUserName?: string | null;
  reason?: string | null;
  notes?: string | null;
  /**
   * Structured rejection reason enum, present only on transitions into Rejected
   * (US-REC-004 AC-4). Shown in the timeline via its human label.
   */
  rejectionReason?: RejectionReason | null;
  /** ISO timestamp. */
  changedAt: string;
}

/** Full applicant record + stage history for the detail slide-over (AC-3 / FR-7). */
export interface IApplicantDetail {
  id: string;
  applicationReferenceNumber: string;
  vacancyId: string;
  firstName: string;
  lastName: string;
  email: string;
  phone: string | null;
  coverLetter: string | null;
  resumeFileName: string | null;
  stage: ApplicantStage;
  source: ApplicantSource;
  isInternal: boolean;
  appliedAt: string;
  /** Ordered oldest→newest transitions (FR-7 timeline). */
  stageHistory: IStageTransition[];
}

/** Body for a stage move (FR-3 / BR-3 / BR-4). Reason required for Rejected/backward. */
export interface IStageChangeRequest {
  toStage: ApplicantStage;
  /**
   * Free-text reason (required for backward moves BR-4). For rejections the
   * structured `rejectionReason` is used instead/as well (US-REC-004 AC-4).
   */
  reason?: string;
  /**
   * Structured rejection reason enum — sent only when moving to Rejected
   * (US-REC-004 AC-4). Must be one of `RejectionReason`.
   */
  rejectionReason?: RejectionReason;
  notes?: string;
}

/**
 * Result of a stage move (US-REC-004). The move is a SOFT gate: it succeeds even
 * when gate criteria fail or the headcount is filled — the backend returns
 * `warnings` for the recruiter to acknowledge (BR-4 / FR-1 / §8). A hard failure
 * (e.g. vacancy closed/cancelled, FR-8) is surfaced as an HTTP error instead.
 */
export interface IStageMoveResult {
  id: string;
  stage: ApplicantStage;
  /** ISO timestamp the applicant entered the new stage, if the backend returns it. */
  enteredStageAt?: string | null;
  /** Soft-gate / headcount warnings to show the recruiter (BR-4 / FR-1). */
  warnings: string[];
}

/** Query params for the board endpoint (FR-6 filters). */
export interface IPipelineFilters {
  stage?: ApplicantStage | null;
  source?: ApplicantSource | null;
  /** Applied-on-or-after date (yyyy-MM-dd). */
  from?: string | null;
  /** Applied-on-or-before date (yyyy-MM-dd). */
  to?: string | null;
  /** Free-text on name/email. */
  search?: string | null;
}

// ─── Helpers ──────────────────────────────────────────────────

/** Index of a stage in the canonical sequence (−1 if unknown). */
export function stageIndex(stage: ApplicantStage): number {
  return PIPELINE_STAGES.indexOf(stage);
}

/** True when moving `from`→`to` is a backward move in the canonical order (BR-4). */
export function isBackwardMove(from: ApplicantStage, to: ApplicantStage): boolean {
  const a = stageIndex(from);
  const b = stageIndex(to);
  // Unknown stages: don't treat as backward (let the server decide).
  if (a < 0 || b < 0) {
    return false;
  }
  // Rejected sits last in the array but is a "side" terminal, not a forward goal —
  // moving OUT of Rejected back into the funnel is a backward move.
  if (from === 'Rejected') {
    return true;
  }
  return b < a;
}

/** True when a move requires a reason (Rejected per BR-3, or any backward move BR-4). */
export function moveRequiresReason(
  from: ApplicantStage,
  to: ApplicantStage,
): boolean {
  return to === 'Rejected' || isBackwardMove(from, to);
}

/**
 * The forward stage in the canonical funnel (US-REC-004 AC-1/AC-2). Skips the
 * terminal "Rejected" side-stage. Returns null when there is no forward stage
 * (already at Hired, or in a terminal stage).
 */
export function nextStage(stage: ApplicantStage): ApplicantStage | null {
  if (TERMINAL_STAGES.includes(stage)) {
    return null;
  }
  const i = stageIndex(stage);
  if (i < 0) {
    return null;
  }
  const next = PIPELINE_STAGES[i + 1];
  // PIPELINE_STAGES ends with [..., 'Hired', 'Rejected']; never advance into Rejected.
  if (!next || next === 'Rejected') {
    return null;
  }
  return next;
}

/**
 * The previous active stage in the funnel for a backward move (US-REC-004 FR-5).
 * Returns null when there is no earlier stage (at Applied) or the stage is terminal.
 */
export function previousStage(stage: ApplicantStage): ApplicantStage | null {
  if (TERMINAL_STAGES.includes(stage)) {
    return null;
  }
  const i = stageIndex(stage);
  if (i <= 0) {
    return null;
  }
  return PIPELINE_STAGES[i - 1] ?? null;
}

/** Human label for a structured rejection reason, tolerating unknown values. */
export function rejectionReasonLabel(reason: RejectionReason | string): string {
  return (
    REJECTION_REASON_LABELS[reason as RejectionReason] ?? String(reason)
  );
}

/**
 * Time-in-stage badge text, e.g. "In Screening 5 days" (US-REC-004 §8). Computed
 * from `enteredStageAt` when the backend provides it, else falls back to
 * `appliedAt`. Returns an empty string when no timestamp is usable (graceful
 * omission). Same-day → "today".
 */
export function timeInStageLabel(
  stage: ApplicantStage,
  enteredStageAt: string | null | undefined,
  appliedAt: string | null | undefined,
  now: Date = new Date(),
): string {
  const iso = enteredStageAt || appliedAt;
  if (!iso) {
    return '';
  }
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) {
    return '';
  }
  const diffMs = Math.max(0, now.getTime() - then);
  const days = Math.floor(diffMs / 86400000);
  if (days < 1) {
    return `In ${stage} today`;
  }
  return `In ${stage} ${days} day${days === 1 ? '' : 's'}`;
}

/** "Applied 3 days ago" style relative time from an ISO timestamp (§8). */
export function relativeAppliedTime(iso: string, now: Date = new Date()): string {
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) {
    return '';
  }
  const diffMs = Math.max(0, now.getTime() - then);
  const minutes = Math.floor(diffMs / 60000);
  if (minutes < 1) {
    return 'just now';
  }
  if (minutes < 60) {
    return `${minutes} minute${minutes === 1 ? '' : 's'} ago`;
  }
  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return `${hours} hour${hours === 1 ? '' : 's'} ago`;
  }
  const days = Math.floor(hours / 24);
  if (days < 30) {
    return `${days} day${days === 1 ? '' : 's'} ago`;
  }
  const months = Math.floor(days / 30);
  if (months < 12) {
    return `${months} month${months === 1 ? '' : 's'} ago`;
  }
  const years = Math.floor(months / 12);
  return `${years} year${years === 1 ? '' : 's'} ago`;
}
