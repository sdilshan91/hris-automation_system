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
export interface IJobTitle {
  jobTitleId: string;
  tenantId: string;
  titleName: string;
  description: string | null;
  /** TODO(US-CHR-005): Will be populated once Grade entity exists */
  gradeId: string | null;
  /** TODO(US-CHR-005): Will be populated once Grade entity exists */
  gradeName: string | null;
  isActive: boolean;
  /** TODO(US-CHR-001): Will be populated once Employee entity exists */
  employeeCount: number;
  createdAt: string;
  updatedAt: string;
}

/** Request payload for creating a job title (FR-1) */
export interface ICreateJobTitleRequest {
  titleName: string;
  description?: string | null;
  /** TODO(US-CHR-005): Omitted until Grade entity exists */
  // gradeId?: string | null;
  isActive: boolean;
}

/** Request payload for updating a job title (FR-1) */
export interface IUpdateJobTitleRequest {
  titleName: string;
  description?: string | null;
  /** TODO(US-CHR-005): Omitted until Grade entity exists */
  // gradeId?: string | null;
  isActive: boolean;
}

/** Error response shape from the backend for job title operations */
export interface IJobTitleErrorResponse {
  message: string;
  code?: 'duplicate_name' | 'has_active_employees' | string;
  employeeCount?: number;
}
