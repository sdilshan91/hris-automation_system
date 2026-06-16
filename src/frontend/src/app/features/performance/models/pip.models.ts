/**
 * US-PRF-008: Performance Improvement Plan (PIP) models matching the (ASSUMED)
 * backend API contract. HR-led, with manager + employee touchpoints: HR creates and
 * owns the lifecycle (create, extend, close, escalate — BR-1); a manager OR HR can
 * record checkpoints; the employee acknowledges initiation (BR-4) from their own
 * "My PIP" view under /my-review.
 *
 * The service layer (`PipService`) is intentionally thin so a route/DTO mismatch is a
 * one-file fix once the backend lands (like US-PRF-001..007).
 *
 * ── ASSUMED backend contract (backend agent must confirm/reconcile) ────────────
 * `apiBaseUrl` already includes `/api/v1`. All under `/performance/pips`. Tenant +
 * acting user resolved server-side from the session (FE sends no tenant/user id);
 * `Performance.Review.All` (HR create/extend/close/escalate, BR-1), manager OR HR
 * (record checkpoint, BR-1), PIP-ownership (employee acknowledge), + RLS (NFR-2).
 * PIP data is encrypted at rest (NFR-4) and EXCLUDED from general dashboards (BR-5)
 * — the FE never surfaces PIP in the dashboard.
 *
 *   GET   /performance/pips
 *         → IPipSummary[] (the HR PIP list; AC-1 entry point). Tolerates a `{ data }`
 *           page. Tenant-scoped to PIPs the caller may see (FR-8).
 *
 *   GET   /performance/pips/{pipId}
 *         → IPip (the whole PIP detail: timeline, objectives + their checkpoints,
 *           mentor, escalation config, signatures). One call drives the detail screen.
 *
 *   GET   /performance/pips/draft?employeeId={id}&reviewId={id?}
 *         → IPipDraft (AC-1: pre-fills the creation form — employee name, suggested
 *           reason if coming from a flagged review, configured escalation options,
 *           and whether the employee already has an active PIP, BR-2). employeeId is
 *           OPTIONAL: omit it to open a blank form (HR-initiated). The FE only sends
 *           reviewId when the HR officer arrived from a flagged manager review.
 *
 *   POST  /performance/pips  body ICreatePipRequest → IPip
 *         AC-2: creates the PIP (status → Active), notifies employee/manager/mentor,
 *         schedules Hangfire checkpoint reminders. Server re-validates min-30-days
 *         (BR-3), ≥1 objective, and one-active-PIP-per-employee (BR-2).
 *
 *   POST  /performance/pips/{pipId}/checkpoints/{checkpointId}  body IRecordCheckpointRequest → IPip
 *         AC-3: a manager OR HR records a SCHEDULED checkpoint (traffic-light status +
 *         evidence notes + optional attachment), notifies the employee. The checkpoint
 *         id identifies which scheduled slot is being recorded. Multipart when a file
 *         is attached (field `file`); JSON otherwise.
 *
 *   POST  /performance/pips/{pipId}/outcome  body ISetOutcomeRequest → IPip
 *         AC-4: HR-only (BR-1). SuccessfullyCompleted closes the PIP; Extended sets a
 *         new end date (+ optional new objectives, FR-6); NotMet flips to NotMet and
 *         unlocks the escalation step (AC-5).
 *
 *   POST  /performance/pips/{pipId}/escalate  body IEscalateRequest → IPip
 *         AC-5: HR-only. Confirms the configured escalation action once the outcome is
 *         NotMet, records an immutable audit entry, notifies stakeholders.
 *
 *   POST  /performance/pips/{pipId}/acknowledge → IPip
 *         BR-4: the EMPLOYEE acknowledges PIP initiation (records a signature). No body.
 *
 * Bare payloads (US-PLT-001 unwrap); PascalCase enum strings (US-PLT-003), never ints.
 */

// ─── Enums ────────────────────────────────────────────────────

/**
 * PIP status lifecycle (FR-2). Matches the C# enum.
 *  - Draft: HR is composing the PIP (not yet initiated).
 *  - Active: initiated; employee/manager/mentor notified; checkpoints scheduled.
 *  - Extended: outcome Extended → new end date set, still in progress (FR-6).
 *  - SuccessfullyCompleted: closed; employee returns to normal status (AC-4).
 *  - NotMet: outcome Not Met → escalation pending/recorded (AC-4/AC-5).
 *  - Cancelled: HR cancelled the PIP before completion.
 */
export type PipStatus =
  | 'Draft'
  | 'Active'
  | 'Extended'
  | 'SuccessfullyCompleted'
  | 'NotMet'
  | 'Cancelled';

