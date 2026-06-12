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

export type EmployeeStatus = 'active' | 'probation';

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
