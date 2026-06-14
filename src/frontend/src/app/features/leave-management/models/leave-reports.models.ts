/**
 * US-LV-012: Leave Reports & Analytics — models, chart shapes, and pure helpers.
 *
 * Backend endpoints (backend agent building in parallel — assumed contract,
 * RECONCILE against docs/vault/modules/leave-management.md "Frontend (US-LV-012)"):
 *
 *   GET /api/v1/leaves/reports/{reportType}?from&to&departmentId&jobLevel&
 *        employmentType&leaveTypeId&search&page&pageSize&sortBy&sortDir
 *        -> IReportPage  ({ items, totalCount })                         (FR-6)
 *   GET /api/v1/leaves/analytics/{chartType}?<same filter subset>
 *        -> IAnalyticsResponse (chart-shaped aggregates)                 (FR-7)
 *   GET /api/v1/leaves/reports/{reportType}/export?format=csv|xlsx&<filters>
 *        -> file download OR { jobId, status:'processing' } for large sets (FR-4/AC-5)
 *
 * NOTE: `apiBaseUrl` already includes `/api/v1`, so the resource base is
 * `${apiBaseUrl}/leaves`. All report rows are returned by the backend already
 * tenant-isolated (BR-1) and role-scoped (BR-2); the FE only renders.
 */

// ─── Report identity ─────────────────────────────────────────

/** The pre-built report types (FR-1). Used as the `{reportType}` path segment. */
export type ReportType =
  | 'balance-summary'
  | 'utilization'
  | 'absenteeism'
  | 'trend-analysis'
  | 'carry-forward-summary'
  | 'lop-summary';

/** Catalog entry for the landing-page report-card grid (§8). */
export interface IReportCard {
  type: ReportType;
  /** Display title. */
  title: string;
  /** One-line description shown on the card. */
  description: string;
  /** Heroicon-style inline SVG path data (rendered in the card icon chip). */
  iconPath: string;
  /** Whether this report renders charts in its detail view (utilization/absenteeism/trend). */
  hasCharts: boolean;
}

/**
 * The six pre-built reports surfaced on the landing page (FR-1). The seventh
 * story item ("Department Leave Calendar Coverage") is DEFERRED — it overlaps the
 * US-LV-009 team-calendar and has no analytics contract here.
 */
export const REPORT_CATALOG: IReportCard[] = [
  {
    type: 'balance-summary',
    title: 'Leave Balance Summary',
    description: 'Current balance per employee for every leave type. Filter, sort and export.',
    iconPath: 'M3 7h18M3 12h18M3 17h18',
    hasCharts: false,
  },
  {
    type: 'utilization',
    title: 'Leave Utilization',
    description: 'Total leaves taken by type and department with average utilization %.',
    iconPath: 'M4 19V5m0 14h16M8 17v-5m4 5V8m4 9v-3',
    hasCharts: true,
  },
  {
    type: 'absenteeism',
    title: 'Absenteeism',
    description: 'Top-absentee employees, trend lines and threshold-flagged outliers.',
    iconPath: 'M12 9v4m0 4h.01M10.3 3.9l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.7-3.1l-8-14a2 2 0 0 0-3.4 0z',
    hasCharts: true,
  },
  {
    type: 'trend-analysis',
    title: 'Leave Trend Analysis',
    description: 'Monthly leave trends by type over 12 months with year-over-year comparison.',
    iconPath: 'M3 17l6-6 4 4 8-8M21 7h-5m5 0v5',
    hasCharts: true,
  },
  {
    type: 'carry-forward-summary',
    title: 'Carry-Forward Summary',
    description: 'Projected carry-forward and forfeiture per employee at year close.',
    iconPath: 'M4 4v6h6M20 20v-6h-6M20 8a8 8 0 0 0-14.3-3M4 16a8 8 0 0 0 14.3 3',
    hasCharts: false,
  },
  {
    type: 'lop-summary',
    title: 'LOP Summary',
    description: 'Loss-of-pay days by employee and source across the selected period.',
    iconPath: 'M12 1v22M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6',
    hasCharts: false,
  },
];

/** Lookup a card by report type (returns undefined for an unknown type). */
export function findReportCard(type: string): IReportCard | undefined {
  return REPORT_CATALOG.find((c) => c.type === type);
}

// ─── Filters (FR-2) ──────────────────────────────────────────

/** Employment-type options for the filter (mirrors the US-LV-002 dimension). */
export type EmploymentType = 'FullTime' | 'PartTime' | 'Contract' | 'Intern';

