/**
 * US-ONB-005: Offboarding / Exit Checklist & Clearance models.
 *
 * The read payloads are the GENERATED contract types (`Schema<'Onboarding…Dto'>`) with explicit mappers
 * into the view-models the templates render. A renamed C# property is now a TypeScript compile error in
 * the mapper rather than a silent `undefined` on screen.
 *
 * ── What this file used to get wrong, and why it matters ─────────────────────────────────────────────
 *
 * **One union was doing the work of two vocabularies.** `ClearanceStatus` was declared as
 * `'cleared' | 'issues' | 'pending'` — the *department* traffic light — and then also used as the type of
 * a *task's* `clearanceStatus`, which the API only ever populates with `"approved"`, `"pending_issues"`,
 * or null. Because the two vocabularies share no member, every comparison of a task's clearance against a
 * department token was dead code that TypeScript happily accepted. The completion gate was exactly that
 * comparison (`t.clearanceStatus !== 'cleared'`), so **every mandatory task counted as blocking forever**
 * and the "Complete Offboarding" button could never enable. They are now two named types.
 *
 * **The reason dropdown could not be submitted.** `OffboardingReason` carried the display string
 * `'Contract End'` and sent it as the wire value; the API parses reasons with `Enum.TryParse` after
 * stripping underscores only, so the space made `"Contract End"` fail and the request came back
 * **400 `invalid_reason`**. Display text and wire token are now separate things — the token comes from the
 * contract enum, the label from {@link OFFBOARDING_REASON_LABEL}.
 *
 * **The completion rule is no longer predicted here at all.** The backend projects the answer
 * (`canComplete` + `pendingMandatoryItems`) from the same code its completion endpoint enforces with, so
 * the client renders it instead of re-deriving it. Re-deriving is what produced the bug above.
 */

import type { Schema } from '@core/api';
import { AssetCondition } from './onboarding-asset.models';

// Note: AssetCondition / ASSET_CONDITIONS are NOT re-exported here — they live in
// onboarding-asset.models and the barrel (index.ts) already exposes them. Consumers
// of the offboarding return-asset selector import them from onboarding-asset.models
// directly to avoid a duplicate barrel export.

// ─── Wire vocabularies (mirror the contract enums exactly) ────────────────────

/**
 * Reason an employee is leaving — the **wire token**, not display text (BR-1 / Data Requirements §7).
 * `ContractEnd` is one word on purpose: the API's `TryParseReason` strips underscores but not spaces, so
 * the old `'Contract End'` was rejected with a 400. Render {@link OFFBOARDING_REASON_LABEL} instead.
 */
export type OffboardingReason =
  | 'Resignation'
  | 'Termination'
  | 'ContractEnd'
  | 'Retirement';

/** Display text for a reason. Separate from the token so the two can never be confused again. */
export const OFFBOARDING_REASON_LABEL: Readonly<Record<OffboardingReason, string>> = {
  Resignation: 'Resignation',
  Termination: 'Termination',
  ContractEnd: 'Contract end',
  Retirement: 'Retirement',
} as const;

/** Ordered reason options for the initiate dropdown. */
export const OFFBOARDING_REASONS: readonly OffboardingReason[] = [
  'Resignation',
  'Termination',
  'ContractEnd',
  'Retirement',
] as const;

/**
 * The **department** traffic light (AC-3 / FR-4) — computed server-side across a department's tasks.
 * Distinct from {@link TaskClearanceStatus}; conflating the two is what broke the completion gate.
 */
export type DepartmentClearanceStatus = 'cleared' | 'issues' | 'pending';

/**
 * The **task**-level clearance verdict a department head records (AC-3), or `null` while undecided.
 * Note that not every task is a clearance — a plain mandatory task stays `null` for its whole life and
 * that is not a blocker.
 */
export type TaskClearanceStatus = 'approved' | 'pending_issues';

/**
 * The value POSTed for a per-task clearance decision (AC-3). Deliberately an alias rather than a second
 * literal union: the value you send and the value you read back are the same vocabulary, and writing it
 * twice is how they drift.
 */
export type ClearanceDecision = TaskClearanceStatus;

/**
 * Why a mandatory item blocks completion (AC-5). The backend owns this vocabulary
 * (`OffboardingCompletionGate.ReasonNotCompleted` / `ReasonClearanceNotApproved`).
 */
export const PENDING_BLOCK_REASONS = ['not_completed', 'clearance_not_approved'] as const;
export type PendingBlockReason = (typeof PENDING_BLOCK_REASONS)[number];

