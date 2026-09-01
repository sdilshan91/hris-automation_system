/**
 * US-PAY-009: Payroll Reports & Analytics — view models for the reports page and
 * the analytics dashboard.
 *
 * Wire conventions (shared across the Payroll feature):
 *  - Bare payloads: the global ApiResponse unwrap interceptor (US-PLT-001) strips
 *    the `{ data }` envelope, so JSON methods consume BARE payloads.
 *  - Report-type ids are PascalCase string unions matching the backend report-type
 *    enum (US-PLT-003). The source of truth for the UI sidebar is the BE descriptor
 *    list (GET /payroll/reports); this file holds the union + FE-only chart hints.
 *  - Money/numerics arrive already FORMATTED as strings inside `cells` (the BE owns
 *    column formatting); the FE renders cells verbatim. Analytics points carry raw
 *    numbers for the FE-drawn charts.
 */

import type { Schema } from '@core/api';

// ─── Report types (FR-1) ──────────────────────────────────────────

/**
 * The out-of-the-box payroll report ids (FR-1). These string ids are the
 * `:reportType` path segment AND the sidebar selection key. PascalCase to match the
 * backend enum.
 */
export type PayrollReportType =
  | 'PayrollSummary'
  | 'EmployeeRegister'
  | 'DepartmentSummary'
  | 'StatutoryDeduction'
  | 'BankAdvice'
  | 'Ctc'
  | 'Variance'
  | 'YearEndTaxStatement';

/** Export file formats (FR-2): CSV, Excel (.xlsx via ClosedXML), PDF (via QuestPDF). */
export type ReportExportFormat = 'csv' | 'xlsx' | 'pdf';

/**
 * A report-type descriptor as returned by `GET /payroll/reports` (FR-1). PascalCase
 * `id` is the `:reportType` route segment. `deferred` flags reports that are produced
 * asynchronously (e.g. the year-end tax statement bulk-PDF flow).
 */
export interface IReportTypeMeta {
  /** Stable id == the `:reportType` route segment. */
  id: PayrollReportType;
  /** Display name shown in the sidebar + report header. */
  name: string;
  /** One-line description shown under the report header. */
  description: string;
  /** Whether the report is produced asynchronously (deferred / bulk). */
  deferred: boolean;
}

/**
 * Filters applied to a report (FR-3). `period` is the wire `YYYY-MM` string; the
 * service splits it into the backend's separate `payMonth` + `payYear` query params.
 */
export interface IReportFilters {
  /** Pay period as `YYYY-MM` (one field carries month + year for the UI). */
  period: string;
  /** Department id, or null for "all departments". */
  departmentId: string | null;
  /**
   * US-RPT-003 FR-4: a specific payroll run id, or null to use the latest finalized
   * run for the period (supports supplementary / multiple runs per period).
   */
  payrollRunId?: string | null;
}

// ─── Generic report payload (the `:reportType` GET) ───────────────

/** A single row of a report table — pre-formatted cells aligned to {@link IReportResult.columns}. */
export interface IReportRow {
  /** Display-ready cell values, one per column (already formatted by the BE). */
  cells: string[];
}

/**
 * A generated report (`GET /payroll/reports/{reportType}`). The backend returns
 * column headers + pre-formatted string rows so the FE renders a generic table
 * without hardcoding each report's shape. `totalRow` is an optional footer row and
 * `note` carries a contextual message (e.g. "showing most-recent finalized run").
 *
 * US-RPT-003 (AC-1, FR-3): the `summary` block is OPTIONAL and only populated for the
 * Payroll Run Summary report. When present it drives the KPI cards + the month-over-
 * month dual bar chart; when absent the report renders only the generic table (the
 * US-PAY-009 behaviour is unchanged).
 */
export interface IReportResult {
  reportType: PayrollReportType;
  /** Display title for the report header. */
  title: string;
  payMonth: number;
  payYear: number;
  /** Column header labels. */
  columns: string[];
  /** Data rows (cells aligned to {@link columns}). */
  rows: IReportRow[];
  /** Optional footer/total row (cells aligned to {@link columns}). */
  totalRow?: IReportRow | null;
  /** Total number of data rows (may exceed `rows.length` if paginated). */
  totalCount: number;
  /** Optional contextual note shown under the table. */
  note?: string | null;
  /** US-RPT-003 AC-1/FR-3: KPI + month-over-month summary (Payroll Run Summary only). */
  summary?: IPayrollRunSummary | null;
}

