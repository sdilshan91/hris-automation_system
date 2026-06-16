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
 * ── ASSUMED backend contract (backend agent must confirm/reconcile) ────────────
 * `apiBaseUrl` already includes `/api/v1`. All under `/performance/sign-off`. Tenant
 * + acting user resolved server-side from the session (FE sends no tenant/user id);
 * `Performance.Review.Team`/`.All` (manager/HR notes + request) and review-ownership
 * (employee acknowledge/dispute) + RLS (NFR-2). Sign-off records are append-only and
 * IMMUTABLE once recorded (NFR-3) — the server rejects any mutation of a signature.
 *
 *   GET   /performance/sign-off/reviews/{reviewId}
 *         → IReviewSignoff (the whole sign-off record: meeting notes, status, the
 *           manager + employee signatures/dispute, the goal+rating snapshot, and the
 *           workflow timeline). One call drives every US-PRF-006 screen.
 *
 *   PUT   /performance/sign-off/reviews/{reviewId}/notes
 *         body ISaveMeetingNotesRequest → IReviewSignoff
 *         Saves/updates the meeting notes (sanitized HTML). Allowed only while the
 *         status is NotesDraft (BR-1/BR-5: locked once sign-off is requested).
 *
 *   POST  /performance/sign-off/reviews/{reviewId}/request
 *         body ISaveMeetingNotesRequest → IReviewSignoff
 *         AC-2: persists the notes, records the MANAGER signature (name+timestamp+IP
 *         server-side), flips status → PendingEmployeeSignOff, notifies the employee.
 *
 *   POST  /performance/sign-off/reviews/{reviewId}/acknowledge → IReviewSignoff
 *         AC-3: records the EMPLOYEE signature (name+timestamp+IP server-side), flips
 *         status → SignedOff (and Completed once the cycle closes). No body.
 *
 *   POST  /performance/sign-off/reviews/{reviewId}/dispute
 *         body IDisputeRequest → IReviewSignoff
 *         AC-3/FR-4/FR-5: records the employee's dispute comments, flips status →
 *         Disputed, notifies the manager + HR for resolution.
 *
 *   POST  /performance/sign-off/reviews/{reviewId}/resolve
 *         body IResolveDisputeRequest → IReviewSignoff
 *         BR-4: HR resolves a dispute by Amend (reopen notes → NotesDraft) or Confirm
 *         (uphold the review → back to PendingEmployeeSignOff). HR-only.
 *
 *   GET   /performance/sign-off/reviews/{reviewId}/export (OPTIONAL) →
 *         application/pdf (HttpResponse<Blob> + Content-Disposition filename). FR-6.
 *         The FE wires "Export PDF" only when `exportAvailable` is true.
 *
 * Bare payloads (US-PLT-001 unwrap); PascalCase enum strings (US-PLT-003), never ints.
 */

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