/** Lifecycle status of a single exit task (wire tokens — `Skipped` exists and is not a completion). */
export type TaskStatus = 'Pending' | 'InProgress' | 'Completed' | 'Skipped';

/** Overall lifecycle status of the offboarding instance (wire tokens). */
export type OffboardingStatus = 'InProgress' | 'Completed';

// ─── View-models ─────────────────────────────────────────────────────────────

/**
 * One exit task within a department lane (AC-1/AC-3). `linkedAssetId` is set on
 * the IT "Asset Return" task that maps to a register entry (AC-2). `clearanceStatus`
 * drives the card chip; it is `null` until a decision is recorded.
 */
export interface IOffboardingTask {
  id: string;
  title: string;
  responsibleRole: string;
  /** ISO date (yyyy-MM-dd) computed as LWD - offset (FR-2). */
  dueDate: string;
  status: TaskStatus;
  isMandatory: boolean;
  clearanceStatus: TaskClearanceStatus | null;
  remarks?: string | null;
  linkedAssetId?: string | null;
}

/** A department clearance lane = one Kanban column / accordion section (FR-4). */
export interface IClearanceDepartment {
  department: string;
  clearanceStatus: DepartmentClearanceStatus;
  tasks: IOffboardingTask[];
}

/** One mandatory item standing between this offboarding and completion (AC-5). */
export interface IPendingMandatoryItem {
  taskId: string;
  title: string;
  department: string;
  /** Why it blocks — `null` when the API sends a reason this build does not know. */
  reason: PendingBlockReason | null;
}

/** The full offboarding instance returned by GET endpoints (Output §7). */
export interface IOffboardingInstance {
  id: string;
  employeeId: string;
  employeeName?: string | null;
  lastWorkingDay: string;
  /** `null` when the API sends a reason this build does not know — see {@link toOffboardingReason}. */
  reason: OffboardingReason | null;
  status: OffboardingStatus;
  /** Overall clearance — only 'cleared' when every department is cleared (AC-3). */
  overallClearance: DepartmentClearanceStatus;
  /** 0..100 completion of mandatory + optional tasks. */
  progressPercent: number;
  departments: IClearanceDepartment[];
  /**
   * AC-5: what blocks completion right now, **as the server computed it**. The client does not derive
   * this; see the file header for what happened when it did.
   */
  pendingMandatory: IPendingMandatoryItem[];
  /** AC-5: whether completing would succeed right now — the server's answer, not a local prediction. */
  canComplete: boolean;
}

/** Body of POST /offboarding/initiate (Data Requirements §7 input fields). */
export interface IInitiateOffboardingRequest {
  employeeId: string;
  /** ISO date (yyyy-MM-dd); must be today or in the future. */
  lastWorkingDay: string;
  offboardingTemplateId?: string | null;
  reason: OffboardingReason;
  notes?: string | null;
}

/** Body of POST /offboarding/tasks/{taskId}/clearance (Clearance input §7). */
export interface IRecordClearanceRequest {
  status: ClearanceDecision;
  remarks?: string | null;
}

/** Body of POST /offboarding/tasks/{taskId}/return-asset (AC-2 / UI/UX §8). */
export interface IReturnAssetRequest {
  assetId: string;
  condition: AssetCondition;
  /** When true the asset is marked 'disposed' rather than 'available' (AC-2). */
  disposed?: boolean;
}

// ─── Wire contract → view-model mappers ──────────────────────────────────────

export type OffboardingInstanceWire = Schema<'OnboardingOffboardingInstanceDto'>;
export type OffboardingTaskWire = Schema<'OnboardingOffboardingTaskDto'>;
export type DepartmentClearanceWire = Schema<'OnboardingDepartmentClearanceDto'>;
export type PendingMandatoryItemWire = Schema<'OnboardingPendingMandatoryItemDto'>;
export type CompleteOffboardingResultWire =
  Schema<'OnboardingCompleteOffboardingResultDto'>;

/**
 * Narrows the wire's untyped `clearanceStatus` string to the task vocabulary.
 *
 * The contract types this field as `string | null` because the C# DTO does, so the generated type cannot
 * narrow it for us. Narrowing here — rather than casting — means an unrecognised token becomes `null`
 * (undecided) instead of a value the exhaustive `switch`es below would silently fall through.
 */
export function toTaskClearanceStatus(
  value: string | null | undefined,
): TaskClearanceStatus | null {
  return value === 'approved' || value === 'pending_issues' ? value : null;
}