// ─── Payroll Run Summary (US-RPT-003 AC-1 / FR-3) ─────────────────

/**
 * The KPI metric keys surfaced as summary cards (UI §8). `gross`, `deductions` and
 * `net` are cost metrics (increase = red, decrease = green per FR-3); `employeeCount`
 * is a headcount metric (variance shown but coloured neutrally).
 */
export type PayrollSummaryMetricKey = 'gross' | 'deductions' | 'net' | 'employeeCount';

/**
 * One KPI metric with its current value and the previous-period comparison (FR-3).
 * `previous`/`variance` are null when there is no prior finalized run to compare
 * against. `variance` is the signed delta (current − previous). `isCost` flips the
 * colour semantics: for cost metrics an increase is bad (red).
 */
export interface IPayrollSummaryMetric {
  key: PayrollSummaryMetricKey;
  /** Display label, e.g. "Total Gross". */
  label: string;
  /** Current-period raw value. */
  current: number;
  /** Previous-period raw value, or null when no prior run exists. */
  previous: number | null;
  /** Signed delta (current − previous), or null when no prior run exists. */
  variance: number | null;
  /** True for monetary cost metrics (increase highlighted red, FR-3). */
  isCost: boolean;
}

/**
 * Payroll Run Summary metrics + month-over-month comparison (AC-1, FR-3). `currency`
 * is the tenant's configured currency code (BR-2). `previousLabel`/`currentLabel` are
 * the two period labels for the dual bar chart axis.
 */
export interface IPayrollRunSummary {
  currency: string;
  currentLabel: string;
  previousLabel: string | null;
  metrics: IPayrollSummaryMetric[];
}

// ─── Bank advice (BR-2 / AC-2) ────────────────────────────────────

/**
 * One bank-advice preview line. Account numbers are returned ALREADY MASKED by the
 * backend (BR-2: last 4 digits only in the UI preview); the full numbers live only
 * in the downloaded file. `accountNumber` is e.g. "••••6789".
 */
export interface IBankAdviceLine {
  employeeNo: string;
  employeeName: string;
  bankName: string;
  branchCode: string;
  /** Masked for the UI preview (BR-2), e.g. "••••6789". */
  accountNumber: string;
  netAmount: number;
  narration: string;
}

/** Bank-advice preview payload (`GET /payroll/reports/bank-advice/preview`). */
export interface IBankAdvicePreview {
  payMonth: number;
  payYear: number;
  lines: IBankAdviceLine[];
  employeeCount: number;
  totalNetAmount: number;
  note?: string | null;
}

// ─── Analytics (the `/payroll/analytics/{chartType}` GETs, FR-5) ──

/** Analytics chart types served separately under `/payroll/analytics/{chartType}`. */
export type PayrollChartType =
  | 'MonthlyTrend'
  | 'DepartmentCostDistribution'
  | 'StatutoryBreakdown';

/** A single labelled data point (label + raw numeric value) for an analytics chart. */
export interface IAnalyticsPoint {
  label: string;
  value: number;
}

/** A named series of points (for multi-series charts like the monthly trend). */
export interface IAnalyticsSeries {
  name: string;
  points: IAnalyticsPoint[];
}

/**
 * One analytics chart payload (`GET /payroll/analytics/{chartType}`). Single-series
 * charts (department distribution) use `points`; multi-series charts (monthly trend,
 * statutory breakdown) use `series` with `categories` as the shared x-axis labels.
 */
export interface IPayrollAnalyticsResult {
  chartType: PayrollChartType | string;
  points: IAnalyticsPoint[];
  categories: string[];
  series: IAnalyticsSeries[];
}

/**
 * The combined analytics dashboard view model — assembled FE-side by `forkJoin`ing
 * the three `/payroll/analytics/{chartType}` calls (there is NO single dashboard
 * endpoint). Each field holds one chart's result.
 */
