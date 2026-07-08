/**
 * US-PRF-004: HR-facing appraisal-cycle management models matching the backend
 * API contract. This is the HR Officer creating/managing appraisal cycles
 * (phases, timelines, participants, status transitions) — the cycle layer that the
 * goal-setting (US-PRF-001), self-assessment (US-PRF-002), and manager-review
 * (US-PRF-003) stories all hang off.
 *
 * The service layer (`CycleService`) is intentionally thin so a route/DTO mismatch
 * is a one-file fix once the backend lands. This is also the reconciliation point
 * for the US-PRF-001 path mismatch (see docs/vault/modules/performance.md).
 *
 * ── Backend contract (VERIFIED against CycleDtos.cs, BUG-257) ──────────────────
 * The save-request field names below are aligned to the authoritative
 * `CreateCycleInput` / `UpdateCycleInput` / `CyclePhaseInput` / `ParticipantScopeInput`:
 * `selfWeightPercent`, `is360Enabled`, `isCalibrationEnabled`, `phases[].phaseType`,
 * `scope.scopeType`. The backend derives `managerWeightPercent` (100 − self), so the
 * FE never sends it. The `Grades` participant scope resolves to an EMPTY set on the
 * backend (unimplemented Core-HR grade seam), so it is disabled in the UI and the FE
 * sends no grade ids.
 *
 * `apiBaseUrl` already includes `/api/v1`. All under `/performance/cycles`:
 *
 *   GET    /performance/cycles                       → ICycleSummary[] (list, FR-7)
 *   GET    /performance/cycles/{id}                  → ICycle (full detail incl. phases + scope)
 *   POST   /performance/cycles                       body ISaveCycleRequest → ICycle (AC-1/AC-2)
 *   PUT    /performance/cycles/{id}                  body ISaveCycleRequest → ICycle (AC-5 edit/extend)
 *   GET    /performance/cycles/{id}/dashboard        → ICycleDashboard (AC-3 stats + overdue)
 *   POST   /performance/cycles/{id}/transition       body { action, reason? } → ICycle (status actions, FR-7)
 *   POST   /performance/cycles/{id}/clone            body { name, startDate, endDate } → ICycle (FR-8)
 *
 * NOTE: All requests use withCredentials (httpOnly cookie auth) and are tenant-scoped
 * via the tenantInterceptor (X-Tenant-Subdomain header). The backend resolves the
 * tenant from the session and enforces `Performance.SetGoal.All` / `Performance.Publish.All`
 * + RLS (BR-1 / NFR-2) — the FE sends no tenant id. The global ApiResponse unwrap
 * interceptor (US-PLT-001) strips the `{ data }` envelope, so services consume BARE
 * payloads (do NOT double-unwrap). Enums are PascalCase STRINGS (US-PLT-003), never
 * integers — matching the C# member names via the global JsonStringEnumConverter.
 */

// ─── Enums ────────────────────────────────────────────────────

/** Lifecycle status of an appraisal cycle (FR-7, §8). Matches C# `CycleStatus`. */
export type CycleStatus =
  | 'Draft'
  | 'Active'
  | 'Paused'
  | 'Completed'
  | 'Cancelled';

export const CYCLE_STATUS_OPTIONS: CycleStatus[] = [
  'Draft',
  'Active',
  'Paused',
  'Completed',
  'Cancelled',
];

/** §8 status-badge colors: Draft (gray), Active (green), Paused (amber), Completed (blue), Cancelled (red). */
export const CYCLE_STATUS_BADGE: Record<CycleStatus, string> = {
  Draft: 'bg-neutral-100 text-neutral-600 ring-neutral-500/20',
  Active: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  Paused: 'bg-amber-50 text-amber-700 ring-amber-600/20',
  Completed: 'bg-sky-50 text-sky-700 ring-sky-600/20',
  Cancelled: 'bg-rose-50 text-rose-700 ring-rose-600/20',
};

export const CYCLE_STATUS_LABEL: Record<CycleStatus, string> = {
  Draft: 'Draft',
  Active: 'Active',
  Paused: 'Paused',
  Completed: 'Completed',
  Cancelled: 'Cancelled',
};