export const EMPLOYMENT_TYPE_OPTIONS: { value: EmploymentType; label: string }[] = [
  { value: 'FullTime', label: 'Full-time' },
  { value: 'PartTime', label: 'Part-time' },
  { value: 'Contract', label: 'Contract' },
  { value: 'Intern', label: 'Intern' },
];

/** The full set of report filters (FR-2). All optional. */
export interface IReportFilters {
  from?: string | null; // 'YYYY-MM-DD'
  to?: string | null; // 'YYYY-MM-DD'
  departmentId?: string | null;
  jobLevel?: string | null;
  employmentType?: EmploymentType | null;
  leaveTypeId?: string | null;
  search?: string | null; // employee name/number search
}

/** Empty filter object used to reset the sidebar. */
export function emptyFilters(): IReportFilters {
  return {
    from: null,
    to: null,
    departmentId: null,
    jobLevel: null,
    employmentType: null,
    leaveTypeId: null,
    search: null,
  };
}

/** True when at least one filter is set (drives the "Clear filters" affordance). */
export function hasActiveFilters(f: IReportFilters): boolean {
  return Boolean(
    f.from || f.to || f.departmentId || f.jobLevel || f.employmentType || f.leaveTypeId || f.search,
  );
}

// ─── Pagination + sorting (FR-3) ─────────────────────────────

export type SortDir = 'asc' | 'desc';

export interface IReportQuery extends IReportFilters {
  page: number; // 1-based
  pageSize: number;
  sortBy?: string | null;
  sortDir?: SortDir;
}

/** Server-side report page envelope (FR-6). */
export interface IReportPage {
  items: IReportRow[];
  totalCount: number;
}

/**
 * A report row is an open record — the backend returns different columns per
 * report type. The FE renders columns from the `IReportColumn[]` metadata so a
 * single table component serves every tabular report.
 */
export type IReportRow = Record<string, string | number | boolean | null>;

/** Column metadata that drives the generic sortable table. */
export interface IReportColumn {
  /** Property key on the row. */
  key: string;
  /** Header label. */
  label: string;
  /** Cell alignment. */
  align?: 'left' | 'right' | 'center';
  /** Whether the column is server-sortable (sends `sortBy=key`). */
  sortable?: boolean;
  /** Render hint: 'flag' shows a threshold indicator for truthy values (AC-3). */
  kind?: 'text' | 'number' | 'flag';
}

/**
 * Column definitions per tabular report. The chart-only reports (utilization,
 * absenteeism, trend) ALSO ship a table beneath the chart, so they have columns
 * too. Keys must match the backend row property names (camelCase).
 */
export const REPORT_COLUMNS: Record<ReportType, IReportColumn[]> = {
  'balance-summary': [
    { key: 'employeeName', label: 'Employee', sortable: true },
    { key: 'departmentName', label: 'Department', sortable: true },
    { key: 'leaveTypeName', label: 'Leave Type', sortable: true },
    { key: 'entitlement', label: 'Entitlement', align: 'right', kind: 'number', sortable: true },
    { key: 'used', label: 'Used', align: 'right', kind: 'number', sortable: true },
    { key: 'balance', label: 'Balance', align: 'right', kind: 'number', sortable: true },
  ],
  utilization: [
    { key: 'departmentName', label: 'Department', sortable: true },
    { key: 'leaveTypeName', label: 'Leave Type', sortable: true },
    { key: 'totalDays', label: 'Days Taken', align: 'right', kind: 'number', sortable: true },
    { key: 'entitlementDays', label: 'Entitlement', align: 'right', kind: 'number', sortable: true },
    { key: 'utilizationPct', label: 'Utilization %', align: 'right', kind: 'number', sortable: true },
  ],
  absenteeism: [
    { key: 'employeeName', label: 'Employee', sortable: true },
    { key: 'departmentName', label: 'Department', sortable: true },
    { key: 'unplannedDays', label: 'Unplanned', align: 'right', kind: 'number', sortable: true },
    { key: 'lopDays', label: 'LOP', align: 'right', kind: 'number', sortable: true },
    { key: 'absenteeismDays', label: 'Total', align: 'right', kind: 'number', sortable: true },
    { key: 'flagged', label: 'Flagged', align: 'center', kind: 'flag', sortable: true },
  ],
  'trend-analysis': [
    { key: 'month', label: 'Month', sortable: true },
    { key: 'leaveTypeName', label: 'Leave Type', sortable: true },
    { key: 'totalDays', label: 'Days', align: 'right', kind: 'number', sortable: true },
    { key: 'priorYearDays', label: 'Prior Year', align: 'right', kind: 'number', sortable: true },
  ],
  'carry-forward-summary': [
    { key: 'employeeName', label: 'Employee', sortable: true },
    { key: 'departmentName', label: 'Department', sortable: true },
    { key: 'leaveTypeName', label: 'Leave Type', sortable: true },
    { key: 'projectedCarryForward', label: 'Carry-forward', align: 'right', kind: 'number', sortable: true },
    { key: 'projectedForfeiture', label: 'Forfeited', align: 'right', kind: 'number', sortable: true },
  ],
  'lop-summary': [
    { key: 'employeeName', label: 'Employee', sortable: true },
    { key: 'departmentName', label: 'Department', sortable: true },
    { key: 'source', label: 'Source', sortable: true },
    { key: 'lopDays', label: 'LOP Days', align: 'right', kind: 'number', sortable: true },
  ],
};