export const PIP_STATUS_BADGE: Record<PipStatus, string> = {
  Draft: 'bg-neutral-100 text-neutral-600 ring-neutral-500/20',
  Active: 'bg-blue-50 text-blue-700 ring-blue-600/20',
  Extended: 'bg-amber-50 text-amber-700 ring-amber-600/20',
  SuccessfullyCompleted: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  NotMet: 'bg-rose-50 text-rose-700 ring-rose-600/20',
  Cancelled: 'bg-neutral-100 text-neutral-500 ring-neutral-400/20',
};

export const PIP_STATUS_LABEL: Record<PipStatus, string> = {
  Draft: 'Draft',
  Active: 'Active',
  Extended: 'Extended',
  SuccessfullyCompleted: 'Successfully completed',
  NotMet: 'Not met',
  Cancelled: 'Cancelled',
};

/**
 * Checkpoint traffic-light status (AC-3/§8). Matches the C# enum.
 *  - OnTrack: green — improving as expected.
 *  - AtRisk: amber — some progress but at risk of not meeting the objective.
 *  - NotMet: red — objective not being met.
 */
export type CheckpointStatus = 'OnTrack' | 'AtRisk' | 'NotMet';

/** Traffic-light option metadata for the AC-3 status selector. */
export interface ICheckpointStatusOption {
  value: CheckpointStatus;
  label: string;
  /** Dot/swatch colour class (the "traffic light"). */
  dotClass: string;
  /** Selected-state ring/background classes. */
  selectedClass: string;
}

export const CHECKPOINT_STATUS_OPTIONS: ICheckpointStatusOption[] = [
  {
    value: 'OnTrack',
    label: 'On track',
    dotClass: 'bg-emerald-500',
    selectedClass: 'border-emerald-500 bg-emerald-50 ring-emerald-500/30',
  },
  {
    value: 'AtRisk',
    label: 'At risk',
    dotClass: 'bg-amber-500',
    selectedClass: 'border-amber-500 bg-amber-50 ring-amber-500/30',
  },
  {
    value: 'NotMet',
    label: 'Not met',
    dotClass: 'bg-rose-500',
    selectedClass: 'border-rose-500 bg-rose-50 ring-rose-500/30',
  },
];

export const CHECKPOINT_STATUS_BADGE: Record<CheckpointStatus, string> = {
  OnTrack: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  AtRisk: 'bg-amber-50 text-amber-700 ring-amber-600/20',
  NotMet: 'bg-rose-50 text-rose-700 ring-rose-600/20',
};

export const CHECKPOINT_STATUS_LABEL: Record<CheckpointStatus, string> = {
  OnTrack: 'On track',
  AtRisk: 'At risk',
  NotMet: 'Not met',
};

/**
 * Final PIP outcome (AC-4). Matches the C# enum.
 *  - SuccessfullyCompleted: PIP closed, employee returns to normal status.
 *  - Extended: new end date set (+ optional new objectives, FR-6).
 *  - NotMet: triggers the configured escalation action (AC-5).
 */
export type PipOutcome = 'SuccessfullyCompleted' | 'Extended' | 'NotMet';

/**
 * Configurable escalation action (BR-6). Matches the C# enum. The set offered to HR
 * is whatever the tenant configured (`IPipDraft.escalationOptions` / `IPip`).
 */
export type EscalationAction =
  | 'Reassignment'
  | 'Demotion'
  | 'ContractNonRenewal'
  | 'TerminationRecommendation';

export const ESCALATION_ACTION_LABEL: Record<EscalationAction, string> = {
  Reassignment: 'Reassignment',
  Demotion: 'Demotion',
  ContractNonRenewal: 'Contract non-renewal',
  TerminationRecommendation: 'Termination recommendation',
};

/** Employee PIP-initiation acknowledgement state (BR-4). */
export type PipAcknowledgement = 'Pending' | 'Acknowledged' | 'NotAcknowledged';

// ─── DTOs ─────────────────────────────────────────────────────

/** One checkpoint assessment recorded against an objective (AC-3/FR-4). */
export interface IPipCheckpoint {
  checkpointId: string;
  /** Scheduled date for this checkpoint (ISO). */
  dueDate: string;
  /** When it was actually recorded (ISO); null if not yet recorded. */
  recordedOn: string | null;
  /** Traffic-light status; null until recorded. */
  status: CheckpointStatus | null;
  /** Evidence / progress notes (AC-3). */
  notes: string | null;
  /** Who recorded it (display name); null until recorded. */
  recordedBy: string | null;
  /** Attachment file name if one was uploaded (FR-4); null otherwise. */
  attachmentName: string | null;
  /** Whether this checkpoint is still awaiting a recorded assessment. */
  overdue: boolean;
}

