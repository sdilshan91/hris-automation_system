/**
 * US-PAY-007: Payroll adjustments (bonuses, deductions, reimbursements,
 * corrections/arrears) models matching the backend API contract.
 *
 * Backend endpoints (ASSUMED REST contract — the backend agent was building in
 * parallel and had NOT pinned routes in docs/vault/modules/payroll.md when this
 * was written; the service layer is intentionally thin so a route mismatch is a
 * one-file fix in `adjustment.service.ts`). `apiBaseUrl` already includes
 * `/api/v1`, so the resource is `${apiBaseUrl}/payroll/adjustments`:
 *   GET    /payroll/adjustments?status=&type=&period=&employeeId=  - list/filter
 *   POST   /payroll/adjustments                                    - create (FR-1)
 *   POST   /payroll/adjustments/:id/cancel                         - cancel pending (FR-6)
 *   POST   /payroll/adjustments/bulk           (multipart CSV)     - bulk upload (FR-2)
 *   POST   /payroll/adjustments/:id/document   (multipart file)    - supporting doc (AC-3)
 *   GET    /payroll/adjustments/:id/document   (blob)              - download doc
 *   GET    /payroll/adjustments/template       (blob CSV)          - bulk template
 *
 * `period` filter is the wire form `YYYY-MM` (e.g. `2026-06`) so a single string
 * carries month+year; the service derives it from the period selector.
 *
 * All requests are tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain
 * header) and use withCredentials for the httpOnly cookie auth. The backend stamps
 * tenant_id + audit fields and enforces RLS (FR-8, §7, AC-5) — the FE never sends
 * tenant info.
 *
 * ENVELOPE: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so JSON methods consume BARE payloads. Binary methods
 * (document / template download, bulk CSV preview) use `responseType: 'blob'` and
 * bypass the JSON envelope.
 *
 * ENUM CASING (US-PLT-003 — critical): `AdjustmentType` and `AdjustmentStatus` are
 * PascalCase string unions matching the C# member names (global
 * JsonStringEnumConverter); they arrive as STRINGS, never integers.
 */

// ─── Enums (§7, FR-1) ──────────────────────────────────────────

/** Adjustment type (§7 adjustment_type, FR-1). Matches C# `AdjustmentType`. */
export type AdjustmentType =
  | 'Bonus'
  | 'Deduction'
  | 'Reimbursement'
  | 'Correction';

export const ADJUSTMENT_TYPE_OPTIONS: AdjustmentType[] = [
  'Bonus',
  'Deduction',
  'Reimbursement',
  'Correction',
];

/** Tailwind badge classes per adjustment type (§8). Single source of truth. */
export const ADJUSTMENT_TYPE_BADGE: Record<AdjustmentType, string> = {
  Bonus: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  Deduction: 'bg-rose-50 text-rose-700 ring-rose-600/20',
  Reimbursement: 'bg-amber-50 text-amber-700 ring-amber-600/20',
  Correction: 'bg-indigo-50 text-indigo-700 ring-indigo-600/20',
};

/** Human-readable type labels (wire value is PascalCase; Correction shows as Arrears, BR-5). */
export const ADJUSTMENT_TYPE_LABELS: Record<AdjustmentType, string> = {
  Bonus: 'Bonus',
  Deduction: 'Deduction',
  Reimbursement: 'Reimbursement',
  Correction: 'Correction / Arrears',
};

/** Adjustment status (§7 status, FR-4/FR-6). Matches C# `AdjustmentStatus`. */
export type AdjustmentStatus = 'Pending' | 'Applied' | 'Cancelled';

export const ADJUSTMENT_STATUS_OPTIONS: AdjustmentStatus[] = [
  'Pending',
  'Applied',
  'Cancelled',
];

/** Tailwind badge classes per status (§8 inline status badge). */
export const ADJUSTMENT_STATUS_BADGE: Record<AdjustmentStatus, string> = {
  Pending: 'bg-blue-50 text-blue-700 ring-blue-600/20',
  Applied: 'bg-neutral-100 text-neutral-500 ring-neutral-500/20',
  Cancelled: 'bg-neutral-100 text-neutral-400 ring-neutral-400/20',
};

export const ADJUSTMENT_STATUS_LABELS: Record<AdjustmentStatus, string> = {
  Pending: 'Pending',
  Applied: 'Applied',
  Cancelled: 'Cancelled',
};

// ─── Adjustment record (list view, §8) ─────────────────────────

/**
 * A payroll adjustment as shown in the Notion-style table (§8) and returned by the
 * list/create endpoints. Carries the denormalized employee display fields (so the
 * table needs no join) and the full set of §7 columns the FE renders.
 */
export interface IAdjustment {
  id: string;
  employeeId: string;
  /** Denormalized for the table; point-in-time snapshot. */
  employeeName: string;
  employeeNo: string;
  type: AdjustmentType;
  amount: number;
  description: string;
  /** §7 applicable_pay_month (1–12). */
  applicablePayMonth: number;
  /** §7 applicable_pay_year. */
  applicablePayYear: number;
  isTaxable: boolean;
  isRecurring: boolean;
  /** §7 recurrence_end_month (1–12), null when not recurring. */
  recurrenceEndMonth: number | null;
  recurrenceEndYear: number | null;
  status: AdjustmentStatus;
  /** True once a supporting document has been uploaded (AC-3). */
  hasDocument: boolean;
  createdAt: string;
}