// ─── Analytics / chart shapes (FR-7) ─────────────────────────

/** A single labelled value (bar / pie slice). */
export interface IChartDatum {
  label: string;
  value: number;
  /** Optional muted color override; otherwise the palette is used. */
  color?: string | null;
}

/** A named series of points over a shared x-axis (line chart). */
export interface IChartSeries {
  name: string;
  /** Per-x-axis values, aligned to `IAnalyticsResponse.categories`. */
  values: number[];
  color?: string | null;
}

/**
 * Chart-shaped aggregate returned by the analytics endpoint (FR-7). A bar/pie
 * chart populates `data`; a multi-series line chart populates `categories` +
 * `series`. The backend includes whichever shape fits the chartType.
 */
export interface IAnalyticsResponse {
  /** For bar/pie: the slices/bars. */
  data?: IChartDatum[];
  /** For line: the shared x-axis labels (e.g. months). */
  categories?: string[];
  /** For line: one entry per leave type / comparison series. */
  series?: IChartSeries[];
}

/** Which analytics chart a report-detail view requests. */
export type ChartType =
  | 'utilization-by-department'
  | 'utilization-by-type'
  | 'absenteeism-trend'
  | 'monthly-trend';

// ─── Dashboard summary widgets (AC cards, §8) ────────────────

/** Key leave metrics shown as summary cards atop the landing page. */
export interface ILeaveSummaryMetrics {
  /** Total utilization across the tenant for the period, as a percentage. */
  totalUtilizationPct: number;
  /** The most-used leave type by days taken. */
  topLeaveType: string;
  /** Absenteeism rate (unplanned + LOP days / total working days) as a percentage. */
  absenteeismRatePct: number;
}

// ─── Export (FR-4 / AC-5) ────────────────────────────────────

export type ExportFormat = 'csv' | 'xlsx';

/**
 * Export response. A synchronous export returns the file as a Blob (handled by
 * the service, not typed here). For large datasets (>5,000 rows, AC-5) the
 * backend returns this JSON envelope indicating a background Hangfire job.
 *
 * DEFER (seam only): real background-export polling / blob download. The FE shows
 * a "processing — you'll be notified" state from `status:'processing'` and does
 * NOT poll. TODO(export-polling) in the service.
 */
export interface IExportJobResponse {
  status: 'ready' | 'processing';
  jobId?: string | null;
  /** Optional direct download URL when status is 'ready' (small synchronous case). */
  downloadUrl?: string | null;
  /** Optional row count the backend used to decide sync vs background. */
  rowCount?: number | null;
}

/** Error envelope surfaced via toast (module-wide contract). */
export interface IReportErrorResponse {
  message: string;
  code?: string;
}

// ─── Pure helpers (unit-tested) ──────────────────────────────

/** Muted, Notion/Linear-style chart palette (NFR-4). Cycled by index. */
export const CHART_PALETTE: string[] = [
  '#6366f1', // indigo-500
  '#0ea5e9', // sky-500
  '#14b8a6', // teal-500
  '#f59e0b', // amber-500
  '#ec4899', // pink-500
  '#8b5cf6', // violet-500
  '#84cc16', // lime-500
  '#f43f5e', // rose-500
];

/** Resolve a stable palette color for a series/datum index. */
export function paletteColor(index: number): string {
  return CHART_PALETTE[index % CHART_PALETTE.length];
}

