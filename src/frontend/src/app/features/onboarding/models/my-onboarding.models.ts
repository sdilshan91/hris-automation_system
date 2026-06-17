/**
 * US-ONB-003: New-hire "My Onboarding" checklist + dashboard progress models.
 *
 * Mirrors the PINNED backend contract under `/api/v1/onboarding/checklists/me`
 * exactly (paths/verbs/field names are fixed). Kept in ONE place so the service
 * mapping, the checklist page, and the dashboard widget share a single source of
 * truth. Reuses ResponsibleRole + ChecklistTaskStatus from the existing models —
 * does NOT redefine them.
 */
import { ResponsibleRole } from './onboarding-template.models';
import { ChecklistTaskStatus } from './onboarding-checklist.models';

/**
 * Server-derived status carried per task. The backend may stamp 'overdue' as a
 * distinct lifecycle value (AC-5) on top of the assignment statuses, so the
 * "me" view widens ChecklistTaskStatus with it. The UI never derives 'overdue'
 * from dates by itself when the server already says so (it can additionally flag
 * a still-pending past-due task — see isTaskOverdue).
 */
export type MyTaskStatus = ChecklistTaskStatus | 'overdue';

/** A single task instance as returned by the "me" checklist endpoint (AC-2). */
export interface IMyOnboardingTask {
  taskInstanceId: string;
  title: string;
  description?: string | null;
  category?: string | null;
  responsibleRole?: ResponsibleRole | null;
  /** ISO date (yyyy-MM-dd) server-calculated due date. */
  dueDate: string;
  status: MyTaskStatus;
  isMandatory: boolean;
  /** When true, completion requires a document upload (FR-3 / AC-4). */
  requiresDocument: boolean;
  /** ISO timestamp when completed (BR-3). */
  completedAt?: string | null;
  /** Display name of the actor who completed it. */
  completedBy?: string | null;
  comment?: string | null;
  /** Reference to the stored attachment once uploaded (AC-4). */
  attachmentUrl?: string | null;
}

/** Tasks grouped by category for the collapsible accordions (UI/UX §8). */
export interface IMyOnboardingCategory {
  category: string;
  tasks: IMyOnboardingTask[];
}

/** Progress summary block — shared by the widget and the full checklist (FR-4). */
export interface IOnboardingProgress {
  progressPercent: number;
  totalTasks: number;
  completedTasks: number;
  pendingTasks: number;
  overdueTasks: number;
}

/** Full "me" checklist response (GET /checklists/me). */
export interface IMyChecklist extends IOnboardingProgress {
  checklistInstanceId: string;
  status: string;
  categories: IMyOnboardingCategory[];
}

/** Body for marking a task complete (POST .../complete). */
export interface ICompleteTaskRequest {
  /** Optional note, max 500 chars (Data Requirements §7). */
  comment?: string | null;
  /** Conditionally required when the task requiresDocument (AC-4). */
  file?: File | null;
}

/**
 * Server response to a completion — the updated task plus recalculated progress
 * (Output §7). Lets the UI update the progress ring without a full re-fetch.
 */
export interface ICompleteTaskResponse {
  task: IMyOnboardingTask;
  progress: IOnboardingProgress;
}

// ─── File-upload validation (FR-3 / NFR-6) ──────────────────────

/** Max upload size — 10 MB per file (NFR-6). */
export const MAX_UPLOAD_BYTES = 10 * 1024 * 1024;

/** Accepted MIME types for onboarding document submissions (FR-3). */
export const ACCEPTED_UPLOAD_MIME: readonly string[] = [
  'application/pdf',
  'image/jpeg',
  'image/png',
  'application/msword',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
] as const;

/** `accept` attribute string for the file input. */
export const UPLOAD_ACCEPT_ATTR = '.pdf,.jpg,.jpeg,.png,.doc,.docx';

/**
 * Validate a candidate upload by MIME type + size (client-side guard, FR-3 /
 * NFR-6). Returns a human message on failure, or null when valid. Pure helper —
 * unit-tested directly. Server re-validates + malware-scans (NFR-3); this is UX.
 */
export function validateUploadFile(file: File): string | null {
  if (!ACCEPTED_UPLOAD_MIME.includes(file.type)) {
    return 'Unsupported file type. Upload a PDF, image (JPG/PNG), or Word document.';
  }
  if (file.size > MAX_UPLOAD_BYTES) {
    return 'File is too large. The maximum size is 10 MB.';
  }
  if (file.size === 0) {
    return 'The selected file is empty.';
  }
  return null;
}

/** Human-readable file size (KB/MB) for the selected-file row. Pure helper. */
export function formatUploadSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

// ─── Status / overdue helpers (AC-2 / AC-5) ─────────────────────

/**
 * Whether a task should render with the overdue treatment (red border, warning
 * icon). True when the server already marked it 'overdue', OR when it is still
 * open (not completed/cancelled) and its due date is strictly before `today`.
 * Pure helper — `today` injected for testability (AC-5).
 */
export function isTaskOverdue(task: IMyOnboardingTask, today: Date = new Date()): boolean {
  if (task.status === 'overdue') return true;
  if (task.status === 'completed' || task.status === 'cancelled') return false;
  const due = new Date(`${task.dueDate}T00:00:00`);
  const start = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  return due.getTime() < start.getTime();
}

/**
 * Whole days a task is overdue (>=1), or 0 when not overdue. Drives the
 * "X days overdue" badge (UI/UX §8). Pure helper.
 */
export function daysOverdue(task: IMyOnboardingTask, today: Date = new Date()): number {
  if (!isTaskOverdue(task, today)) return 0;
  const due = new Date(`${task.dueDate}T00:00:00`);
  const start = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  const ms = start.getTime() - due.getTime();
  const days = Math.floor(ms / (1000 * 60 * 60 * 24));
  return Math.max(1, days);
}

/** Count of completed tasks within a category (for the "2/4" badge). */
export function categoryCompletedCount(cat: IMyOnboardingCategory): number {
  return cat.tasks.filter((t) => t.status === 'completed').length;
}

/** Only the Employee may action a task; all others are read-only (BR-1 / FR-7). */
export function isEmployeeActionable(task: IMyOnboardingTask): boolean {
  return task.responsibleRole === 'Employee' && task.status !== 'completed' && task.status !== 'cancelled';
}

/** Status -> chip label for the "me" view (display-only). */
export function myTaskStatusLabel(status: MyTaskStatus): string {
  switch (status) {
    case 'pending':
      return 'Pending';
    case 'in_progress':
      return 'In progress';
    case 'completed':
      return 'Completed';
    case 'cancelled':
      return 'Cancelled';
    case 'overdue':
      return 'Overdue';
  }
}

/**
 * SVG dash-offset for a circular progress ring given a percent (0-100) and the
 * stroke circumference. Pure helper so both the widget and the page ring share
 * one calculation (UI/UX §8 progress ring). Clamps to 0-100.
 */
export function ringDashOffset(percent: number, circumference: number): number {
  const clamped = Math.max(0, Math.min(100, percent));
  return circumference * (1 - clamped / 100);
}