export interface IDashboardAnalytics {
  trend: IPayrollAnalyticsResult;
  departmentCosts: IPayrollAnalyticsResult;
  statutory: IPayrollAnalyticsResult;
}

// ─── Static descriptors + pure helpers ────────────────────────────

/**
 * FE-side report-type descriptors used as a fallback for the sidebar before the
 * backend descriptor list (`GET /payroll/reports`) loads. Order matches the story's
 * FR-1 listing. Kept as data (not hardcoded in the template) so the sidebar renders
 * generically and is trivially unit-testable.
 */
export const REPORT_TYPES: IReportTypeMeta[] = [
  {
    id: 'PayrollSummary',
    name: 'Payroll Summary',
    description: 'Period totals with a department-wise breakdown.',
    deferred: false,
  },
  {
    id: 'EmployeeRegister',
    name: 'Employee Register',
    description: 'All employees with component-wise pay for the period.',
    deferred: false,
  },
  {
    id: 'DepartmentSummary',
    name: 'Department Summary',
    description: 'Cost totals grouped by department.',
    deferred: false,
  },
  {
    id: 'StatutoryDeduction',
    name: 'Statutory Deduction',
    description: 'Tax, EPF and ETF summaries for statutory filing.',
    deferred: false,
  },
  {
    id: 'BankAdvice',
    name: 'Bank Advice',
    description: 'Salary disbursement file for the bank.',
    deferred: false,
  },
  {
    id: 'Ctc',
    name: 'CTC Report',
    description: 'Current cost-to-company of all employees.',
    deferred: false,
  },
  {
    id: 'Variance',
    name: 'Variance Report',
    description: 'Month-over-month change, highlighting > 10% swings.',
    deferred: false,
  },
  {
    id: 'YearEndTaxStatement',
    name: 'Year-End Tax Statement',
    description: 'Annual tax statements (generated asynchronously).',
    deferred: true,
  },
];

/**
 * Report types whose preview renders a department-wise bar chart (FE hint only — the
 * BE report result is a generic table, so the chart is derived FE-side from the
 * rows). Mirrors the chart-bearing reports in the story (§8).
 */
export const CHART_REPORT_TYPES: ReadonlySet<PayrollReportType> = new Set<PayrollReportType>([
  'PayrollSummary',
  'DepartmentSummary',
  'StatutoryDeduction',
  'Variance',
]);

/** Whether a report type renders a derived bar chart in its preview. */
export function reportHasChart(id: PayrollReportType): boolean {
  return CHART_REPORT_TYPES.has(id);
}

/** Human label for a report-type id (falls back to the id). */
export function reportTypeName(id: PayrollReportType): string {
  return REPORT_TYPES.find((r) => r.id === id)?.name ?? id;
}

/** The default pay period (`YYYY-MM`) — the PREVIOUS month (payroll runs in arrears). */
export function defaultReportPeriod(now: Date = new Date()): string {
  const d = new Date(now.getFullYear(), now.getMonth() - 1, 1);
  const y = d.getFullYear();
  const m = `${d.getMonth() + 1}`.padStart(2, '0');
  return `${y}-${m}`;
}

/**
 * Split a `YYYY-MM` period into the backend's separate `payMonth` (1-12) + `payYear`
 * query params. Returns `null` for an unparseable / out-of-range period so callers
 * can omit the params (the BE then defaults to the most-recent finalized run).
 */
export function splitPeriod(period: string): { payMonth: number; payYear: number } | null {
  const m = /^(\d{4})-(\d{2})$/.exec(period);
  if (!m) {
    return null;
  }
  const payYear = Number(m[1]);
  const payMonth = Number(m[2]);
  if (payMonth < 1 || payMonth > 12) {
    return null;
  }
  return { payMonth, payYear };
}

/** Format a `YYYY-MM` period as "Jun 2026". Returns the input as-is when unparseable. */
export function periodLabel(period: string): string {
  const m = /^(\d{4})-(\d{2})$/.exec(period);
  if (!m) {
    return period;
  }
  const year = Number(m[1]);
  const month = Number(m[2]);
  if (month < 1 || month > 12) {
    return period;
  }
  return `${MONTHS[month - 1]} ${year}`;
}

