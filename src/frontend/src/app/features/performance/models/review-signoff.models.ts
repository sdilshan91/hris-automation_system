/**
 * US-PRF-006: Performance Review Meeting Notes & Sign-Off models matching the
 * (ASSUMED) backend API contract. This extends the manager-review workflow
 * (US-PRF-003): after the manager review is submitted, the manager records meeting
 * notes (rich text) and requests the employee's digital sign-off; the employee then
 * acknowledges (signs) or disputes; HR resolves disputes; the completed record is a
 * full, auditable view.
 *
 * The service layer (`ReviewSignoffService`) is intentionally thin so a route/DTO
 * mismatch is a one-file fix once the backend lands (like US-PRF-001..005).
 *
 * ── Backend contract (reconciled — BUG-243 re-keying + ISSUE-288 self endpoints) ─
 * `apiBaseUrl` already includes `/api/v1`; all under `/tenant/performance`. Tenant +
 * acting user resolved server-side from the session (FE sends no tenant/user id). Sign-off
 * records are append-only and IMMUTABLE once recorded (NFR-3).
 *
 * MANAGER / HR (keyed by cycleId + employeeId; `Performance.Review.Team`/`.All`) — the FE
 * resolves cycleId via `cycles/active`, employeeId from the route:
 *   GET  reviews/cycles/{cycleId}/employees/{employeeId}/notes            → IReviewSignoff
 *   PUT  reviews/cycles/{cycleId}/employees/{employeeId}/notes            (save meeting notes)
 *   POST reviews/cycles/{cycleId}/employees/{employeeId}/request-signoff  (AC-2: request sign-off)
 *   POST reviews/cycles/{cycleId}/employees/{employeeId}/resolve-dispute  (BR-4: HR resolves; `.All`)
 *   GET  reviews/cycles/{cycleId}/employees/{employeeId}/export           (AC-4/FR-6: review record)
 *
 * EMPLOYEE SELF-SERVICE (ISSUE-288 — caller-scoped; `Performance.Read.Self`; the server
 * resolves the caller's OWN employeeId + the active cycleId, so the FE sends neither):
 *   GET  reviews/cycles/active/me/notes        → IReviewSignoff (drives the employee sign-off screen)
 *   POST reviews/cycles/active/me/acknowledge  (AC-3/FR-4: records the employee signature → SignedOff + lock)
 *   POST reviews/cycles/active/me/dispute       body { comments } (AC-3/FR-4/FR-5: → Disputed; notifies manager + HR)
 *
 * Bare payloads (US-PLT-001 unwrap); PascalCase enum strings (US-PLT-003), never ints.
 */

import type { Schema } from '@core/api';

// ─── Enums ────────────────────────────────────────────────────

/**
 * Sign-off workflow status (AC-2/AC-3/AC-4 + the §8 timeline). Matches the C# enum.
 *  - NotesDraft: manager is drafting meeting notes (editable, BR-1).
 *  - PendingEmployeeSignOff: notes locked; awaiting the employee's signature (AC-2).
 *  - SignedOff: employee acknowledged & signed; review locked (AC-3/BR-5).
 *  - Disputed: employee disputed; escalated to HR (FR-5/BR-4).
 *  - Completed: review fully closed after sign-off (AC-4).
 *  - NoResponse: employee did not sign within the window; auto-closed (BR-3).
 */
export type SignoffStatus =
  | 'NotesDraft'
  | 'PendingEmployeeSignOff'
  | 'SignedOff'
  | 'Disputed'
  | 'Completed'
  | 'NoResponse';

export const SIGNOFF_STATUS_BADGE: Record<SignoffStatus, string> = {
  NotesDraft: 'bg-neutral-100 text-neutral-600 ring-neutral-500/20',
  PendingEmployeeSignOff: 'bg-amber-50 text-amber-700 ring-amber-600/20',
  SignedOff: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  Disputed: 'bg-rose-50 text-rose-700 ring-rose-600/20',
  Completed: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  NoResponse: 'bg-neutral-100 text-neutral-500 ring-neutral-400/20',
};

