/**
 * US-CHR-010: Bulk Employee Import models.
 *
 * Backend contract assumptions (backend agent building in parallel):
 *   GET  /api/v1/employees/import/template?format=csv|xlsx  - download template blob
 *   POST /api/v1/employees/import                           - multipart upload, returns sync or async result
 *   GET  /api/v1/employees/import/jobs/:jobId               - poll async job status
 *   GET  /api/v1/employees/import/jobs/:jobId/error-report  - download error report CSV blob
 */

// ─── Template ────────────────────────────────────────────────

export type ImportTemplateFormat = 'csv' | 'xlsx';

// ─── Upload / Sync result ────────────────────────────────────

/**
 * Row-level validation error returned from the backend.
 * Maps to AC-3 error report columns: row number, field, error.
 */
export interface IImportRowError {
  row: number;
  field: string;
  error: string;
}

/**
 * Synchronous import result (<= 500 rows, FR-7).
 * Returned directly from POST /employees/import.
 */
export interface IImportResult {
  total: number;
  success: number;
  failed: number;
  errors: IImportRowError[];
}

/**
 * Async job reference (> 500 rows, FR-7 / AC-4).
 * Returned from POST /employees/import when the file is queued.
 */
export interface IImportJobRef {
  jobId: string;
  status: ImportJobStatus;
}

export type ImportJobStatus =
  | 'queued'
  | 'processing'
  | 'completed'
  | 'failed';

/**
 * Job status polling response (GET /employees/import/jobs/:jobId).
 * progress is 0-100; result is present when status === 'completed'.
 */
export interface IImportJobStatus {
  jobId: string;
  status: ImportJobStatus;
  progress: number;
  result: IImportResult | null;
}

/**
 * Discriminated union for POST /employees/import response.
 * Backend returns either a sync result (with `total`) or an async ref (with `jobId`).
 */
export type ImportResponse = IImportResult | IImportJobRef;

/** Type guard: sync result has `total`, async ref has `jobId`. */
export function isImportResult(resp: ImportResponse): resp is IImportResult {
  return 'total' in resp;
}

export function isImportJobRef(resp: ImportResponse): resp is IImportJobRef {
  return 'jobId' in resp;
}

// ─── Plan-limit pre-check (AC-5, FR-9) ──────────────────────

/**
 * When the import would exceed the plan limit, the backend returns 409
 * with this shape (or 200 with this nested in the response body).
 * The frontend checks for the `plan_limit_exceeded` code.
 */
export interface IPlanLimitWarning {
  code: 'plan_limit_exceeded';
  message: string;
  /** Max employees allowed by the tenant plan */
  maxAllowed: number;
  /** Current employee count */
  currentCount: number;
  /** Number of records in the uploaded file */
  fileRecordCount: number;
  /** How many can still be imported */
  importableCount: number;
}

export function isPlanLimitWarning(body: unknown): body is IPlanLimitWarning {
  return (
    !!body &&
    typeof body === 'object' &&
    (body as IPlanLimitWarning).code === 'plan_limit_exceeded'
  );
}

// ─── Import summary result type (for UI rendering) ──────────

export type ImportOutcome = 'all-success' | 'partial' | 'all-failed';

export function getImportOutcome(result: IImportResult): ImportOutcome {
  if (result.failed === 0) return 'all-success';
  if (result.success === 0) return 'all-failed';
  return 'partial';
}

// ─── Client-side validation (BR-7) ──────────────────────────

export const ALLOWED_IMPORT_TYPES = [
  'text/csv',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
];

export const ALLOWED_IMPORT_EXTENSIONS = ['.csv', '.xlsx'];

/** 25 MB limit (BR-7) */
export const MAX_IMPORT_FILE_SIZE_BYTES = 25 * 1024 * 1024;

export const MAX_IMPORT_FILE_SIZE_LABEL = '25 MB';

// ─── Template column guide (AC-1, Section 7) ────────────────

export interface ITemplateColumn {
  name: string;
  required: boolean;
  validation: string;
}

export const TEMPLATE_COLUMNS: ITemplateColumn[] = [
  { name: 'first_name', required: true, validation: 'Max 100 chars' },
  { name: 'last_name', required: true, validation: 'Max 100 chars' },
  { name: 'email', required: true, validation: 'Valid email, unique per tenant' },
  { name: 'phone', required: false, validation: 'E.164 format' },
  { name: 'date_of_birth', required: false, validation: 'Date (YYYY-MM-DD), past' },
  { name: 'gender', required: false, validation: 'Male / Female / Non-Binary / Prefer Not To Say' },
  { name: 'date_of_joining', required: true, validation: 'Date (YYYY-MM-DD)' },
  { name: 'department_name', required: true, validation: 'Must exist in tenant' },
  { name: 'job_title_name', required: true, validation: 'Must exist in tenant' },
  { name: 'employment_type', required: true, validation: 'Full-Time / Part-Time / Contract / Intern' },
  { name: 'location_name', required: false, validation: 'Must exist in tenant if provided' },
  { name: 'status', required: false, validation: 'Default: active' },
];
