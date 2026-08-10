/**
 * US-ONB-005: Offboarding / Exit Checklist & Clearance models.
 *
 * Mirrors the PINNED backend contract under `/api/v1/offboarding` exactly
 * (paths/verbs/field names are fixed). Kept in ONE place so the service mapping,
 * the HR initiate form, and the clearance dashboard share a single source of
 * truth. Asset condition grades are reused from the US-ONB-004 asset register so
 * the return-asset selector matches the issuance vocabulary.
 */

import { AssetCondition } from './onboarding-asset.models';

// Note: AssetCondition / ASSET_CONDITIONS are NOT re-exported here — they live in
// onboarding-asset.models and the barrel (index.ts) already exposes them. Consumers
// of the offboarding return-asset selector import them from onboarding-asset.models
// directly to avoid a duplicate barrel export.

/** Reason an employee is leaving (Data Requirements §7 / BR-1). */
export type OffboardingReason =
  | 'Resignation'
  | 'Termination'
  | 'Contract End'
  | 'Retirement';

/** Ordered reason options for the initiate dropdown. */
export const OFFBOARDING_REASONS: readonly OffboardingReason[] = [
  'Resignation',
  'Termination',
  'Contract End',
  'Retirement',
] as const;

/** Per-task clearance verdict a department head records (Data Requirements §7). */
export type ClearanceStatus = 'cleared' | 'issues' | 'pending';

/** Lifecycle status of a single exit task. */
export type TaskStatus = 'pending' | 'in_progress' | 'completed';

/** Overall lifecycle status of the offboarding instance. */
export type OffboardingStatus = 'in_progress' | 'completed';

/** The value POSTed for a per-task clearance decision (AC-3). */
export type ClearanceDecision = 'approved' | 'pending_issues';

/**
 * One exit task within a department lane (AC-1/AC-3). `linkedAssetId` is set on
 * the IT "Asset Return" task that maps to a register entry (AC-2). `clearanceStatus`
 * drives the card chip + the department traffic light.
 */
export interface IOffboardingTask {
  id: string;
  title: string;
  responsibleRole: string;
  /** ISO date (yyyy-MM-dd) computed as LWD - offset (FR-2). */
  dueDate: string;
  status: TaskStatus;
  isMandatory: boolean;
  clearanceStatus: ClearanceStatus;
  remarks?: string | null;
  linkedAssetId?: string | null;
}

/** A department clearance lane = one Kanban column / accordion section (FR-4). */
export interface IClearanceDepartment {
  department: string;
  clearanceStatus: ClearanceStatus;
  tasks: IOffboardingTask[];
}

/** The full offboarding instance returned by GET endpoints (Output §7). */
export interface IOffboardingInstance {
  id: string;
  employeeId: string;
  employeeName?: string | null;
  lastWorkingDay: string;
  reason: OffboardingReason;
  status: OffboardingStatus;
  /** Overall clearance — only 'cleared' when every department is cleared (AC-3). */
  overallClearance: ClearanceStatus;
  /** 0..100 completion of mandatory + optional tasks. */
  progressPercent: number;
  departments: IClearanceDepartment[];
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

/** Shape of the 409 body when mandatory items block completion (AC-5). */
export interface ICompleteBlockedError {
  pending: string[];
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

/** Tailwind chip class for a task/department clearance status (UI/UX §8). */
export function clearanceChipClass(status: ClearanceStatus): string {
  switch (status) {
    case 'cleared':
      return 'chip-cleared';
    case 'issues':
      return 'chip-issues';
    case 'pending':
      return 'chip-pending';
  }
}

/** Traffic-light color token for a department status (FR-4: green/yellow/red). */
export function trafficLightClass(status: ClearanceStatus): string {
  switch (status) {
    case 'cleared':
      return 'light-green';
    case 'issues':
      return 'light-yellow';
    case 'pending':
      return 'light-red';
  }
}

/** Human label for a clearance status chip. */
export function clearanceLabel(status: ClearanceStatus): string {
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
 * The titles of all incomplete MANDATORY tasks across every department — drives
 * the disabled "Complete Offboarding" button's tooltip and the local guard
 * (BR-2 / AC-5). A task is outstanding when it is mandatory and not cleared.
 * Pure helper over the instance so it can be unit-tested without the component.
 */
export function pendingMandatoryTitles(
  instance: IOffboardingInstance | null,
): string[] {
  if (!instance) return [];
  return instance.departments
    .flatMap((d) => d.tasks)
    .filter((t) => t.isMandatory && t.clearanceStatus !== 'cleared')
    .map((t) => t.title);
}

/** Whether the instance can be completed locally (no pending mandatory tasks). */
export function canComplete(instance: IOffboardingInstance | null): boolean {
  return !!instance && instance.status !== 'completed' && pendingMandatoryTitles(instance).length === 0;
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