export const SIGNOFF_STATUS_LABEL: Record<SignoffStatus, string> = {
  NotesDraft: 'Notes draft',
  PendingEmployeeSignOff: 'Pending employee sign-off',
  SignedOff: 'Signed off',
  Disputed: 'Disputed',
  Completed: 'Completed',
  NoResponse: 'No response',
};

/** BR-4: how HR resolves a dispute. */
export type DisputeResolution = 'Amend' | 'Confirm';

// ─── DTOs ─────────────────────────────────────────────────────

/** Read-only goal + rating snapshot shown in the notes template + completed record. */
export interface ISignoffGoal {
  goalId: string;
  title: string;
  weight: number;
  /** Manager rating on the tenant scale (1..ratingScaleMax); null if not rated. */
  managerRating: number | null;
}

/** An immutable digital signature record (NFR-3 / §7). */
export interface ISignature {
  /** Signer's display name as captured at sign time. */
  name: string;
  /** ISO timestamp the signature was recorded. */
  signedOn: string;
  /** Captured client IP (server-side, FR-7); may be omitted in the FE payload. */
  ipAddress?: string | null;
}

/** One step on the §8 status timeline (AC-4). */
export interface ISignoffTimelineEntry {
  key:
    | 'NotesAdded'
    | 'SignOffRequested'
    | 'EmployeeSigned'
    | 'Disputed'
    | 'Completed';
  label: string;
  /** ISO timestamp the step occurred; null if not yet reached. */
  occurredOn: string | null;
  /** True once this step has happened. */
  done: boolean;
}

/**
 * The whole sign-off record for one manager review (one GET drives every screen).
 * The backend is authoritative on `status` and on whether notes are editable; the
 * FE derives edit/sign affordances from `status` + the signature presence, never
 * from dates.
 */
export interface IReviewSignoff {
  /** The manager-review record id (US-PRF-003) this sign-off belongs to. */
  reviewId: string;
  cycleId: string;
  cycleName: string;
  employeeId: string;
  employeeName: string;
  jobTitle?: string | null;
  managerName: string;
  status: SignoffStatus;
  /** Tenant-configured rating scale max (mirrors US-PRF-003). */
  ratingScaleMax: number;
  /** Final combined score the server computed (display only); null if not set. */
  finalScore: number | null;
  /** Sanitized meeting-notes HTML (FR-1/§Assumptions: stored as sanitized HTML). */
  meetingNotesHtml: string;
  /** Manager's signature once sign-off is requested (AC-2); null before. */
  managerSignature: ISignature | null;
  /** Employee's signature once acknowledged (AC-3); null before / if disputed. */
  employeeSignature: ISignature | null;
  /** Employee's dispute comments once disputed (FR-4); null otherwise. */
  disputeComments: string | null;
  /** ISO timestamp the employee disputed; null otherwise. */
  disputedOn: string | null;
  /** BR-2: whether the employee has opened/read the notes. Display only. */
  employeeViewed: boolean;
  /** Read-only goal + manager-rating snapshot for the template + record (AC-4). */
  goals: ISignoffGoal[];
  /** §8 workflow timeline (AC-4). The server may omit it; the FE derives it then. */
  timeline?: ISignoffTimelineEntry[];
  /** FR-6: whether a server-side PDF export endpoint is available for this record. */
  exportAvailable: boolean;
}

/** Notes save / request-sign-off body. */
export interface ISaveMeetingNotesRequest {
  /** Sanitized meeting-notes HTML. */
  meetingNotesHtml: string;
}

/** Employee dispute body (FR-4: comments are mandatory). */
export interface IDisputeRequest {
  comments: string;
}

/** HR dispute-resolution body (BR-4). */
export interface IResolveDisputeRequest {
  resolution: DisputeResolution;
  /** HR's resolution note (optional context for the audit trail). */
  note?: string;
}

// ─── Validation constants (mirror the backend) ────────────────

/** FR-4: minimum dispute-comment length to submit a dispute. */
export const DISPUTE_COMMENT_MIN = 10;

/** §8 confirmation-modal copy (QA may assert verbatim). */
export const SIGNOFF_CONFIRM_MESSAGE =
  'By signing, you acknowledge this review has been discussed.';