const MONTHS = [
  'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
  'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec',
];

/** Format a `payMonth`/`payYear` pair as "Jun 2026". */
export function monthYearLabel(payMonth: number, payYear: number): string {
  if (payMonth < 1 || payMonth > 12) {
    return `${payYear}`;
  }
  return `${MONTHS[payMonth - 1]} ${payYear}`;
}

/**
 * Build the y-coordinate set for a multi-series line chart over a shared max,
 * mapping each value into a `[0, height]` SVG band (top = high value). Pure +
 * unit-tested; backs the analytics monthly-trend line chart (FR-5) the same way
 * the attendance trends chart is drawn (no charting library).
 */
export function lineY(value: number, max: number, height: number): number {
  const m = max > 0 ? max : 1;
  const v = Math.max(0, value);
  return height - (v / m) * height;
}

/** Evenly spaced x-coordinate for point `index` of `count` over `width`. */
export function lineX(index: number, count: number, width: number): number {
  if (count <= 1) {
    return width / 2;
  }
  return (index / (count - 1)) * width;
}

/**
 * Build an SVG polyline `points` string for a series of values. Pure helper for the
 * trend line chart (FR-5). `[]` for an empty series.
 */
export function polylinePoints(
  values: number[],
  max: number,
  width: number,
  height: number,
): string {
  return values
    .map((v, i) => `${round2(lineX(i, values.length, width))},${round2(lineY(v, max, height))}`)
    .join(' ');
}

/**
 * The overall max value across one or more series, so they share one y-scale.
 * Returns 0 for an empty input (callers fall back to 1).
 */
export function seriesMax(series: IAnalyticsSeries[]): number {
  let max = 0;
  for (const s of series) {
    for (const p of s.points) {
      max = Math.max(max, p.value);
    }
  }
  return max;
}

/** Round to 2 decimals (SVG coordinate noise reduction). */
function round2(n: number): number {
  return Math.round(n * 100) / 100;
}

// ─── KPI variance semantics (US-RPT-003 FR-3) ─────────────────────

/** Direction of a metric's month-over-month change. */
export type VarianceDirection = 'up' | 'down' | 'flat' | 'none';

/**
 * The direction of a metric's variance: `none` when there is no prior period to
 * compare, else `up` / `down` / `flat`. Pure + unit-tested.
 */
export function varianceDirection(metric: IPayrollSummaryMetric): VarianceDirection {
  if (metric.variance === null || metric.previous === null) {
    return 'none';
  }
  if (metric.variance > 0) {
    return 'up';
  }
  if (metric.variance < 0) {
    return 'down';
  }
  return 'flat';
}

/**
 * The Tailwind text-colour token for a metric's variance (FR-3 highlighting). For
 * cost metrics an increase is bad (red) and a decrease is good (green); headcount
 * (`isCost === false`) is coloured neutrally either way. No prior period → neutral.
 */
export function varianceColorClass(metric: IPayrollSummaryMetric): string {
  const dir = varianceDirection(metric);
  if (dir === 'none' || dir === 'flat') {
    return 'text-neutral-400';
  }
  if (!metric.isCost) {
    return 'text-neutral-500';
  }
  // Cost metric: up = red (worse), down = green (better).
  return dir === 'up' ? 'text-red-600' : 'text-emerald-600';
}

/**
 * Percentage change of a metric vs the previous period, or null when there is no
 * prior period (or the previous value is 0, where a percentage is undefined). Pure.
 */
export function variancePercent(metric: IPayrollSummaryMetric): number | null {
  if (metric.variance === null || metric.previous === null || metric.previous === 0) {
    return null;
  }
  return round2((metric.variance / Math.abs(metric.previous)) * 100);
}

