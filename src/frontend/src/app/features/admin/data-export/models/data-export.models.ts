/**
 * US-ADM-010 — Tenant Data Export on Demand.
 *
 * All model interfaces, the static entity catalogue, and the small pure helpers
 * live in this single file (per the implementation brief) so the contract can be
 * adjusted in one place while the backend DTOs are finalized in parallel.
 *
 * The same shapes serve BOTH personas:
 *  - Tenant Admin  → `/api/v1/tenant/exports...`            (implicit tenant)
 *  - System Admin  → `/api/v1/system/tenants/{id}/exports...` (explicit tenant)
 *
 * Tenant isolation is enforced server-side via ITenantContext + RLS (AC-5); the
 * Tenant-Admin FE never sends a tenant id — only the auth cookie + the
 * X-Tenant-Subdomain header (added by the tenantInterceptor). The System-Admin
 * path carries the target tenant id in the URL.
 */

/** Lifecycle status of a single export job (GET history / status). */
export type ExportStatus =
  | 'Queued'
  | 'Processing'
  | 'Completed'
  | 'Failed'
  | 'Expired';

/**
 * Export scope (FR-1). Either the literal `'full'` (every entity) or an explicit
 * array of entity codes for a partial export.
 */
export type ExportScope = 'full' | string[];

/** CSV format preferences (FR-1 / FR-3). Both optional — server has defaults. */
export interface IExportFormatOptions {
  /** CSV delimiter — comma (default), semicolon, or tab. */
  delimiter: ',' | ';' | '\t';
  /** Date serialization format token. */
  dateFormat: 'ISO' | 'US' | 'EU';
}

/** Body of the initiate-export request (POST). */
export interface IInitiateExportRequest {
  /** `'full'` or an array of selected entity codes. */
  scope: ExportScope;
  /** Optional CSV format preferences. */
  formatOptions?: IExportFormatOptions;
}

/** Response from initiating an export — the new job's id + initial status. */
export interface IInitiateExportResponse {
  exportId: string;
  status: ExportStatus;
}

/** A single export job in the history list / status response. */
export interface IExportRecord {
  id: string;
  /** Human-readable scope, e.g. "Full export" or "12 entities". */
  scope: string;
  status: ExportStatus;
  /** ISO-8601 UTC timestamp the export was requested. */
  requestedAt: string;
  /** ISO-8601 UTC timestamp the export finished (null while in progress). */
  completedAt: string | null;
  /** Size of the generated bundle in bytes (null until complete). */
  fileSizeBytes: number | null;
  /** ISO-8601 UTC timestamp the 72h download link expires (null until complete). */
  expiresAt: string | null;
  /** Server-authoritative gate: whether the download is currently available. */
  downloadAvailable: boolean;
}

// ─── Entity catalogue (scope checkboxes) ─────────────────────

/** A single exportable entity option for the scope checkboxes. */
export interface IExportEntity {
  /** Stable code sent in the partial-export `scope` array (FR-1). */
  code: string;
  /** Display label (i18n-friendly, English default). */
  label: string;
}

/** A module grouping of entities, rendered under a section divider (§8). */
export interface IExportEntityGroup {
  /** Module heading, e.g. "Core HR", "Leave". */
  module: string;
  entities: IExportEntity[];
}

/**
 * The static, grouped catalogue of exportable entities (§7 / brief). Codes are
 * stable identifiers the backend maps to tables; labels are display-only. Grouped
 * by module so the UI can render module headers with subtle dividers (§8).
 */