// ─── Pure helpers (testable without a component) ──────────────

/**
 * AC-1: build the pre-populated meeting-notes template (HTML). Sections: strengths,
 * areas for improvement, agreed development actions (with a deadline hint), and an
 * overall discussion summary — referencing the goal titles + manager ratings (§8).
 * Returns sanitized-safe markup (only headings/paragraphs/lists), no scripts.
 */
export function buildMeetingNotesTemplate(
  goals: ISignoffGoal[],
  ratingScaleMax: number,
): string {
  const escape = (s: string): string =>
    s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

  const goalLines =
    goals.length > 0
      ? goals
          .map(
            (g) =>
              `<li>${escape(g.title)} — rating ${
                g.managerRating != null
                  ? `${g.managerRating}/${ratingScaleMax}`
                  : 'not rated'
              }</li>`,
          )
          .join('')
      : '<li>No goals on record.</li>';

  return [
    '<h3>Key strengths</h3>',
    '<p></p>',
    '<h3>Areas for improvement</h3>',
    '<p></p>',
    '<h3>Agreed development actions (with deadlines)</h3>',
    '<ul><li>Action — owner — due date</li></ul>',
    '<h3>Overall discussion summary</h3>',
    '<p></p>',
    '<h3>Goals reviewed</h3>',
    `<ul>${goalLines}</ul>`,
  ].join('');
}

/** Treat empty contenteditable markup as blank so "notes required" gates correctly. */
export function notesAreEmpty(html: string | null | undefined): boolean {
  if (!html) {
    return true;
  }
  const stripped = html
    .replace(/<br\s*\/?>/gi, '')
    .replace(/<p>\s*<\/p>/gi, '')
    .replace(/<[^>]+>/g, '')
    .replace(/&nbsp;/gi, '')
    .trim();
  return stripped.length === 0;
}

/** FR-4 submit gate: dispute comments must be ≥ DISPUTE_COMMENT_MIN chars. */
export function canSubmitDispute(comments: string | null | undefined): boolean {
  return (comments ?? '').trim().length >= DISPUTE_COMMENT_MIN;
}

/** AC-1/BR-1: the manager may edit notes only while in NotesDraft. */
export function notesEditable(status: SignoffStatus): boolean {
  return status === 'NotesDraft';
}

/** AC-3: the employee may acknowledge/dispute only while pending their sign-off. */
export function employeeCanRespond(status: SignoffStatus): boolean {
  return status === 'PendingEmployeeSignOff';
}

/** AC-4: a terminal/locked status (record is read-only). */
export function signoffIsLocked(status: SignoffStatus): boolean {
  return (
    status === 'SignedOff' ||
    status === 'Completed' ||
    status === 'NoResponse'
  );
}

/**
 * §8/AC-4 status timeline: Notes Added → Sign-Off Requested → Employee Signed /
 * Disputed → Completed. Uses the server timeline if provided; otherwise derives the
 * steps from the record's signatures + status so the view always renders. Pure +
 * exported so QA can assert the rendered steps without a component.
 */
export function buildSignoffTimeline(
  record: IReviewSignoff,
): ISignoffTimelineEntry[] {
  if (record.timeline && record.timeline.length > 0) {
    return record.timeline;
  }

  const managerOn = record.managerSignature?.signedOn ?? null;
  const employeeOn = record.employeeSignature?.signedOn ?? null;
  const disputed = record.status === 'Disputed' || record.disputedOn != null;
  const completed = record.status === 'Completed';

  const entries: ISignoffTimelineEntry[] = [
    {
      key: 'NotesAdded',
      label: 'Notes added',
      occurredOn: managerOn,
      done: managerOn != null || record.status !== 'NotesDraft',
    },
    {
      key: 'SignOffRequested',
      label: 'Sign-off requested',
      occurredOn: managerOn,
      done:
        managerOn != null ||
        record.status === 'PendingEmployeeSignOff' ||
        record.status === 'SignedOff' ||
        completed ||
        disputed,
    },
  ];

  if (disputed) {
    entries.push({
      key: 'Disputed',
      label: 'Disputed',
      occurredOn: record.disputedOn,
      done: true,
    });
  } else {
    entries.push({
      key: 'EmployeeSigned',
      label: 'Employee signed',
      occurredOn: employeeOn,
      done: employeeOn != null,
    });
  }

  entries.push({
    key: 'Completed',
    label: 'Completed',
    occurredOn: completed ? (employeeOn ?? managerOn) : null,
    done: completed,
  });

  return entries;
}