/**
 * Narrows the instance lifecycle status.
 *
 * Unknown tokens fall back to `InProgress` deliberately: `Completed` gates BR-6 (completion is
 * irreversible) and switches the primary button's label, so guessing "finished" from a token this build has
 * never seen is the dangerous direction. An unrecognised status leaves the offboarding actionable instead.
 */
export function toOffboardingStatus(
  value: string | null | undefined,
): OffboardingStatus {
  return value === 'Completed' ? 'Completed' : 'InProgress';
}

/**
 * Narrows the leaving reason, or `null` when the API sends one this build does not know. Null rather than a
 * guessed default: the reason is shown to HR on a termination record, and displaying the wrong one is worse
 * than displaying none.
 */
export function toOffboardingReason(
  value: string | null | undefined,
): OffboardingReason | null {
  return value != null && (OFFBOARDING_REASONS as readonly string[]).includes(value)
    ? (value as OffboardingReason)
    : null;
}

/** Narrows the blocking reason, or `null` when unrecognised. */
export function toPendingBlockReason(
  value: string | null | undefined,
): PendingBlockReason | null {
  return value != null && (PENDING_BLOCK_REASONS as readonly string[]).includes(value)
    ? (value as PendingBlockReason)
    : null;
}

/** Narrows the wire's untyped department `status` string to the traffic-light vocabulary. */
export function toDepartmentClearanceStatus(
  value: string | null | undefined,
): DepartmentClearanceStatus {
  return value === 'cleared' || value === 'issues' ? value : 'pending';
}

/** Narrows a task's lifecycle status; unknown tokens read as `Pending` (not done, not skipped). */
export function toTaskStatus(value: string | null | undefined): TaskStatus {
  return value === 'InProgress' || value === 'Completed' || value === 'Skipped'
    ? value
    : 'Pending';
}

/** Maps a wire task onto the card view-model. */
export function mapOffboardingTask(w: OffboardingTaskWire): IOffboardingTask {
  return {
    id: w.id ?? '',
    title: w.title ?? '',
    responsibleRole: w.responsibleRoleName ?? '',
    dueDate: w.dueDate ?? '',
    status: toTaskStatus(w.statusName ?? w.status),
    isMandatory: w.isMandatory ?? false,
    clearanceStatus: toTaskClearanceStatus(w.clearanceStatus),
    remarks: w.remarks ?? null,
    linkedAssetId: w.linkedAssetId ?? null,
  };
}

/** Maps a wire department lane onto the column view-model (`clearanceCategoryName` → `department`). */
export function mapClearanceDepartment(
  w: DepartmentClearanceWire,
): IClearanceDepartment {
  return {
    department: w.clearanceCategoryName ?? '',
    clearanceStatus: toDepartmentClearanceStatus(w.status),
    tasks: (w.tasks ?? []).map(mapOffboardingTask),
  };
}

/** Maps a wire blocking item (`clearanceCategoryName` → `department`). */
export function mapPendingMandatoryItem(
  w: PendingMandatoryItemWire,
): IPendingMandatoryItem {
  return {
    taskId: w.taskId ?? '',
    title: w.title ?? '',
    department: w.clearanceCategoryName ?? '',
    reason: toPendingBlockReason(w.reason),
  };
}

/**
 * Maps the whole instance payload. `overallClearance` is derived from the wire's
 * `clearanceSummary` — the API sends counts and a `fullyCleared` flag, not a traffic-light token — while
 * `canComplete`/`pendingMandatory` are taken verbatim because they are the server's own verdict.
 */
export function mapOffboardingInstance(
  w: OffboardingInstanceWire,
): IOffboardingInstance {
  const summary = w.clearanceSummary;
  const departments = (w.departments ?? []).map(mapClearanceDepartment);
  return {
    id: w.id ?? '',
    employeeId: w.employeeId ?? '',
    employeeName: w.employeeName ?? null,
    lastWorkingDay: w.lastWorkingDay ?? '',
    reason: toOffboardingReason(w.reasonName ?? w.reason),
    status: toOffboardingStatus(w.statusName ?? w.status),
    overallClearance: summary?.fullyCleared
      ? 'cleared'
      : departments.some((d) => d.clearanceStatus === 'issues')
        ? 'issues'
        : 'pending',
    progressPercent: w.progressPercent ?? 0,
    departments,
    pendingMandatory: (w.pendingMandatoryItems ?? []).map(mapPendingMandatoryItem),
    canComplete: w.canComplete ?? false,
  };
}