/** Cycle type (§7). Matches C# `CycleType`. */
export type CycleType = 'Annual' | 'Quarterly' | 'Probation';

export const CYCLE_TYPE_OPTIONS: { value: CycleType; label: string }[] = [
  { value: 'Annual', label: 'Annual review' },
  { value: 'Quarterly', label: 'Quarterly KPI check-in' },
  { value: 'Probation', label: 'Probation review' },
];

/**
 * The fixed, ordered set of phases (§8/AC-1). The ordinal here is the canonical
 * sequence used by the sequencing validation (FR-2). `Calibration` and `Publish`
 * are optional in the sense that calibration can be toggled off (FR-6), but when a
 * phase is present its dates must respect this order.
 */
export type CyclePhaseKind =
  | 'GoalSetting'
  | 'SelfAssessment'
  | 'ManagerReview'
  | 'Calibration'
  | 'Publish';

/** Canonical phase order (FR-2 sequencing). */
export const PHASE_ORDER: CyclePhaseKind[] = [
  'GoalSetting',
  'SelfAssessment',
  'ManagerReview',
  'Calibration',
  'Publish',
];

export const PHASE_LABEL: Record<CyclePhaseKind, string> = {
  GoalSetting: 'Goal setting',
  SelfAssessment: 'Self-assessment',
  ManagerReview: 'Manager review',
  Calibration: 'Calibration',
  Publish: 'Publish',
};

/** Participant scoping mode (FR-3). Matches C# `ParticipantScopeType`. */
export type ParticipantScopeType =
  | 'AllEmployees'
  | 'Departments'
  | 'Grades'
  | 'CustomList';

export const PARTICIPANT_SCOPE_OPTIONS: {
  value: ParticipantScopeType;
  label: string;
  /** BUG-257: an unavailable option is greyed out and cannot be selected. */
  disabled?: boolean;
  /** Short reason shown alongside a disabled option. */
  hint?: string;
}[] = [
  { value: 'AllEmployees', label: 'All active employees' },
  { value: 'Departments', label: 'Specific departments' },
  {
    value: 'Grades',
    label: 'Specific grades / bands',
    // BUG-257: the backend Grades scope resolves to zero participants (the
    // Core-HR grade/band field is not implemented yet), so selecting it would
    // silently create an empty cycle. Disabled until that seam lands.
    disabled: true,
    hint: 'Not available yet — grade / band data is not configured.',
  },
  { value: 'CustomList', label: 'Custom employee list' },
];

// ─── DTOs ─────────────────────────────────────────────────────

/** One row on the Cycles list (FR-7). Lightweight — no phases. */
export interface ICycleSummary {
  id: string;
  name: string;
  type: CycleType;
  status: CycleStatus;
  /** ISO date (yyyy-MM-dd). */
  startDate: string;
  /** ISO date (yyyy-MM-dd). */
  endDate: string;
  participantCount: number;
}

/** One phase within a cycle (FR-2). Matches C# `CyclePhaseInput.PhaseType`. */
export interface ICyclePhase {
  phaseType: CyclePhaseKind;
  /** ISO date (yyyy-MM-dd). */
  startDate: string;
  /** ISO date (yyyy-MM-dd). */
  endDate: string;
}

/**
 * Participant scope (FR-3). The id lists are only meaningful for their scope type.
 * Matches C# `ParticipantScopeInput` (ScopeType + DepartmentIds + EmployeeIds).
 */
export interface IParticipantScope {
  scopeType: ParticipantScopeType;
  /** Department ids (Departments scope); resolved server-side to participants. */
  departmentIds: string[];
  /** Explicit employee ids (CustomList scope). */
  employeeIds: string[];
}