// ─── Wire contract → view-model mappers (D1 payroll slice) ───────────────────
//
// `http.get<IReportResult>(…)` was an unchecked ASSERTION, not a check: TypeScript believed the
// hand-written interface while the server sent whatever it sent. These aliases bind the view-models
// above to the GENERATED contract, so a backend rename becomes a compile error here instead of an
// `undefined` cell in the report table or a blank KPI card.
//
// Every generated property is optional because Swashbuckle does not emit `required` for non-nullable
// C# reference types, so most `??` below fill a GENERATOR ARTIFACT, not a real absence. Where the
// SCHEMA itself marks a field nullable (`previous`, `variance`, `note`, `totalRow`, `summary`) the
// default is `null` and that distinction is load-bearing.
//
// DEFAULTING POLICY (payroll is money — a default is a decision):
//  - `deferred` defaults to FALSE. An absent flag must not mark a synchronous report as async.
//  - `isCost` defaults to FALSE. An absent flag must not paint a metric with the red "cost went up"
//    semantics of FR-3; neutral is the least-claiming colour.
//  - `previous` / `variance` default to NULL, never 0. `null` means "no prior finalized run" and makes
//    `varianceDirection()` return 'none'; a 0 would render a fabricated "0.0% vs last period" delta on
//    a first-ever payroll run.
//  - `totalRow` / `summary` default to NULL, not an empty object — absent means "no footer row" /
//    "no KPI cards", and an empty shell would render an empty grey row and four blank KPI tiles.
//  - Enum-ish strings (`reportType`, `chartType`, metric `key`, descriptor `id`) are kept RAW and only
//    cast. The contract types all four as plain `string`, so there is nothing to validate against; the
//    important part is that an unknown value must NOT be coerced into a known one. An unrecognised
//    `reportType` simply makes `reportHasChart()` return false — no chart, no wrong chart.
//  - Money/counts (`current`, `netAmount`, `totalNetAmount`, `totalCount`, `employeeCount`, chart
//    `value`) default to 0: all are server-computed non-nullable numerics, and the view-models type
//    them as plain `number` — there is no "not yet calculated" state the UI renders differently.

export type ReportTypeMetaWire = Schema<'PayrollPayrollReportDescriptorDto'>;
export type ReportRowWire = Schema<'PayrollPayrollReportRow'>;
export type ReportResultWire = Schema<'PayrollPayrollReportResult'>;
export type PayrollRunSummaryWire = Schema<'PayrollPayrollReportSummaryDto'>;
export type PayrollSummaryMetricWire = Schema<'PayrollPayrollSummaryMetricDto'>;
export type BankAdviceLineWire = Schema<'PayrollBankAdviceLineDto'>;
export type BankAdvicePreviewWire = Schema<'PayrollBankAdvicePreviewDto'>;
export type AnalyticsPointWire = Schema<'PayrollPayrollChartPoint'>;
export type AnalyticsSeriesWire = Schema<'PayrollPayrollChartSeries'>;
export type PayrollAnalyticsResultWire = Schema<'PayrollPayrollAnalyticsResult'>;

/**
 * `GET /payroll/reports` descriptor → sidebar entry. `id` is the `:reportType` route segment and is
 * kept RAW (the wire types it `string`); when the name is missing we fall back to the id so the
 * sidebar never renders a blank, unclickable row.
 */
export function mapReportTypeMeta(w: ReportTypeMetaWire): IReportTypeMeta {
  const id = (w.id ?? '') as PayrollReportType;
  return {
    id,
    name: w.name ?? id,
    description: w.description ?? '',
    // Least-claiming: absent must not flag a synchronous report as deferred/bulk.
    deferred: w.deferred ?? false,
  };
}

/** One report table row — cells are pre-formatted strings owned by the BE. */
export function mapReportRow(w: ReportRowWire): IReportRow {
  return { cells: w.cells ?? [] };
}

/** One KPI metric card (US-RPT-003 FR-3). */
export function mapPayrollSummaryMetric(w: PayrollSummaryMetricWire): IPayrollSummaryMetric {
  return {
    // Kept RAW (wire type is `string`); used only as a track/data-test key, never coerced.
    key: (w.key ?? '') as PayrollSummaryMetricKey,
    label: w.label ?? '',
    // Server-computed non-nullable double; the card renders "0" the same as "no value".
    current: w.current ?? 0,
    // SCHEMA-nullable: null == "no prior finalized run". A 0 would fabricate a variance.
    previous: w.previous ?? null,
    variance: w.variance ?? null,
    // Least-claiming: absent must not apply the red "cost increased" semantics.
    isCost: w.isCost ?? false,
  };
}

