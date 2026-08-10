import type { Schema } from '@core/api';

/**
 * US-CHR-005: Job Title models matching the backend API contract.
 *
 * Note on grade fields: grade_id references the Grade entity which is not yet
 * implemented. The frontend renders the grade field as a disabled placeholder
 * until that feature lands.
 * TODO(US-CHR-005): Replace gradeId/gradeName with proper grade picker once
 * the Grade entity is available.
 *
 * Note on employee count: employeeCount references the Employee entity
 * (US-CHR-001) which is not yet implemented. The frontend renders the count
 * as a dash until that feature lands.
 * TODO(US-CHR-001): Populate employeeCount once the Employees feature is available.
 */

/** Job title entity returned by the API */
/**
 * GAP-014: was hand-written with `jobTitleId` and a `tenantId` the API does not return, so every
 * deactivate call sent `undefined` in the URL. Now derived from the generated contract.
 */
export type IJobTitle = Schema<'JobTitlesJobTitleDto'> & {
  id: string;
  titleName: string;
};

/** Request payload for creating a job title (FR-1) */
export interface ICreateJobTitleRequest {
  titleName: string;
  description?: string | null;
  /** ISSUE-021: SalaryGrade id (null = no grade). Validated server-side. */
  gradeId: string | null;
  isActive: boolean;
}

/** Request payload for updating a job title (FR-1) */
export interface IUpdateJobTitleRequest {
  titleName: string;
  description?: string | null;
  /** ISSUE-021: SalaryGrade id (null = no grade). Validated server-side. */
  gradeId: string | null;
  isActive: boolean;
}

/**
 * DF-38: the raw employment-type row emitted by
 * GET /api/v1/tenant/job-titles/employment-types (EmploymentTypeDto, camelCase).
 * `name` is the exact C# enum member name (e.g. 'FullTime'); `displayName` is the
 * human-readable text (e.g. 'Full-Time'). The service maps this to
 * IEmploymentTypeOption so the consumer's { value, label } contract holds.
 */
export interface IEmploymentTypeDto {
  id: string;
  name: string;
  displayName: string;
}

/**
 * DF-38: an employment-type option consumed by the profile employment-type select.
 * `value` is the exact C# enum member name (e.g. 'FullTime') that must be sent back
 * on save; `label` is the human-readable display text (e.g. 'Full-Time').
 */
export interface IEmploymentTypeOption {
  value: string;
  label: string;
}

/** Error response shape from the backend for job title operations */
export interface IJobTitleErrorResponse {
  message: string;
  code?: 'duplicate_name' | 'has_active_employees' | string;
  employeeCount?: number;
}