/** Full cycle detail (GET /cycles/{id}). */
export interface ICycle {
  id: string;
  name: string;
  type: CycleType;
  status: CycleStatus;
  /** ISO date (yyyy-MM-dd). */
  startDate: string;
  /** ISO date (yyyy-MM-dd). */
  endDate: string;
  phases: ICyclePhase[];
  /**
   * BUG-259: the backend read DTO (`CycleDto.ParticipantScope`) returns only the FLAT
   * scope enum — there are NO department/employee id lists on the read side yet. The
   * nested `{ scopeType, departmentIds, employeeIds }` shape is the WRITE contract
   * (see `ISaveCycleRequest.scope`), not this one. Prefilling id lists in edit mode is
   * deferred to a backend enrichment (BUG-260).
   */
  participantScope: ParticipantScopeType;
  /** Rating-scale maximum (integer 2-10, default 5); locked once Active (BR-5). */
  ratingScaleMax: number;
  /** Self-rating weight 0-100; managerWeightPercent is derived server-side (FR-6). */
  selfWeightPercent: number;
  /** 360-degree peer feedback toggle (FR-6). */
  is360Enabled: boolean;
  /** Calibration phase toggle (FR-6). */
  isCalibrationEnabled: boolean;
  participantCount: number;
  /** ISO timestamp; null while Draft. */
  cancelledReason?: string | null;
}

/** Create/update request body (AC-1 / AC-5). */
export interface ISaveCycleRequest {
  name: string;
  type: CycleType;
  startDate: string;
  endDate: string;
  phases: ICyclePhase[];
  scope: IParticipantScope;
  /** Rating-scale maximum (integer 2-10, default 5). Maps to backend RatingScaleMax. */
  ratingScaleMax: number;
  /** Self-rating weight 0-100; the backend derives managerWeightPercent (100 − self). */
  selfWeightPercent: number;
  is360Enabled: boolean;
  isCalibrationEnabled: boolean;
}

/** Per-phase completion stats for the dashboard (AC-3). Matches C# `PhaseCompletionDto`. */
export interface IPhaseStat {
  phaseType: CyclePhaseKind;
  /** ISO date. */
  startDate: string;
  /** ISO date. */
  endDate: string;
  /** Number of participants who completed this phase. */
  completedCount: number;
  /** Total participants expected to complete this phase. */
  totalParticipants: number;
  /** Participants past the phase end date who have not completed it. */
  overdueCount: number;
}

/** Cycle dashboard payload (AC-3). One call drives the whole dashboard. Matches C# `CycleDashboardDto`. */
export interface ICycleDashboard {
  cycleId: string;
  name: string;
  status: CycleStatus;
  participantCount: number;
  phases: IPhaseStat[];
}

/** Status-transition action (FR-7). Matches C# `CycleTransitionAction`. */
export type CycleTransitionAction =
  | 'Activate'
  | 'Pause'
  | 'Resume'
  | 'Complete'
  | 'Cancel';

/** Transition request body. `reason` is required for Cancel (BR-6). */
export interface ICycleTransitionRequest {
  action: CycleTransitionAction;
  reason?: string;
}

/** Clone request body (FR-8). New name + a fresh window; phases re-shift server-side. */
export interface ICloneCycleRequest {
  name: string;
  startDate: string;
  endDate: string;
}

// ─── Validation constants (mirror the backend) ────────────────

export const CYCLE_NAME_MAX = 150;
export const CANCEL_REASON_MIN = 5;
export const CANCEL_REASON_MAX = 500;
/** Rating-scale maximum bounds (mirror backend CycleValidators InclusiveBetween(2,10)). */
export const RATING_SCALE_MIN = 2;
export const RATING_SCALE_MAX = 10;
export const RATING_SCALE_DEFAULT = 5;

// ─── Pure helpers (testable without a component) ──────────────

/** A phase shape used by the sequencing validator (phaseType + date strings). */
export interface IPhaseDates {
  phaseType: CyclePhaseKind;
  startDate: string;
  endDate: string;
}

/** Parse an ISO yyyy-MM-dd date to a comparable epoch; NaN if blank/invalid. */
function toEpoch(iso: string | null | undefined): number {
  if (!iso) {
    return NaN;
  }
  const t = Date.parse(iso);
  return Number.isNaN(t) ? NaN : t;
}