// ─── Display + derivation helpers (pure — unit-tested directly) ──────────

/** Max characters for the initiation notes field (Data Requirements §7). */
export const MAX_NOTES_LEN = 2000;

/** Max characters for a clearance remarks field (Clearance input §7). */
export const MAX_REMARKS_LEN = 1000;

/**
 * Today's date as an ISO yyyy-MM-dd string in local time — used as the `min`
 * attribute on the last-working-day input so a past date can't be picked
 * (Data Requirements §7). Pure helper, `today` injected for testability.
 */
export function todayIso(today: Date = new Date()): string {
  const y = today.getFullYear();
  const m = `${today.getMonth() + 1}`.padStart(2, '0');
  const d = `${today.getDate()}`.padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/**
 * Whether an ISO date string (yyyy-MM-dd) is strictly before today (local) —
 * drives the LWD "today or in the future" validator. Pure helper.
 */
export function isPastDate(iso: string, today: Date = new Date()): boolean {
  if (!iso) return false;
  const picked = new Date(`${iso}T00:00:00`);
  const start = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  return picked.getTime() < start.getTime();
}

/** Tailwind chip class for a DEPARTMENT clearance status (UI/UX §8). */
export function clearanceChipClass(status: DepartmentClearanceStatus): string {
  switch (status) {
    case 'cleared':
      return 'chip-cleared';
    case 'issues':
      return 'chip-issues';
    case 'pending':
      return 'chip-pending';
  }
}

/** Traffic-light color token for a DEPARTMENT status (FR-4: green/yellow/red). */
export function trafficLightClass(status: DepartmentClearanceStatus): string {
  switch (status) {
    case 'cleared':
      return 'light-green';
    case 'issues':
      return 'light-yellow';
    case 'pending':
      return 'light-red';
  }
}

/** Human label for a DEPARTMENT clearance status chip. */
export function clearanceLabel(status: DepartmentClearanceStatus): string {
  switch (status) {
    case 'cleared':
      return 'Cleared';
    case 'issues':
      return 'Issues';
    case 'pending':
      return 'Pending';
  }
}

/**
 * Tailwind chip class for a TASK clearance verdict. Separate from
 * {@link clearanceChipClass} because the vocabularies differ — an undecided task
 * (`null`) is not the same thing as a department that is merely "pending".
 */
export function taskClearanceChipClass(
  status: TaskClearanceStatus | null,
): string {
  switch (status) {
    case 'approved':
      return 'chip-cleared';
    case 'pending_issues':
      return 'chip-issues';
    default:
      return 'chip-pending';
  }
}

/** Human label for a TASK clearance verdict. */
export function taskClearanceLabel(status: TaskClearanceStatus | null): string {
  switch (status) {
    case 'approved':
      return 'Approved';
    case 'pending_issues':
      return 'Issues';
    default:
      return 'Awaiting clearance';
  }
}

/**
 * The titles of all mandatory items blocking completion — drives the disabled
 * "Complete Offboarding" button's tooltip (BR-2 / AC-5).
 *
 * This **reads** the server's list rather than re-deriving it from the tasks. The
 * previous local derivation compared a task's clearance against a department-level
 * token, so it matched nothing and reported every mandatory task as blocking.
 */
export function pendingMandatoryTitles(
  instance: IOffboardingInstance | null,
): string[] {
  return (instance?.pendingMandatory ?? []).map((p) => p.title);
}

/** Whether the instance can be completed — the server's verdict (AC-5 / BR-6). */
export function canComplete(instance: IOffboardingInstance | null): boolean {
  return instance?.canComplete ?? false;
}

/** A single asset-return line surfaced in the dashboard's Asset Return section. */
export interface IAssetReturnLine {
  taskId: string;
  title: string;
  assetId: string;
  status: TaskStatus;
}

/**
 * Extract the asset-return tasks (tasks carrying a `linkedAssetId`) across all
 * departments for the dedicated Asset Return section (UI/UX §8). Pure helper.
 */
export function assetReturnLines(
  instance: IOffboardingInstance | null,
): IAssetReturnLine[] {
  if (!instance) return [];
  return instance.departments
    .flatMap((d) => d.tasks)
    .filter((t): t is IOffboardingTask & { linkedAssetId: string } => !!t.linkedAssetId)
    .map((t) => ({
      taskId: t.id,
      title: t.title,
      assetId: t.linkedAssetId,
      status: t.status,
    }));
}