// ─── Wire contract → view-model mapper (US-PRF-006 D-perf slice 3) ────────────
//
// All nine reads consume the GENERATED `PerformanceReviewMeetingNotesDto`. Drift
// reconciled here: `reviewId ← managerReviewId`, `meetingNotesHtml ← body`, the status
// enum is remapped (wire `NotStarted`/`NotesAdded` → FE `NotesDraft`), and the manager/
// employee signatures + dispute are read out of the `signoffs[]` audit array (the notes
// DTO has no flat signature fields).
export type ReviewMeetingNotesWire =
  Schema<'PerformanceReviewMeetingNotesDto'>;
export type ReviewSignoffEntryWire =
  Schema<'PerformanceReviewSignoffEntryDto'>;

/** Wire ReviewSignoffStatus → FE SignoffStatus (the wire has no `Completed`). */
const SIGNOFF_STATUS_WIRE_MAP: Record<string, SignoffStatus> = {
  NotStarted: 'NotesDraft',
  NotesAdded: 'NotesDraft',
  PendingEmployeeSignOff: 'PendingEmployeeSignOff',
  SignedOff: 'SignedOff',
  Disputed: 'Disputed',
  NoResponse: 'NoResponse',
};

function toSignature(
  entry: ReviewSignoffEntryWire | undefined,
): ISignature | null {
  if (!entry) {
    return null;
  }
  return {
    name: entry.signerName ?? '',
    signedOn: entry.signedAt ?? '',
    ipAddress: entry.clientIpAddress ?? null,
  };
}

/**
 * Maps `PerformanceReviewMeetingNotesDto` onto `IReviewSignoff`. The notes DTO is
 * notes-centric: it does NOT carry the review's goal/rating snapshot, rating scale,
 * manager name, cycle name, final score, or an export flag — those are defaulted here
 * and REPORTED (the sign-off screen renders them, so this is a real backend gap, not a
 * fabricated value).
 */
export function mapReviewSignoff(w: ReviewMeetingNotesWire): IReviewSignoff {
  const signoffs = w.signoffs ?? [];
  const byAction = (action: string): ReviewSignoffEntryWire | undefined =>
    signoffs.find((s) => (s.actionName ?? s.action) === action);
  const disputeEntry = byAction('Disputed');
  const rawStatus = w.signoffStatusName ?? w.signoffStatus;
  return {
    reviewId: w.managerReviewId ?? '',
    cycleId: w.cycleId ?? '',
    // No wire source on the notes DTO — defaulted (reported).
    cycleName: '',
    employeeId: w.employeeId ?? '',
    employeeName: w.employeeName ?? '',
    // No wire source — defaulted (reported).
    jobTitle: null,
    // No wire source — defaulted (reported).
    managerName: '',
    status:
      (rawStatus ? SIGNOFF_STATUS_WIRE_MAP[rawStatus] : undefined) ??
      'NotesDraft',
    // No wire source — defaulted (reported).
    ratingScaleMax: 0,
    // No wire source — defaulted (reported).
    finalScore: null,
    meetingNotesHtml: w.body ?? '',
    managerSignature: toSignature(byAction('RequestedSignOff')),
    employeeSignature: toSignature(byAction('Acknowledged')),
    disputeComments: disputeEntry?.comments ?? null,
    disputedOn: disputeEntry?.signedAt ?? null,
    // No wire source — defaulted (reported).
    employeeViewed: false,
    // No wire source: the notes DTO carries no goal/rating snapshot — defaulted (reported).
    goals: [],
    // No wire source — defaulted (reported).
    exportAvailable: false,
  };
}