/**
 * Total number of pages for a page size + total count (>= 1 so the pager always
 * renders at least one page).
 */
export function totalPages(totalCount: number, pageSize: number): number {
  if (pageSize <= 0) {
    return 1;
  }
  return Math.max(1, Math.ceil(totalCount / pageSize));
}

/**
 * Build SVG bar-chart geometry for a set of data. Pure so it is unit-testable
 * (asserts scaling). Returns one rect spec per datum plus the value-axis max.
 * Bars are laid out left-to-right within `width`×`height` with a small gap.
 */
export interface IBarGeom {
  x: number;
  y: number;
  width: number;
  height: number;
  color: string;
  datum: IChartDatum;
}

export function buildBarGeometry(
  data: IChartDatum[],
  width: number,
  height: number,
  gap = 8,
): { bars: IBarGeom[]; max: number } {
  const max = data.reduce((m, d) => Math.max(m, d.value), 0);
  if (data.length === 0) {
    return { bars: [], max: 0 };
  }
  const slot = width / data.length;
  const barWidth = Math.max(1, slot - gap);
  const bars: IBarGeom[] = data.map((d, i) => {
    const h = max > 0 ? (d.value / max) * height : 0;
    return {
      x: i * slot + gap / 2,
      y: height - h,
      width: barWidth,
      height: h,
      color: d.color ?? paletteColor(i),
      datum: d,
    };
  });
  return { bars, max };
}

/**
 * Build SVG pie-chart slices (cumulative-angle arcs) for a set of data. Pure +
 * unit-tested. Returns an SVG path `d` per slice over a unit circle of `radius`
 * centered at (`cx`,`cy`). A single non-zero datum yields a full circle path.
 */
export interface IPieSlice {
  path: string;
  color: string;
  datum: IChartDatum;
  /** Percentage of the whole (0–100), for the legend. */
  percent: number;
}

export function buildPieSlices(
  data: IChartDatum[],
  cx: number,
  cy: number,
  radius: number,
): IPieSlice[] {
  const total = data.reduce((s, d) => s + Math.max(0, d.value), 0);
  if (total <= 0) {
    return [];
  }
  let angle = -Math.PI / 2; // start at 12 o'clock
  return data.map((d, i) => {
    const frac = Math.max(0, d.value) / total;
    const sweep = frac * Math.PI * 2;
    const end = angle + sweep;
    const x1 = cx + radius * Math.cos(angle);
    const y1 = cy + radius * Math.sin(angle);
    const x2 = cx + radius * Math.cos(end);
    const y2 = cy + radius * Math.sin(end);
    const largeArc = sweep > Math.PI ? 1 : 0;
    // A single full-circle slice can't be drawn with one arc; split visually by
    // drawing a near-full circle is acceptable, but we special-case 100%.
    const path =
      frac >= 0.999
        ? `M ${cx} ${cy - radius} A ${radius} ${radius} 0 1 1 ${cx - 0.01} ${cy - radius} Z`
        : `M ${cx} ${cy} L ${x1} ${y1} A ${radius} ${radius} 0 ${largeArc} 1 ${x2} ${y2} Z`;
    angle = end;
    return { path, color: d.color ?? paletteColor(i), datum: d, percent: frac * 100 };
  });
}

/**
 * Build SVG polyline points for a line series scaled into `width`×`height`.
 * Pure + unit-tested (asserts the y inverts correctly — higher value = smaller y).
 * `globalMax` lets multiple series share one y-scale.
 */
export function buildLinePoints(
  values: number[],
  width: number,
  height: number,
  globalMax: number,
): { x: number; y: number }[] {
  if (values.length === 0) {
    return [];
  }
  const max = globalMax > 0 ? globalMax : 1;
  const stepX = values.length > 1 ? width / (values.length - 1) : 0;
  return values.map((v, i) => ({
    x: values.length > 1 ? i * stepX : width / 2,
    y: height - (v / max) * height,
  }));
}

/** The maximum value across all series (shared line-chart y-scale). */
export function seriesMax(series: IChartSeries[]): number {
  let max = 0;
  for (const s of series) {
    for (const v of s.values) {
      if (v > max) {
        max = v;
      }
    }
  }
  return max;
}

/** Format a number as `points`-string for an SVG polyline `points` attribute. */
export function pointsToString(points: { x: number; y: number }[]): string {
  return points.map((p) => `${round2(p.x)},${round2(p.y)}`).join(' ');
}

function round2(n: number): number {
  return Math.round(n * 100) / 100;
}
