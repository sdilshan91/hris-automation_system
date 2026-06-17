/**
 * US-ONB-002: Onboarding checklist ASSIGNMENT models + helpers.
 *
 * Mirrors the backend contract under `/api/v1/onboarding/checklists` for assigning
 * a template's tasks to a specific new hire (applicable-template lookup, preview,
 * assign, get-by-employee, modify). Kept in ONE place so the service mapping and
 * the assignment component share a single source of truth.
 *
 * Reuses ResponsibleRole from the template models (do not redefine it).
 */
import { ResponsibleRole } from './onboarding-template.models';

/** Lifecycle state of a single assigned task instance (AC-2). */
export type ChecklistTaskStatus =
  | 'pending'
  | 'in_progress'
  | 'completed'
  | 'cancelled';

/** When the employee already has an active checklist, AC-3 offers two paths. */
export type AssignMode = 'replace' | 'merge';

/**
 * An applicable template the new hire is eligible for — already filtered server
 * side by department / job title + universal templates (FR-1 / AC-1). The
 * matchReason explains *why* it surfaced so the dropdown can tag it.
 */
export interface IApplicableTemplate {
  id: string;
  templateName: string;
  description?: string | null;
  taskCount: number;
  /** e.g. 'department' | 'jobTitle' | 'universal' — display-only tag. */
  matchReason?: string | null;
}

/**
 * A single previewed/assigned task instance. The preview endpoint returns these
 * with calculated dueDate (date_of_joining + dueOffsetDays, or today + offset if
 * joining is in the past — BR-4) so the UI never recomputes dates itself.
 */
export interface IChecklistTask {
  /** Present once the checklist is assigned; absent in a fresh preview. */
  id?: string;
  /** Source template task id (null for ad-hoc tasks — FR-5). */
  templateTaskId?: string | null;
  title: string;
  description?: string | null;
  category?: string | null;
  responsibleRole?: ResponsibleRole | null;
  responsibleUserId?: string | null;
  /** Resolved display name of the responsible person, when known. */
  responsibleName?: string | null;
  dueOffsetDays: number;
  /** ISO date (yyyy-MM-dd) — server-calculated due date (FR-2 / BR-4). */
  dueDate: string;
  status: ChecklistTaskStatus;
  isMandatory: boolean;
  sortOrder: number;
}

/** Preview of a template applied to the employee, before confirming (UI/UX §8). */
export interface IChecklistPreview {
  employeeId: string;
  employeeName?: string | null;
  templateId: string;
  templateName: string;
  /** ISO date the offsets are measured from (joining date or today — BR-4). */
  startDate: string;
  tasks: IChecklistTask[];
}

/** The assigned checklist instance returned by assign/get/modify (output §7). */
export interface IAssignedChecklist {
  checklistInstanceId: string;
  employeeId: string;
  employeeName?: string | null;
  templateId: string;
  templateName: string;
  startDate: string;
  isActive: boolean;
  tasks: IChecklistTask[];
  /** People notified on assignment (drives the success-toast count, AC-2/AC-5). */
  notifiedCount?: number;
  createdAt?: string;
}

/** A task as sent to the assign/modify API (UI-only fields stripped). */
export interface IChecklistTaskRequest {
  /** Present when editing an existing instance task; absent for new tasks. */
  id?: string | null;
  templateTaskId?: string | null;
  title: string;
  description?: string | null;
  category?: string | null;
  responsibleRole?: ResponsibleRole | null;
  responsibleUserId?: string | null;
  /** ISO date (yyyy-MM-dd) — HR may override the calculated date (FR-6). */
  dueDate: string;
  isMandatory: boolean;
  sortOrder: number;
}

/** Assign-checklist request (tenant_id is server-set, never sent — FR-7). */
export interface IAssignChecklistRequest {
  employeeId: string;
  templateId: string;
  /** Defaults to the employee's date_of_joining when omitted (§7). */
  overrideStartDate?: string | null;
  /** Required only when the employee already has an active checklist (AC-3). */
  mode?: AssignMode | null;
  /** Final (possibly inline-edited) task set the HR officer confirmed. */
  tasks: IChecklistTaskRequest[];
}

/** Modify request for an existing checklist instance (AC-4 / FR-5 / FR-6). */
export interface IModifyChecklistRequest {
  tasks: IChecklistTaskRequest[];
}

/** Status -> badge palette key for the chip (display-only). */
export const CHECKLIST_STATUS_LABELS: Record<ChecklistTaskStatus, string> = {
  pending: 'Pending',
  in_progress: 'In progress',
  completed: 'Completed',
  cancelled: 'Cancelled',
};

/**
 * Sort tasks ascending by dueDate, then sortOrder as a tie-breaker. Used for the
 * mobile vertical list (UI/UX §8 — "vertical task list sorted by due date") and
 * to keep the timeline chronological. Pure — unit-tested directly.
 */
export function sortTasksByDueDate(tasks: IChecklistTask[]): IChecklistTask[] {
  return [...tasks].sort((a, b) => {
    if (a.dueDate !== b.dueDate) {
      return a.dueDate < b.dueDate ? -1 : 1;
    }
    return a.sortOrder - b.sortOrder;
  });
}

/**
 * Count distinct responsible parties across the task set. A responsible party is
 * keyed by its named user when present, else by its role (UI/UX §8 confirmation
 * summary — "summary of task count and responsible parties"). Pure helper.
 */
export function countResponsibleParties(tasks: IChecklistTask[]): number {
  const keys = new Set<string>();
  for (const t of tasks) {
    if (t.responsibleUserId) {
      keys.add(`u:${t.responsibleUserId}`);
    } else if (t.responsibleRole) {
      keys.add(`r:${t.responsibleRole}`);
    }
  }
  return keys.size;
}

/**
 * Pluralize the success-toast people count (AC-2). The N comes from the API
 * response's notifiedCount, never recomputed client-side. Pure helper.
 */
export function notificationToast(count: number): string {
  const noun = count === 1 ? 'person' : 'people';
  return `Onboarding checklist assigned. Notifications sent to ${count} ${noun}.`;
}