// ─── Create request (FR-1) ─────────────────────────────────────

/**
 * Create-adjustment payload (FR-1). The supporting document is uploaded
 * separately as multipart to `/:id/document` once the record exists (AC-3); this
 * JSON create carries everything else. When `isRecurring` is false the recurrence
 * end fields are null.
 */
export interface IAdjustmentRequest {
  employeeId: string;
  type: AdjustmentType;
  amount: number;
  description: string;
  applicablePayMonth: number;
  applicablePayYear: number;
  isTaxable: boolean;
  isRecurring: boolean;
  recurrenceEndMonth: number | null;
  recurrenceEndYear: number | null;
}

// ─── List filters (§8) ─────────────────────────────────────────

/**
 * Filters for the adjustments table (§8: Status, Type, Period, Employee). All
 * optional; the service omits empties so the backend returns everything. `period`
 * is the wire `YYYY-MM` string.
 */
export interface IAdjustmentFilters {
  status?: AdjustmentStatus | null;
  type?: AdjustmentType | null;
  /** `YYYY-MM`, e.g. `2026-06`. */
  period?: string | null;
  employeeId?: string | null;
}

// ─── Bulk CSV upload (FR-2) ────────────────────────────────────

/**
 * One CLIENT-SIDE validated row from a parsed bulk CSV (FR-2 validation preview).
 * The backend has NO dry-run mode, so the FE parses the CSV in the browser and
 * validates each row (employee_no non-empty, type in the allowed set, amount a
 * positive number) to build this preview BEFORE the user commits. `rowNumber` is the
 * 1-based source line (header excluded).
 */
export interface IBulkAdjustmentPreviewRow {
  rowNumber: number;
  employeeNo: string;
  type: string;
  amount: number | null;
  description: string;
  isTaxable: boolean;
  valid: boolean;
  /** Validation error for the row (null when valid). */
  error: string | null;
}

/**
 * Result of CLIENT-SIDE parsing a bulk CSV (FR-2). `validCount` / `invalidCount`
 * summarize the `rows`; the FE shows the preview table and only enables Commit when
 * there is at least one valid row AND a target period is set. The period is NOT in
 * the CSV — it comes from the upload's payMonth/payYear form fields.
 */
export interface IBulkAdjustmentPreview {
  rows: IBulkAdjustmentPreviewRow[];
  totalRows: number;
  validCount: number;
  invalidCount: number;
}

/**
 * Per-row outcome of a committed bulk upload, as returned by the backend
 * (`BulkAdjustmentResultDto.results`). The upload COMMITS immediately — there is no
 * preview/dry-run on the server.
 */
export interface IBulkAdjustmentRowResult {
  rowNumber: number;
  employeeNo: string;
  success: boolean;
  adjustmentId: string | null;
  error: string | null;
  errorCode: string | null;
}

/**
 * Result of committing a bulk upload (FR-2), matching the backend
 * `BulkAdjustmentResultDto`. The endpoint commits the valid rows and reports the
 * per-row outcome; the FE shows succeeded/failed counts via a toast.
 */
export interface IBulkAdjustmentResult {
  totalRows: number;
  succeededCount: number;
  failedCount: number;
  results: IBulkAdjustmentRowResult[];
}

// ─── Period helpers ────────────────────────────────────────────

/** Month short labels for the period selector + table cell (1-indexed access). */
export const MONTH_LABELS = [
  'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
  'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec',
];

/** Render a (month, year) pair as e.g. "Jun 2026" for the table + previews. */
export function periodLabel(month: number, year: number): string {
  const m = MONTH_LABELS[month - 1] ?? '—';
  return `${m} ${year}`;
}

/** Build the wire `YYYY-MM` period string from a month/year pair. */
export function toPeriodParam(month: number, year: number): string {
  return `${year}-${String(month).padStart(2, '0')}`;
}

/**
 * Enumerate the future periods a recurring adjustment will affect (BR-6 preview):
 * every (month, year) from the start period through the recurrence-end period,
 * inclusive. Returns [] when the end is before the start (invalid range). Capped at
 * 60 entries as a guard so a typo can't blow up the preview list.
 */
export function recurringPeriods(
  startMonth: number,
  startYear: number,
  endMonth: number,
  endYear: number,
): { month: number; year: number }[] {
  const out: { month: number; year: number }[] = [];
  let m = startMonth;
  let y = startYear;
  const startIdx = startYear * 12 + (startMonth - 1);
  const endIdx = endYear * 12 + (endMonth - 1);
  if (endIdx < startIdx) {
    return out;
  }
  for (let idx = startIdx; idx <= endIdx && out.length < 60; idx++) {
    out.push({ month: m, year: y });
    m++;
    if (m > 12) {
      m = 1;
      y++;
    }
  }
  return out;
}