/** An improvement objective with measurable success criteria (FR-1/AC-1). */
export interface IPipObjective {
  objectiveId: string;
  title: string;
  description: string;
  /** Measurable success criteria (FR-1). */
  successCriteria: string;
  /** Target completion date (ISO). */
  dueDate: string;
  /** Checkpoints attached to this objective (the accordion body, §8). */
  checkpoints: IPipCheckpoint[];
}

/** An immutable digital signature/acknowledgement record (§7/BR-4). */
export interface IPipSignature {
  name: string;
  signedOn: string;
  ipAddress?: string | null;
}

/** One immutable audit entry for the escalation decision (AC-5/FR-5). */
export interface IPipEscalation {
  action: EscalationAction;
  /** HR's rationale captured at confirmation. */
  note: string | null;
  confirmedBy: string;
  confirmedOn: string;
}

/** A PIP list row (AC-1 list). */
export interface IPipSummary {
  pipId: string;
  employeeId: string;
  employeeName: string;
  jobTitle?: string | null;
  status: PipStatus;
  startDate: string;
  endDate: string;
  /** 0..N objectives. */
  objectiveCount: number;
  /** Recorded vs scheduled checkpoints (e.g. 2 of 4). */
  checkpointsRecorded: number;
  checkpointsTotal: number;
  /** BR-4 acknowledgement state. */
  acknowledgement: PipAcknowledgement;
}

/**
 * The whole PIP record (one GET drives the detail/timeline screen). The backend is
 * authoritative on `status`; the FE derives affordances from status + the caller's
 * role (HR-only outcome/escalation, BR-1), never from dates.
 */
export interface IPip {
  pipId: string;
  employeeId: string;
  employeeName: string;
  jobTitle?: string | null;
  managerName?: string | null;
  status: PipStatus;
  /** Encrypted at rest server-side (NFR-4); returned decrypted to authorised callers. */
  reason: string;
  startDate: string;
  endDate: string;
  /** Assigned mentor/coach display name; null if none. */
  mentorName: string | null;
  objectives: IPipObjective[];
  /** The escalation action configured at creation (offered if outcome is NotMet). */
  escalationAction: EscalationAction;
  /** The recorded escalation once confirmed (AC-5); null otherwise. */
  escalation: IPipEscalation | null;
  /** BR-4 employee acknowledgement state + signature. */
  acknowledgement: PipAcknowledgement;
  acknowledgedSignature: IPipSignature | null;
  /** The final outcome once set (AC-4); null while in progress. */
  outcome: PipOutcome | null;
}

/** Pre-fill payload for the creation form (AC-1). */
export interface IPipDraft {
  /** Pre-filled when arriving from a flagged review; null for a blank HR-initiated form. */
  employeeId: string | null;
  employeeName: string | null;
  jobTitle?: string | null;
  managerName?: string | null;
  /** Suggested reason if coming from a flagged manager review (US-PRF-003 PIP flag). */
  suggestedReason: string | null;
  /** Whether this employee already has an active PIP (BR-2: blocks creation). */
  hasActivePip: boolean;
  /** The escalation actions this tenant has configured (BR-6). */
  escalationOptions: EscalationAction[];
}

/** Create-PIP body (AC-2). */
export interface ICreatePipRequest {
  employeeId: string;
  reason: string;
  startDate: string;
  endDate: string;
  mentorId: string | null;
  escalationAction: EscalationAction;
  objectives: IPipObjectiveInput[];
  /** Extra checkpoint dates beyond the per-objective due dates (§7). */
  checkpointDates: string[];
}

/** An objective in the create/extend payload. */
export interface IPipObjectiveInput {
  title: string;
  description: string;
  successCriteria: string;
  dueDate: string;
}

/** Record-checkpoint body (AC-3). The file (if any) is sent multipart, field `file`. */
export interface IRecordCheckpointRequest {
  status: CheckpointStatus;
  notes: string;
}

/** Set-outcome body (AC-4). */
export interface ISetOutcomeRequest {
  outcome: PipOutcome;
  /** Required when outcome is Extended (FR-6). */
  newEndDate?: string;
  /** Optional additional objectives when extending (FR-6). */
  newObjectives?: IPipObjectiveInput[];
}

/** Confirm-escalation body (AC-5). */
export interface IEscalateRequest {
  action: EscalationAction;
  note?: string;
}

// ─── Validation constants (mirror the backend) ────────────────

/** BR-3: PIP duration must be at least 30 days. */
export const PIP_MIN_DURATION_DAYS = 30;

/** AC-1/FR-1: duration presets (days) + a custom option. */
export const PIP_DURATION_PRESETS = [30, 60, 90] as const;

