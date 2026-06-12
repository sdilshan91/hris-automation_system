/**
 * US-CHR-001: Employee models matching the backend API contract.
 *
 * Data Requirements (Section 7):
 *   - first_name: varchar(100), required
 *   - last_name: varchar(100), required
 *   - email: varchar(150), required, unique per tenant
 *   - phone: varchar(20), optional, E.164 preferred
 *   - date_of_birth: date, optional, age >= 16
 *   - gender: enum, optional
 *   - date_of_joining: date, required, not > 90 days future
 *   - department_id: uuid, required
 *   - job_title_id: uuid, required
 *   - employment_type: enum, required
 *   - status: varchar(20), default 'active'
 *   - profile_photo: file, optional (JPEG/PNG/WebP, max 5 MB)
 *   - custom_fields: jsonb, optional
 */

// ─── Enums ────────────────────────────────────────────────────

export type EmployeeGender =
  | 'Male'
  | 'Female'
  | 'Non-Binary'
  | 'Prefer Not To Say';

export type EmploymentType =
  | 'Full-Time'
  | 'Part-Time'
  | 'Contract'
  | 'Intern';

export type EmployeeStatus = 'active' | 'probation' | 'suspended' | 'terminated' | 'inactive';

export const EMPLOYEE_STATUS_OPTIONS: EmployeeStatus[] = [
  'active',
  'probation',
  'suspended',
  'terminated',
  'inactive',
];

export const GENDER_OPTIONS: EmployeeGender[] = [
  'Male',
  'Female',
  'Non-Binary',
  'Prefer Not To Say',
];

export const EMPLOYMENT_TYPE_OPTIONS: EmploymentType[] = [
  'Full-Time',
  'Part-Time',
  'Contract',
  'Intern',
];

// ─── Entity ───────────────────────────────────────────────────

