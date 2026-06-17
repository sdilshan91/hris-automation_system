/**
 * US-ONB-001: Onboarding checklist template models + UI constants.
 *
 * Mirrors the backend contract for onboarding templates (create/list/get/clone/
 * activate-deactivate) under `/api/v1/onboarding/templates`. Kept in ONE place so
 * the service mapping and the builder component share a single source of truth.
 */

/** Responsible-party role choices for a task (Data Requirements §7). */
export type ResponsibleRole = 'HR' | 'Manager' | 'IT' | 'Employee' | 'Custom';

export const RESPONSIBLE_ROLES: readonly ResponsibleRole[] = [
  'HR',
  'Manager',
  'IT',
  'Employee',
  'Custom',
] as const;

/** Suggested category labels (FR-2: free-text, these are just chips/presets). */
export const SUGGESTED_CATEGORIES: readonly string[] = [
  'Documentation',
  'IT Setup',
  'Training',
  'Compliance',
] as const;

/** A single onboarding task within a template (FR-3). */
export interface IOnboardingTask {
  /** Present once persisted; absent for newly-added tasks. */
  id?: string;
  title: string;
  description?: string | null;
  category?: string | null;
  responsibleRole?: ResponsibleRole | null;
  responsibleUserId?: string | null;
  dueOffsetDays: number;
  isMandatory: boolean;
  sortOrder: number;
}

/** Full onboarding checklist template (output / detail shape). */
export interface IOnboardingTemplate {
  id: string;
  templateName: string;
  description?: string | null;
  applicableDepartmentIds: string[];
  applicableJobTitleIds: string[];
  isActive: boolean;
  tasks: IOnboardingTask[];
  taskCount?: number;
  createdAt?: string;
  updatedAt?: string;
}

/** Lightweight row for the template list view (FR-6 / FR-7). */
export interface IOnboardingTemplateSummary {
  id: string;
  templateName: string;
  description?: string | null;
  isActive: boolean;
  taskCount: number;
  applicableDepartmentIds: string[];
  applicableJobTitleIds: string[];
  updatedAt?: string;
}

/** Create / clone-edit request payload (tenant_id is server-set, never sent — FR-5). */
export interface ICreateOnboardingTemplateRequest {
  templateName: string;
  description?: string | null;
  applicableDepartmentIds: string[];
  applicableJobTitleIds: string[];
  isActive: boolean;
  tasks: IOnboardingTaskRequest[];
}

/** Task payload as sent to the API (no UI-only fields). */
export interface IOnboardingTaskRequest {
  title: string;
  description?: string | null;
  category?: string | null;
  responsibleRole?: ResponsibleRole | null;
  responsibleUserId?: string | null;
  dueOffsetDays: number;
  isMandatory: boolean;
  sortOrder: number;
}

/** A pickable scope option (department or job title) for the multi-selects. */
export interface ILookupOption {
  id: string;
  name: string;
}

/** Combined lookups the builder needs (departments, job titles, named users). */
export interface IOnboardingLookups {
  departments: ILookupOption[];
  jobTitles: ILookupOption[];
  users: ILookupOption[];
}

/**
 * Group tasks by category for the collapsible builder sections (UI/UX §8).
 * Preserves first-seen category order; null/blank categories fall under
 * "Uncategorized". Pure helper — unit-tested directly.
 */
export function groupTasksByCategory(
  tasks: IOnboardingTask[],
): { category: string; tasks: IOnboardingTask[] }[] {
  const order: string[] = [];
  const map = new Map<string, IOnboardingTask[]>();
  for (const t of tasks) {
    const key = (t.category ?? '').trim() || 'Uncategorized';
    if (!map.has(key)) {
      map.set(key, []);
      order.push(key);
    }
    map.get(key)!.push(t);
  }
  return order.map((category) => ({ category, tasks: map.get(category)! }));
}

/**
 * Re-number sortOrder sequentially (0-based) after any reorder/add/remove so the
 * payload's sort_order stays dense + consistent with the on-screen order (FR-3).
 */
export function resequence(tasks: IOnboardingTask[]): IOnboardingTask[] {
  return tasks.map((t, i) => ({ ...t, sortOrder: i }));
}
