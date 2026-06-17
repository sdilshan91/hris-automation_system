/**
 * US-RPT-001: Pre-Built HR Reports — frontend contract.
 *
 * The backend (Features/Reports) exposes a GENERIC report shape so all six
 * pre-built report types render through ONE viewer. Every report returns:
 *   - metadata (title, generatedAt, filters echo, optional summary stats)
 *   - one or more chart blocks (typed by kind: bar | line | pie | histogram | stacked-bar)
 *   - a tabular block (columns + rows) which doubles as the screen-reader
 *     alternative to the charts (NFR-5 / WCAG 2.1 AA).
 *
 * TENANT-scoped endpoints under `/api/v1/reports...` (NOT the
 * system-admin console). Tenant isolation is enforced server-side via
 * ITenantContext + EF global filters (AC-5); the FE just carries the auth
 * cookie + the X-Tenant-Subdomain header (added by the tenantInterceptor).
 * Services consume BARE payloads (no ApiResponse unwrap), matching the rest
 * of the FE.
 */

// ─── Catalog (AC-1) ───────────────────────────────────────────────────────

/**
 * Stable keys for the pre-built reports (FR-1). The first six are the US-RPT-001
 * HR reports; the next six are the US-RPT-002 leave & attendance reports. The
 * catalog is server-driven — these keys must match the backend's report keys
 * byte-for-byte so the FE can derive the i18n title/description keys from `type`.
 */
export type ReportType =
  // ── US-RPT-001 HR reports ──
  | 'headcount'
  | 'turnover'
  | 'demographics'
  | 'joiners-leavers'
  | 'department-distribution'
  | 'employment-type-breakdown'
  // ── US-RPT-002 leave & attendance reports ──
  | 'leave-utilization'
  | 'leave-balance'
  | 'attendance-summary'
  | 'absenteeism-trends'
  | 'overtime-report'
  | 'late-arrival-report';

/**
 * Optional grouping for catalog cards (US-RPT-002 §8). Server may tag a report
 * with a `category` so the catalog can render light section groups (e.g.
 * "Leave & Attendance" vs "HR Reports"); absent → a single ungrouped grid.
 */
export type ReportCategory = string;

/**
 * The raw catalog item as sent by the backend (`GET /api/v1/reports`). The
 * server owns only the report `type` + the icon token; the FE owns i18n and
 * derives the title/description translation keys from `type`.
 */
export interface IReportCatalogServerItem {
  type: ReportType;
  /** Material icon token, e.g. 'groups', 'trending_down', 'diversity_3'. */
  icon: string;
  /**
   * Optional grouping key for the catalog UI (US-RPT-002 §8), e.g.
   * 'leave-attendance' or 'hr'. When present the catalog renders one section
   * per category; when absent (or all items share none) it falls back to a
   * single grid. The FE derives the section heading i18n key from this value.
   */
  category?: ReportCategory;
}

/**
 * A single card in the report catalog (AC-1), as consumed by the catalog
 * component. Built in the service from {@link IReportCatalogServerItem} by
 * deriving the i18n keys from `type`.
 */
export interface IReportCatalogItem {
  type: ReportType;
  /** i18n key for the title, e.g. 'reports.catalog.headcount.title'. */
  titleKey: string;
  /** i18n key for the description. */
  descriptionKey: string;
  /** Material icon token rendered directly by <mat-icon>. */
  icon: string;
  /** Optional grouping key echoed from the server (US-RPT-002 §8). */
  category?: ReportCategory;
}

// ─── Filters (FR-2 / §7) ────────────────────────────────────────────────────

/** Filter inputs shared by all report types (FR-2). Sent as the request body. */
export interface IReportFilters {
  /** Defaults to start of current month server-side when omitted. */
  dateFrom: string | null;
  /** Defaults to today server-side when omitted. */
  dateTo: string | null;
  /** Department ids (multi-select with hierarchy). */
  departmentIds: string[];
  locationIds: string[];
  /** Valid employment-type values, e.g. 'full-time'. */
  employmentTypes: string[];
  /** Valid status values, e.g. 'active'. */
  employeeStatuses: string[];
  /**
   * US-RPT-002 FR-2: leave & attendance report filters. All optional and
   * additive — the backend's HrReportQueryParams ignores those that don't apply
   * to a given report type, so they are always safe to send. HR reports that
   * predate US-RPT-002 simply leave these empty/null.
   */
  /** Single employee focus (FR-2 employee search). */
  employeeId: string | null;
  /** Leave-type ids to scope leave reports (FR-2). */
  leaveTypeIds: string[];
  /** Shift ids to scope attendance reports (FR-2). */
  shiftIds: string[];
}

