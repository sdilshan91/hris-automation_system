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

import type { Schema } from '@core/api';

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

// ─── Wire contract → view-model mappers (D1 payroll slice) ───────────────────
//
// The header above says "assumed REST contract". It is no longer assumed: these aliases bind the
// view-models to the GENERATED contract, so `tsc` — not a runtime blank cell — reports a rename.
//
// On optionality: every generated property is `?` because Swashbuckle does not emit `required` for
// non-nullable C# reference types (see core/api/index.ts). So `?? 0` on a server-COMPUTED total is
// filling an artifact of the generator, not inventing an amount; `?? null` is used where the schema
// itself says `nullable` and the UI has a real "no value" rendering (`ytdAmount`, `department`,
// `designation`).
//
// DEFAULTING POLICY: `pdfAvailable` defaults to FALSE — an absent flag must not offer a Download
// button for a PDF that was never rendered (a 404 in the employee's face).

export type MyPayslipListWire = Schema<'PayrollMyPayslipListDto'>;
export type MyPayslipListItemWire = Schema<'PayrollMyPayslipListItemDto'>;
export type MyPayslipDetailWire = Schema<'PayrollMyPayslipDetailDto'>;
export type MyPayslipComponentWire = Schema<'PayrollMyPayslipComponentDto'>;
export type MyPayslipEmployeeWire = Schema<'PayrollMyPayslipEmployeeDto'>;

export function mapMyPayslipListItem(w: MyPayslipListItemWire): IMyPayslipListItem {
  return {
    payslipId: w.payslipId ?? '',
    payMonth: w.payMonth ?? 0,
    payYear: w.payYear ?? 0,
    grossEarnings: w.grossEarnings ?? 0,
    totalDeductions: w.totalDeductions ?? 0,
    netSalary: w.netSalary ?? 0,
    paidDays: w.paidDays ?? 0,
    lopDays: w.lopDays ?? 0,
    // Fail CLOSED: absent must not advertise a PDF that may not exist yet.
    pdfAvailable: w.pdfAvailable ?? false,
  };
}

export function mapMyPayslipComponent(
  w: MyPayslipComponentWire,
): IMyPayslipComponentLine {
  return {
    componentName: w.componentName ?? '',
    amount: w.amount ?? 0,
    // Genuinely nullable on the wire: null means "tenant YTD is off", which `hasYtd()`
    // uses to hide the whole column. Coercing it to 0 would show a fake 0.00 YTD.
    ytdAmount: w.ytdAmount ?? null,
  };
}

export function mapMyPayslipEmployee(w: MyPayslipEmployeeWire): IMyPayslipEmployee {
  return {
    name: w.name ?? '',
    employeeNo: w.employeeNo ?? '',
    department: w.department ?? null,
    designation: w.designation ?? null,
  };
}

export function mapMyPayslipDetail(w: MyPayslipDetailWire): IMyPayslipDetail {
  return {
    payslipId: w.payslipId ?? '',
    payMonth: w.payMonth ?? 0,
    payYear: w.payYear ?? 0,
    employee: mapMyPayslipEmployee(w.employee ?? {}),
    earnings: (w.earnings ?? []).map(mapMyPayslipComponent),
    deductions: (w.deductions ?? []).map(mapMyPayslipComponent),
    grossEarnings: w.grossEarnings ?? 0,
    totalDeductions: w.totalDeductions ?? 0,
    netSalary: w.netSalary ?? 0,
    workingDays: w.workingDays ?? 0,
    paidDays: w.paidDays ?? 0,
    lopDays: w.lopDays ?? 0,
  };
}
