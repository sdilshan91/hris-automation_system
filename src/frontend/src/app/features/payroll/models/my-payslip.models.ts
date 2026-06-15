/**
 * US-PAY-005: Models for the EMPLOYEE self-service payslip views — the "My Payslips"
 * list (§8 Notion table: Pay Period | Gross Earnings | Deductions | Net Salary) and
 * the expandable payslip detail (earnings + deductions breakdown, optional YTD).
 *
 * Distinct from US-PAY-004's HR-facing run payslip models (payslip.models.ts): those
 * are scoped to a payroll RUN; these are scoped to the authenticated EMPLOYEE and only
 * surface payslips from Finalized runs (BR-1, FR-2 — the backend filters by status).
 *
 * Backend endpoints (assumed REST contract — backend agent building in parallel; the
 * service layer is intentionally thin so a route mismatch is a one-file fix). The
 * `apiBaseUrl` already includes `/api/v1`, so the resource is
 * `${apiBaseUrl}/payroll/...`:
 *   GET /payroll/my-payslips            - paginated list, ?year= filter (FR-1/FR-6)
 *   GET /payroll/my-payslips/:id        - full earnings/deductions breakdown (FR-3)
 *   GET /payroll/my-payslips/:id/pdf    - pre-generated PDF (blob, FR-4)
 *
 * Authorization: the backend enforces `Payroll.Read.Self` — an employee can only read
 * their OWN payslips; a cross-employee/cross-tenant id is invisible via the EF global
 * query filter ⇒ 404/403 (AC-4, BR-5, NFR-4). The FE never sends employee/tenant info;
 * tenant is carried by the tenantInterceptor (X-Tenant-Subdomain) + withCredentials.
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the `{ data }`
 * wrapper, so the JSON methods consume BARE payloads. Binary PDF download bypasses the
 * JSON envelope (responseType: 'blob').
 */

// ─── Payslip list row (§8 table) ───────────────────────────────

/**
 * One payslip as shown in the "My Payslips" Notion-style table (§8):
 * Pay Period | Gross Earnings | Deductions | Net Salary. `payMonth` is 1-12;
 * `pdfAvailable` gates the per-row "Download PDF" button (the PDF is pre-generated
 * by US-PAY-004 and may not exist yet for a just-finalized run).
 */
export interface IMyPayslipListItem {
  payslipId: string;
  /** 1-12. */
  payMonth: number;
  payYear: number;
  grossEarnings: number;
  totalDeductions: number;
  netSalary: number;
  paidDays: number;
  lopDays: number;
  /** True once the pre-generated PDF (US-PAY-004) is available for download. */
  pdfAvailable: boolean;
}

/**
 * Paginated payslip-list response (§7). The story pins a paginated shape (12/page =
 * one year of monthly payslips) — the service preserves it so the FE can show a
 * page indicator and lazy-load further years/pages.
 */
export interface IMyPayslipPage {
  items: IMyPayslipListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// ─── Payslip detail (expandable card / slide-over) ─────────────

/** Employee snapshot shown on the detail header (point-in-time, BR-2). */
export interface IMyPayslipEmployee {
  name: string;
  employeeNo: string;
  department: string | null;
  designation: string | null;
}

/**
 * A single earnings or deductions line in the detail breakdown (§8). `ytdAmount` is
 * optional — present only when the tenant has YTD totals enabled (FR-7); the FE shows
 * the YTD column only when at least one line carries it.
 */
export interface IMyPayslipComponentLine {
  componentName: string;
  amount: number;
  /** Year-to-date amount, present only when tenant YTD is enabled (FR-7). */
  ytdAmount?: number | null;
}

/**
 * Full payslip detail (§7) — drives the expandable inline card / desktop slide-over:
 * earnings (green-tinted) + deductions (red-tinted), summary totals and the days line.
 */
export interface IMyPayslipDetail {
  payslipId: string;
  payMonth: number;
  payYear: number;
  employee: IMyPayslipEmployee;
  earnings: IMyPayslipComponentLine[];
  deductions: IMyPayslipComponentLine[];
  grossEarnings: number;
  totalDeductions: number;
  netSalary: number;
  workingDays: number;
  paidDays: number;
  lopDays: number;
}

// ─── View helpers ──────────────────────────────────────────────

/** Month names indexed 1-12 (index 0 unused) for the "Pay Period" label (§8). */
export const MONTH_NAMES = [
  '',
  'January',
  'February',
  'March',
  'April',
  'May',
  'June',
  'July',
  'August',
  'September',
  'October',
  'November',
  'December',
] as const;

/** "May 2026" style label for a pay period (defensive on an out-of-range month). */
export function payPeriodLabel(month: number, year: number): string {
  const name = MONTH_NAMES[month] ?? `Month ${month}`;
  return `${name} ${year}`;
}

/** True when at least one earnings/deductions line carries a YTD amount (FR-7). */
export function hasYtd(detail: IMyPayslipDetail | null): boolean {
  if (!detail) {
    return false;
  }
  return [...detail.earnings, ...detail.deductions].some(
    (line) => line.ytdAmount != null,
  );
}