/** Build an empty filter set (all-inclusive — server applies defaults). */
export function emptyReportFilters(): IReportFilters {
  return {
    dateFrom: null,
    dateTo: null,
    departmentIds: [],
    locationIds: [],
    employmentTypes: [],
    employeeStatuses: [],
    employeeId: null,
    leaveTypeIds: [],
    shiftIds: [],
  };
}

// ─── Generic report payload ─────────────────────────────────────────────────

/** Supported chart kinds (FR-3). */
export type ChartKind = 'bar' | 'horizontal-bar' | 'line' | 'pie' | 'histogram' | 'stacked-bar';

/**
 * A single named data point. For multi-series (stacked) charts the same label
 * appears once per series; the wrapper groups by series.
 */
export interface IChartDataPoint {
  /** Category/x-axis label, e.g. a sub-department or month. */
  label: string;
  /** Numeric value. */
  value: number;
}

/**
 * One chart block. `series` is a map of series-name → points so a single block
 * can describe a single-series chart (one entry) or a multi-series / stacked
 * chart (many entries). Labels are derived from the union of point labels in
 * series order.
 */
export interface IReportChart {
  kind: ChartKind;
  /** i18n key OR literal title for the chart (rendered as-is if no key match). */
  title: string;
  /** Ordered series; key = series name, value = its points. */
  series: IReportSeries[];
  /** Optional axis labels (literal). */
  xAxisLabel?: string;
  yAxisLabel?: string;
}

export interface IReportSeries {
  name: string;
  points: IChartDataPoint[];
}

/**
 * US-RPT-002 AC-2: leave-balance color band. The backend computes the
 * remaining-percentage band per cell so the FE colors generically without
 * hardcoding the threshold logic per report type:
 *   ok   → green  (> 50% remaining)
 *   warn → yellow (25–50%)
 *   low  → red    (< 25%)
 */
export type BalanceBand = 'ok' | 'warn' | 'low';

/** Tabular block — the WCAG data-table alternative to the charts (NFR-5). */
export interface IReportTable {
  columns: string[];
  rows: ReportCell[][];
  /**
   * US-RPT-002 AC-2: optional per-cell color bands aligned to {@link rows}
   * (same row/column indices). Present only for reports that carry band data
   * (e.g. Leave Balance); a `null` entry means "no band — render plainly".
   * Absent entirely for every other report, so the viewer degrades gracefully.
   */
  cellBands?: (BalanceBand | null)[][];
}

export type ReportCell = string | number | null;

/** Report metadata (echoes applied filters + summary stats). */
export interface IReportMetadata {
  type: ReportType;
  /** Resolved/echoed title (server may localize or pass literal). */
  title: string;
  generatedAt: string;
  appliedFilters: IReportFilters;
  /**
   * Optional headline KPIs shown above the charts (e.g. total headcount,
   * turnover %). Free-form label/value pairs so any report can surface its
   * own top-line numbers without a bespoke shape.
   */
  summary: IReportSummaryStat[];
  /**
   * True when this result was served from the Redis cache rather than freshly
   * generated (FR-8). Optional — surfaced in the Refresh UX where useful.
   */
  fromCache?: boolean;
}

export interface IReportSummaryStat {
  /** i18n key OR literal label. */
  label: string;
  value: string | number;
  /** Optional emphasis hint for styling (neutral default). */
  tone?: 'neutral' | 'positive' | 'negative';
}

/** The full generic report payload returned by the generate endpoint. */
export interface IReportResult {
  metadata: IReportMetadata;
  charts: IReportChart[];
  table: IReportTable;
}