/**
 * FR-2 / BR-3: validate the phase set. Returns an array of human-readable error
 * strings (empty = valid). Pure + exported so QA can assert each rule directly:
 *  - each phase has both dates and start ≤ end (no reversed ranges);
 *  - every phase falls within the cycle window [cycleStart, cycleEnd] (BR-3);
 *  - phases are in canonical order and non-overlapping (FR-2): each phase must
 *    start strictly after the previous phase's end.
 *
 * Phases are first sorted into PHASE_ORDER so the sequencing message reflects the
 * intended pipeline regardless of input order.
 */
export function validatePhaseSequencing(
  phases: IPhaseDates[],
  cycleStart: string,
  cycleEnd: string,
): string[] {
  const errors: string[] = [];
  const cs = toEpoch(cycleStart);
  const ce = toEpoch(cycleEnd);

  if (Number.isNaN(cs) || Number.isNaN(ce)) {
    errors.push('Set the cycle start and end dates first.');
    return errors;
  }
  if (cs > ce) {
    errors.push('Cycle end date must be on or after the start date.');
  }

  const ordered = [...phases].sort(
    (a, b) => PHASE_ORDER.indexOf(a.phaseType) - PHASE_ORDER.indexOf(b.phaseType),
  );

  let prevEnd = NaN;
  let prevLabel = '';
  for (const p of ordered) {
    const label = PHASE_LABEL[p.phaseType];
    const s = toEpoch(p.startDate);
    const e = toEpoch(p.endDate);

    if (Number.isNaN(s) || Number.isNaN(e)) {
      errors.push(`${label}: both start and end dates are required.`);
      continue;
    }
    if (s > e) {
      errors.push(`${label}: end date must be on or after the start date.`);
    }
    if (s < cs || e > ce) {
      errors.push(`${label}: dates must be within the cycle window.`);
    }
    if (!Number.isNaN(prevEnd) && s <= prevEnd) {
      errors.push(
        `${label}: must start after ${prevLabel} ends (phases cannot overlap).`,
      );
    }
    prevEnd = e;
    prevLabel = label;
  }

  return errors;
}

/** True when the phase set passes all sequencing rules (FR-2 / BR-3). */
export function phasesAreValid(
  phases: IPhaseDates[],
  cycleStart: string,
  cycleEnd: string,
): boolean {
  return validatePhaseSequencing(phases, cycleStart, cycleEnd).length === 0;
}

/**
 * FR-7 status-machine: which transition actions are valid from a given status.
 * Pure + exported so the toolbar and QA share one source of truth.
 *  - Draft     → Activate, Cancel
 *  - Active    → Pause, Complete, Cancel
 *  - Paused    → Resume, Complete, Cancel
 *  - Completed → (terminal)
 *  - Cancelled → (terminal)
 */
export function allowedTransitions(
  status: CycleStatus,
): CycleTransitionAction[] {
  switch (status) {
    case 'Draft':
      return ['Activate', 'Cancel'];
    case 'Active':
      return ['Pause', 'Complete', 'Cancel'];
    case 'Paused':
      return ['Resume', 'Complete', 'Cancel'];
    default:
      return [];
  }
}

export const TRANSITION_LABEL: Record<CycleTransitionAction, string> = {
  Activate: 'Activate',
  Pause: 'Pause',
  Resume: 'Resume',
  Complete: 'Complete',
  Cancel: 'Cancel',
};

/**
 * AC-3: completion percentage for a phase stat (0-100, rounded). Returns 0 when
 * there are no participants. Pure so the dashboard + QA share it.
 */
export function phaseCompletionPercent(stat: {
  completedCount: number;
  totalParticipants: number;
}): number {
  if (!stat.totalParticipants || stat.totalParticipants <= 0) {
    return 0;
  }
  const pct = (stat.completedCount / stat.totalParticipants) * 100;
  return Math.min(100, Math.max(0, Math.round(pct)));
}

/** Tailwind progress-fill color keyed off completion percent (§8 visual). */
export function progressFillClass(percent: number): string {
  if (percent >= 100) {
    return 'bg-emerald-500';
  }
  if (percent >= 50) {
    return 'bg-sky-500';
  }
  if (percent > 0) {
    return 'bg-amber-500';
  }
  return 'bg-neutral-300';
}