/** Employee entity returned by the API */
export interface IEmployee {
  employeeId: string;
  tenantId: string;
  employeeNo: string;
  firstName: string;
  lastName: string;
  email: string;
  phone: string | null;
  dateOfBirth: string | null;
  gender: EmployeeGender | null;
  dateOfJoining: string;
  departmentId: string;
  departmentName: string | null;
  jobTitleId: string;
  jobTitleName: string | null;
  employmentType: EmploymentType;
  status: EmployeeStatus;
  profilePhotoUrl: string | null;
  customFields: Record<string, unknown> | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

// ─── Request payloads ─────────────────────────────────────────

/** Create employee request — multipart form fields (FR-1, FR-6) */
export interface ICreateEmployeeRequest {
  firstName: string;
  lastName: string;
  email: string;
  phone?: string | null;
  dateOfBirth?: string | null;
  gender?: EmployeeGender | null;
  dateOfJoining: string;
  departmentId: string;
  jobTitleId: string;
  employmentType: EmploymentType;
  status?: EmployeeStatus;
  customFields?: Record<string, unknown> | null;
  // Contact info (step 2)
  address?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  country?: string | null;
  // Emergency contact (step 3)
  emergencyContactName?: string | null;
  emergencyContactRelationship?: string | null;
  emergencyContactPhone?: string | null;
}

/** Draft save payload — partial data for any step */
export interface ISaveEmployeeDraftRequest {
  draftData: Record<string, unknown>;
  currentStep: number;
}

// ─── Error responses ──────────────────────────────────────────

/** Error response shape from the backend for employee operations */
export interface IEmployeeErrorResponse {
  message: string;
  code?: 'duplicate_email' | 'plan_limit_reached' | 'validation_error' | string;
}

// ─── Wizard step model ────────────────────────────────────────

export interface IWizardStep {
  index: number;
  label: string;
  key: string;
  required: boolean;
}

export const WIZARD_STEPS: IWizardStep[] = [
  { index: 0, label: 'Personal Info', key: 'personal', required: true },
  { index: 1, label: 'Contact', key: 'contact', required: true },
  { index: 2, label: 'Emergency Contact', key: 'emergency', required: true },
  { index: 3, label: 'Employment Details', key: 'employment', required: true },
  { index: 4, label: 'Education', key: 'education', required: false },
  { index: 5, label: 'Work History', key: 'workHistory', required: false },
  { index: 6, label: 'Dependents', key: 'dependents', required: false },
];

// ─── Photo validation constants (AC-4, FR-6) ─────────────────

export const ALLOWED_PHOTO_TYPES = ['image/jpeg', 'image/png', 'image/webp'];
export const MAX_PHOTO_SIZE_BYTES = 5 * 1024 * 1024; // 5 MB
export const MAX_PHOTO_SIZE_LABEL = '5 MB';

// ─── US-CHR-002: Employee Profile models ─────────────────────

/**
 * Extended employee status to include terminated/suspended states
 * visible on the profile page (AC-1 status badge).
 */
export type EmployeeProfileStatus =
  | 'active'
  | 'probation'
  | 'terminated'
  | 'suspended'
  | 'inactive';

/** Emergency contact sub-entity */
export interface IEmergencyContact {
  id?: string;
  name: string;
  relationship: string;
  phone: string;
}

/** Education record sub-entity */
export interface IEducationRecord {
  id?: string;
  institution: string;
  degree: string;
  fieldOfStudy?: string | null;
  startYear?: string | null;
  endYear?: string | null;
}

/** Work history record sub-entity (prior employment) */
export interface IWorkHistoryRecord {
  id?: string;
  company: string;
  position: string;
  fromDate: string | null;
  toDate: string | null;
  description?: string | null;
}

/** Dependent sub-entity */
export interface IDependentRecord {
  id?: string;
  name: string;
  relationship: string;
  dateOfBirth: string | null;
}

/**
 * Employment history timeline entry (FR-6, AC-6).
 * Append-only log of changes to department, job title, status, reporting manager.
 */
export interface IEmploymentHistoryEntry {
  id: string;
  effectiveDate: string;
  changeType: 'department' | 'job_title' | 'status' | 'status_change' | 'reporting_manager' | string;
  previousValue: string | null;
  newValue: string;
  reason?: string | null;
  changedBy: string | null;
  changedAt: string;
}

/**
 * Full employee profile returned by GET /employees/:id/profile (US-CHR-002 AC-1).
 *
 * Extends IEmployee with related sub-entities for all profile sections.
 * Includes `xmin` for optimistic concurrency (AC-3, FR-4).
 */
export interface IEmployeeProfile extends IEmployee {
  /** Optimistic concurrency token (PostgreSQL xmin) */
  xmin: string;
  /** Personal email (separate from work email) */
  personalEmail: string | null;
  /** Address fields */
  address: string | null;
  city: string | null;
  state: string | null;
  postalCode: string | null;
  country: string | null;
  /** Reporting manager name for display */
  reportingManagerId: string | null;
  reportingManagerName: string | null;
  /** Sub-entities */
  emergencyContacts: IEmergencyContact[];
  education: IEducationRecord[];
  workHistory: IWorkHistoryRecord[];
  dependents: IDependentRecord[];
  employmentHistory: IEmploymentHistoryEntry[];
}

/**
 * Section keys for per-section PATCH updates.
 * Maps to the backend PATCH /employees/:id/sections/:section endpoint.
 */
export type ProfileSection =
  | 'personal-info'
  | 'contact'
  | 'emergency-contacts'
  | 'employment'
  | 'education'
  | 'work-history'
  | 'dependents'
  | 'custom-fields';

/**
 * Generic section update request payload.
 * Carries the xmin token for optimistic concurrency (AC-3).
 */
export interface IUpdateSectionRequest {
  xmin: string;
  data: Record<string, unknown>;
}

/**
 * Section update response — returns the updated profile and new xmin.
 */
export interface IUpdateSectionResponse {
  xmin: string;
  profile: IEmployeeProfile;
}

/**
 * User role for field-level permission resolution (AC-4, AC-5, FR-3).
 */
export type ProfileViewerRole = 'hr_officer' | 'employee' | 'manager';

/**
 * Field-level edit permissions per role (Section 7 Data Requirements table).
 * Returns true if the given section is editable by the role.
 */
export function isSectionEditable(
  section: ProfileSection,
  role: ProfileViewerRole
): boolean {
  if (role === 'manager') return false; // managers: read-only for all

  if (role === 'hr_officer') return true; // HR: full access

  // Employee role: limited subset
  const employeeEditableSections: ProfileSection[] = [
    'contact',
    'emergency-contacts',
    'education',
    'work-history',
    'dependents',
  ];
  return employeeEditableSections.includes(section);
}

// ─── Directory models (US-CHR-003) ──────────────────────────

/** Paginated response wrapper from the backend (AC-4, FR-5) */
export interface IPaginatedResponse<T> {
  data: T[];
  total: number;
  page: number;
  pageSize: number;
}

/**
 * Query parameters for the employee directory endpoint.
 * Maps to GET /api/v1/tenant/employees with query string params.
 * Multi-select filters are sent as comma-separated values.
 */
export interface IEmployeeDirectoryParams {
  search?: string;
  departments?: string[];
  jobTitles?: string[];
  statuses?: EmployeeStatus[];
  employmentTypes?: EmploymentType[];
  location?: string;
  dateOfJoiningFrom?: string;
  dateOfJoiningTo?: string;
  sort?: EmployeeSortField;
  sortDirection?: 'asc' | 'desc';
  page?: number;
  pageSize?: number;
  includeArchived?: boolean;
}

/** Sortable fields for the directory (FR-4) */
export type EmployeeSortField =
  | 'name'
  | 'employee_no'
  | 'date_of_joining'
  | 'department';

export const EMPLOYEE_SORT_OPTIONS: { value: EmployeeSortField; label: string }[] = [
  { value: 'name', label: 'Name' },
  { value: 'employee_no', label: 'Employee No.' },
  { value: 'date_of_joining', label: 'Date of Joining' },
  { value: 'department', label: 'Department' },
];

/** Page size options for the directory (FR-5) */
export const PAGE_SIZE_OPTIONS: number[] = [10, 20, 50];

/** View mode for the directory (FR-3) */
export type DirectoryViewMode = 'card' | 'table';

/** Export format options (FR-8, AC-5) */
export type ExportFormat = 'csv' | 'excel';

/**
 * Represents an active filter chip displayed below the search bar.
 * Each chip corresponds to one filter value that can be individually removed.
 */
export interface IActiveFilterChip {
  category: string;
  label: string;
  value: string;
  filterKey: keyof IEmployeeDirectoryParams;
}

// ─── US-CHR-009: Employee Status Management models ─────────

/**
 * Valid status transition returned by the backend.
 * The backend is the source of truth for the transition matrix (BR-1).
 */
export interface IStatusTransition {
  targetStatus: EmployeeStatus;
  label: string;
  sideEffects: string[];
}

/**
 * Request payload for changing employee status.
 * POST /api/v1/tenant/employees/:id/status
 */
export interface IChangeStatusRequest {
  newStatus: EmployeeStatus;
  effectiveDate: string;
  reason: string;
}

/**
 * Response from the change-status endpoint.
 * Returns the updated employee profile (with new status and new timeline entry).
 */
export interface IChangeStatusResponse {
  profile: IEmployeeProfile;
}

/**
 * Status badge color mapping (US-CHR-009 Section 7).
 * Returns the CSS class suffix for a given employee status.
 */
export function getStatusBadgeClasses(status: EmployeeStatus | string): string {
  switch (status) {
    case 'active':     return 'bg-green-100 text-green-800';
    case 'probation':  return 'bg-amber-100 text-amber-800';
    case 'suspended':  return 'bg-gray-100 text-gray-800';
    case 'terminated': return 'bg-red-100 text-red-800';
    case 'inactive':   return 'bg-slate-100 text-slate-800';
    default:           return 'bg-neutral-100 text-neutral-600';
  }
}