export const EXPORT_ENTITY_CATALOGUE: readonly IExportEntityGroup[] = [
  {
    module: 'Core HR',
    entities: [
      { code: 'employees', label: 'Employees' },
      { code: 'departments', label: 'Departments' },
      { code: 'job_titles', label: 'Job Titles' },
      { code: 'locations', label: 'Locations' },
    ],
  },
  {
    module: 'Leave',
    entities: [
      { code: 'leave_types', label: 'Leave Types' },
      { code: 'leave_requests', label: 'Leave Requests' },
      { code: 'leave_balances', label: 'Leave Balances' },
      { code: 'holiday_calendars', label: 'Holiday Calendars' },
    ],
  },
  {
    module: 'Attendance',
    entities: [
      { code: 'attendance_records', label: 'Attendance Records' },
      { code: 'shifts', label: 'Shifts' },
    ],
  },
  {
    module: 'Payroll',
    entities: [
      { code: 'payroll_runs', label: 'Payroll Runs' },
      { code: 'payslips', label: 'Payslips' },
      { code: 'salary_structures', label: 'Salary Structures' },
      { code: 'salary_components', label: 'Salary Components' },
    ],
  },
  {
    module: 'Recruitment',
    entities: [
      { code: 'vacancies', label: 'Vacancies' },
      { code: 'applicants', label: 'Applicants' },
      { code: 'interviews', label: 'Interviews' },
      { code: 'offers', label: 'Offers' },
    ],
  },
  {
    module: 'Performance',
    entities: [
      { code: 'performance_goals', label: 'Performance Goals' },
      { code: 'appraisals', label: 'Appraisals' },
    ],
  },
  {
    module: 'Training & Benefits',
    entities: [
      { code: 'training_courses', label: 'Training Courses' },
      { code: 'benefits', label: 'Benefits' },
      { code: 'asset_records', label: 'Asset Records' },
    ],
  },
  {
    module: 'Administration',
    entities: [
      { code: 'custom_fields', label: 'Custom Fields' },
      { code: 'workflow_definitions', label: 'Workflow Definitions' },
      { code: 'users', label: 'Users' },
      { code: 'audit_log', label: 'Audit Log' },
    ],
  },
];

/** Flat list of every entity code in the catalogue (for "select all" parity). */
export function allEntityCodes(): string[] {
  return EXPORT_ENTITY_CATALOGUE.flatMap((g) => g.entities.map((e) => e.code));
}

// ─── Pure helpers ────────────────────────────────────────────

/** Tailwind classes for an export status badge (color-coded, §8). */
export function exportStatusClass(status: ExportStatus): string {
  switch (status) {
    case 'Completed':
      return 'bg-green-50 text-green-700 ring-1 ring-inset ring-green-200';
    case 'Processing':
      return 'bg-blue-50 text-blue-700 ring-1 ring-inset ring-blue-200';
    case 'Queued':
      return 'bg-amber-50 text-amber-700 ring-1 ring-inset ring-amber-200';
    case 'Failed':
      return 'bg-red-50 text-red-700 ring-1 ring-inset ring-red-200';
    case 'Expired':
    default:
      return 'bg-neutral-100 text-neutral-500 ring-1 ring-inset ring-neutral-200';
  }
}

/** Whether a record is still in progress (drives the poll loop + spinner). */
export function isInProgress(status: ExportStatus): boolean {
  return status === 'Queued' || status === 'Processing';
}

/**
 * Whether the download button should be enabled for a record. Trust the
 * server-authoritative `downloadAvailable` flag, but also require a `Completed`
 * status so an expired/processing row can never be downloaded (defence in depth).
 */
export function canDownload(record: IExportRecord): boolean {
  return record.downloadAvailable && record.status === 'Completed';
}

/** Format a byte count as a compact human-readable size (e.g. "2.4 MB"). */
export function formatFileSize(bytes: number | null): string {
  if (bytes === null || bytes < 0) {
    return '—';
  }
  if (bytes === 0) {
    return '0 B';
  }
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.min(
    units.length - 1,
    Math.floor(Math.log(bytes) / Math.log(1024))
  );
  const value = bytes / Math.pow(1024, i);
  return `${value.toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

/** Default format options applied when the user leaves the form untouched. */
export const DEFAULT_FORMAT_OPTIONS: IExportFormatOptions = {
  delimiter: ',',
  dateFormat: 'ISO',
};