/** The Payroll Run Summary block (KPI cards + month-over-month bars). */
export function mapPayrollRunSummary(w: PayrollRunSummaryWire): IPayrollRunSummary {
  return {
    currency: w.currency ?? '',
    currentLabel: w.currentLabel ?? '',
    // SCHEMA-nullable: null == no previous period to label on the dual bar chart.
    previousLabel: w.previousLabel ?? null,
    metrics: (w.metrics ?? []).map(mapPayrollSummaryMetric),
  };
}

/** `GET /payroll/reports/{reportType}` → the generic report table + optional KPI summary. */
export function mapReportResult(w: ReportResultWire): IReportResult {
  return {
    // Kept RAW: an unknown report type makes reportHasChart() false (no chart), which is safe.
    reportType: (w.reportType ?? '') as PayrollReportType,
    title: w.title ?? '',
    payMonth: w.payMonth ?? 0,
    payYear: w.payYear ?? 0,
    columns: w.columns ?? [],
    rows: (w.rows ?? []).map(mapReportRow),
    // Absent footer row → null, NOT an empty row (which would render a blank total line).
    totalRow: w.totalRow ? mapReportRow(w.totalRow) : null,
    // Server-computed count; may exceed rows.length when paginated, so it is never derived FE-side.
    totalCount: w.totalCount ?? 0,
    note: w.note ?? null,
    // Absent summary → null: the report renders the plain table with no KPI cards (US-PAY-009 behaviour).
    summary: w.summary ? mapPayrollRunSummary(w.summary) : null,
  };
}

/** One bank-advice line. `accountNumber` arrives masked from /preview and full from /full (BR-2). */
export function mapBankAdviceLine(w: BankAdviceLineWire): IBankAdviceLine {
  return {
    employeeNo: w.employeeNo ?? '',
    employeeName: w.employeeName ?? '',
    bankName: w.bankName ?? '',
    branchCode: w.branchCode ?? '',
    // Never invent an account number: absent renders blank rather than a plausible-looking value.
    accountNumber: w.accountNumber ?? '',
    // Server-computed net pay for the disbursement line.
    netAmount: w.netAmount ?? 0,
    narration: w.narration ?? '',
  };
}

/**
 * `GET /payroll/reports/bank-advice/preview` (masked) and `.../full` (un-masked) share this shape.
 *
 * NOTE: the wire DTO also carries a `masked: boolean` that this view-model has no field for, so the
 * FE decides "masked vs revealed" purely from which method it called. See the OUT-OF-LANE ISSUE
 * raised with this migration — adding the field needs a component change and is out of lane here.
 */
export function mapBankAdvicePreview(w: BankAdvicePreviewWire): IBankAdvicePreview {
  return {
    payMonth: w.payMonth ?? 0,
    payYear: w.payYear ?? 0,
    lines: (w.lines ?? []).map(mapBankAdviceLine),
    employeeCount: w.employeeCount ?? 0,
    totalNetAmount: w.totalNetAmount ?? 0,
    note: w.note ?? null,
  };
}

/** One labelled chart datum. */
export function mapAnalyticsPoint(w: AnalyticsPointWire): IAnalyticsPoint {
  return { label: w.label ?? '', value: w.value ?? 0 };
}

/** One named chart series. */
export function mapAnalyticsSeries(w: AnalyticsSeriesWire): IAnalyticsSeries {
  return { name: w.name ?? '', points: (w.points ?? []).map(mapAnalyticsPoint) };
}

/** `GET /payroll/analytics/{chartType}` → one chart payload. */
export function mapPayrollAnalyticsResult(w: PayrollAnalyticsResultWire): IPayrollAnalyticsResult {
  return {
    // The view-model already widens to `| string`, so the raw wire value passes through untouched.
    chartType: w.chartType ?? '',
    points: (w.points ?? []).map(mapAnalyticsPoint),
    categories: w.categories ?? [],
    series: (w.series ?? []).map(mapAnalyticsSeries),
  };
}