/** §8 confidentiality banner copy (QA may assert verbatim). */
export const PIP_CONFIDENTIAL_NOTICE =
  'This Performance Improvement Plan is confidential. It is visible only to the employee, their manager, HR, and the assigned mentor.';

/** BR-4 acknowledgement confirmation copy (QA may assert verbatim). */
export const PIP_ACKNOWLEDGE_MESSAGE =
  'By acknowledging, you confirm you have received and read this Performance Improvement Plan.';

// ─── Pure helpers (testable without a component) ──────────────

/** Whole-day inclusive duration between two ISO dates; 0 if either is missing/invalid. */
export function pipDurationDays(
  startDate: string | null | undefined,
  endDate: string | null | undefined,
): number {
  if (!startDate || !endDate) {
    return 0;
  }
  const start = Date.parse(startDate);
  const end = Date.parse(endDate);
  if (Number.isNaN(start) || Number.isNaN(end) || end < start) {
    return 0;
  }
  return Math.round((end - start) / 86_400_000);
}

/** BR-3: the duration must be ≥ PIP_MIN_DURATION_DAYS. */
export function pipDurationValid(
  startDate: string | null | undefined,
  endDate: string | null | undefined,
): boolean {
  return pipDurationDays(startDate, endDate) >= PIP_MIN_DURATION_DAYS;
}

/** Add `days` whole days to an ISO date, returning a `yyyy-MM-dd` string (for presets). */
export function addDaysIso(startDate: string, days: number): string {
  const start = Date.parse(startDate);
  if (Number.isNaN(start)) {
    return '';
  }
  return new Date(start + days * 86_400_000).toISOString().slice(0, 10);
}

/**
 * BR-1: only HR (Performance.Review.All) may create/extend/close/escalate a PIP.
 * Managers can record checkpoints but never close/extend. The FE derives this from
 * the caller's roles; the backend re-enforces it.
 */
export function isPipHrRole(roles: readonly string[]): boolean {
  return (
    roles.includes('HR Officer') ||
    roles.includes('HR Manager') ||
    roles.includes('Tenant Admin')
  );
}

/** AC-4: HR may set the final outcome only once the PIP is Active or Extended. */
export function canSetOutcome(status: PipStatus, isHr: boolean): boolean {
  return isHr && (status === 'Active' || status === 'Extended');
}

/** AC-5: escalation may be confirmed only once the outcome flipped the PIP to NotMet. */
export function canEscalate(pip: IPip, isHr: boolean): boolean {
  return isHr && pip.status === 'NotMet' && pip.escalation == null;
}

/**
 * AC-3: a checkpoint may be recorded (by manager OR HR) while the PIP is in progress
 * and the checkpoint has not yet been recorded.
 */
export function canRecordCheckpoint(
  pip: IPip,
  checkpoint: IPipCheckpoint,
): boolean {
  return (
    (pip.status === 'Active' || pip.status === 'Extended') &&
    checkpoint.recordedOn == null
  );
}

/** A terminal/closed PIP status (read-only). */
export function pipIsClosed(status: PipStatus): boolean {
  return (
    status === 'SuccessfullyCompleted' ||
    status === 'NotMet' ||
    status === 'Cancelled'
  );
}

/**
 * §8 timeline progress: fraction (0..1) of the PIP duration elapsed as of `now`.
 * Pure + clamped so the timeline bar always renders sensibly. Used to place the
 * "today" marker on the duration bar.
 */
export function pipTimelineProgress(
  pip: Pick<IPip, 'startDate' | 'endDate'>,
  now: Date = new Date(),
): number {
  const start = Date.parse(pip.startDate);
  const end = Date.parse(pip.endDate);
  if (Number.isNaN(start) || Number.isNaN(end) || end <= start) {
    return 0;
  }
  const ratio = (now.getTime() - start) / (end - start);
  return Math.min(1, Math.max(0, ratio));
}

/**
 * §8 checkpoint marker position: fraction (0..1) of a checkpoint's due date along the
 * PIP duration, for placing markers on the timeline bar. Clamped to [0,1].
 */
export function checkpointMarkerPosition(
  pip: Pick<IPip, 'startDate' | 'endDate'>,
  checkpoint: Pick<IPipCheckpoint, 'dueDate'>,
): number {
  const start = Date.parse(pip.startDate);
  const end = Date.parse(pip.endDate);
  const due = Date.parse(checkpoint.dueDate);
  if (
    Number.isNaN(start) ||
    Number.isNaN(end) ||
    Number.isNaN(due) ||
    end <= start
  ) {
    return 0;
  }
  return Math.min(1, Math.max(0, (due - start) / (end - start)));
}
