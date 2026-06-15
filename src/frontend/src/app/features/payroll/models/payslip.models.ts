/**
 * US-PAY-004: Models for individual payslips within a payroll run — the payslip
 * list (§8 Notion table), PDF generation status, and the generation progress bar.
 *
 * Backend endpoints (assumed REST contract — backend agent building in parallel;
 * the service layer is intentionally thin so a route mismatch is a one-file fix).
 * `apiBaseUrl` already includes `/api/v1`, so the resource is
 * `${apiBaseUrl}/payroll/...`:
 *   GET    /payroll/runs/:runId/payslips                  - list payslips (table, §8)
 *   POST   /payroll/runs/:runId/payslips/generate         - enqueue generation (AC-1)
 *   POST   /payroll/runs/:runId/payslips/regenerate       - overwrite existing (AC-5)
 *   GET    /payroll/runs/:runId/payslips/status           - generation progress (§8)
 *   GET    /payroll/runs/:runId/payslips/:employeeId/download - single PDF (blob, AC-3)
 *   GET    /payroll/runs/:runId/payslips/download-zip      - bulk ZIP (blob, AC-3)
 *
 * All requests are tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain
 * header) and use withCredentials for the httpOnly cookie auth. The backend stamps
 * tenant_id, enforces RLS, and validates blob paths against traversal (AC-4,
 * NFR-6) — the FE never sends tenant info.
 *
 * ENVELOPE: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so the service consumes BARE payloads (binary downloads are
 * unaffected — they bypass the JSON envelope).
 *
 * ENUM CASING (US-PLT-003 — critical): `PdfStatus` is a PascalCase string union
 * matching the C# member names (global JsonStringEnumConverter); statuses arrive
 * as STRINGS, never integers.
 */

// ─── PDF status enum (§7 pdf_status, FR-7) ─────────────────────

/**
 * Per-payslip PDF generation status (§7 `pdf_status` column). Matches the C#
 * `PdfStatus` enum members. `Pending` = not yet rendered (or queued); `Generated`
 * = PDF stored in blob storage; `Failed` = render errored (retryable, FR-8).
 */
export type PdfStatus = 'Pending' | 'Generated' | 'Failed';

/** Tailwind badge classes per PDF status (§8 status badge). Single source of truth. */
export const PDF_STATUS_BADGE: Record<PdfStatus, string> = {
  Pending: 'bg-neutral-100 text-neutral-600 ring-neutral-500/20',
  Generated: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  Failed: 'bg-rose-50 text-rose-700 ring-rose-600/20',
};

/** Human-readable PDF status labels (wire value is PascalCase). */
export const PDF_STATUS_LABELS: Record<PdfStatus, string> = {
  Pending: 'Pending',
  Generated: 'Generated',
  Failed: 'Failed',
};

// ─── Payslip row (list view, §8) ───────────────────────────────

/**
 * One payslip as shown in the run-detail Notion-style table (§8): Employee Name |
 * Employee No | Department | Net Salary | PDF Status | Actions. Carries the slip id
 * (for per-employee download/preview) and the denormalized employee display fields
 * (point-in-time snapshot, BR-2). `netSalary` is the slip's net pay (§7 summary).
 */
export interface IPayslip {
  id: string;
  employeeId: string;
  employeeName: string;
  employeeNo: string;
  department: string | null;
  netSalary: number;
  pdfStatus: PdfStatus;
  /** ISO timestamp, null until the PDF is generated (§7 pdf_generated_at). */
  pdfGeneratedAt: string | null;
}

// ─── Generation status (progress bar, §8) ──────────────────────

/**
 * Aggregate PDF-generation status for a run, driving the status bar (§8: "4,850 /
 * 5,000 generated"). The FE polls this while generation is in progress and stops
 * once it settles (no longer generating). `failedCount` surfaces partial failures
 * (FR-8 — retryable individually).
 *
 * `isGenerating` is the authoritative "still running" flag the poll loop checks; it
 * is true while the Hangfire job (AC-1, FR-4) is rendering and false once every
 * payslip has reached a terminal state (Generated or Failed).
 */
export interface IPayslipGenerationStatus {
  runId: string;
  /** True while the background generation job is running (drives the poll loop). */
  isGenerating: boolean;
  totalCount: number;
  generatedCount: number;
  failedCount: number;
  pendingCount: number;
}
