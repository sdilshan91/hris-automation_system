/**
 * US-PRF-007: Performance Dashboard & Analytics models matching the (ASSUMED)
 * backend API contract. The dashboard aggregates submitted-review data into
 * overview widgets (completion donut, average score, score-distribution histogram,
 * department-wise average bar), top/bottom performer lists, cycle progress metrics
 * (FR-6), a multi-cycle trend (FR-7), drill-down department employee lists (FR-5)
 * and server-generated exports (FR-8).
 *
 * Sibling to the other Performance services. The service layer
 * (`PerformanceDashboardService`) is intentionally thin so a route/DTO mismatch is a
 * one-file fix once the backend lands (reconcile alongside the other Performance
 * stories).
 *
 * ── ASSUMED backend contract (backend agent must confirm/reconcile) ────────────
 * `apiBaseUrl` already includes `/api/v1`. All under `/performance/dashboard`.
 * Tenant + acting user resolved server-side from the session (FE sends no ids); the
 * server enforces the user's SCOPE (BR-1/BR-3/AC-5): `Performance.Read.All` → org-wide
 * (HR), `Performance.Read.Team` → the manager's direct reports only. The payload
 * carries an authoritative `scope` field the FE reflects (it does NOT decide scope
 * client-side beyond the redirect of pure-employee users).
 *
 *   GET  /performance/dashboard/overview
 *        ?cycleId=&departmentId=&grade=&employmentType=&location= (multi → repeated)
 *        → IDashboardOverview — every overview widget in ONE call (AC-1/AC-2/NFR-1):
 *          scope, filterable option lists, completion %, average score, the score
 *          distribution histogram buckets, department averages, top/bottom (or team
 *          ranking) performers, and the cycle progress metrics (FR-6).
 *
 *   GET  /performance/dashboard/trend?cycleId=&cycleId=…&departmentId=… (FR-7/AC-3)
 *        → ITrendResponse — average score per selected cycle (the org/team series)
 *          plus optional per-department overlay series.
 *
 *   GET  /performance/dashboard/departments/{departmentId}?cycleId=… (FR-5)
 *        → IDepartmentDrilldown — the department's employees with individual scores +
 *          breadcrumb labels for "Dashboard > Department > Employee".
 *
 *   GET  /performance/dashboard/export?format=Csv|Excel|Pdf&<same filters>
 *        → HttpResponse<Blob> (Content-Disposition filename). FR-8/AC-4. The server
 *          owns CSV/XLSX/PDF generation + tenant branding on the PDF. The overview's
 *          `availableExportFormats` gates which buttons are shown (PDF only if the
 *          backend reports it available).
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips `{ data }`,
 * so the JSON methods consume BARE payloads. Enums arrive as PascalCase STRINGS
 * (US-PLT-003).
 */

/** Who the dashboard data is scoped to (authoritative, server-decided). */
export type DashboardScope = 'Organization' | 'Team';

/** Export formats the backend can produce (PascalCase, US-PLT-003). */
export type ExportFormat = 'Csv' | 'Excel' | 'Pdf';

/** Direction of a performer's score vs. the previous cycle (trend indicator, FR-3). */
export type PerformerTrend = 'Up' | 'Down' | 'Flat' | 'New';

/** A selectable filter option (id + human label). */
export interface IFilterOption {
  id: string;
  label: string;
}

/** The option lists the filter panel renders (FR-4). */
export interface IDashboardFilterOptions {
  cycles: IFilterOption[];
  departments: IFilterOption[];
  grades: IFilterOption[];
  employmentTypes: IFilterOption[];
  locations: IFilterOption[];
}

/** The currently-applied multi-select filter selection (FR-4). */
export interface IDashboardFilters {
  cycleIds: string[];
  departmentIds: string[];
  grades: string[];
  employmentTypes: string[];
  locations: string[];
}

/** One bucket of the score-distribution histogram (FR-1). */
export interface IHistogramBucket {
  /** Inclusive lower bound of the score band, e.g. 0, 10, 20 … */
  rangeStart: number;
  /** Exclusive upper bound, e.g. 10, 20 … (last bucket may be inclusive of 100). */
  rangeEnd: number;
  /** Display label, e.g. "0–10". */
  label: string;
  count: number;
}

/** Department-wise average performance (FR-2 horizontal bar + FR-5 drill-down). */
export interface IDepartmentAverage {
  departmentId: string;
  departmentName: string;
  averageScore: number | null;
  headcount: number;
}

/** A top/bottom performer (or team-ranking) row (FR-3). */
export interface IPerformerRow {
  employeeId: string;
  employeeName: string;
  departmentName: string;
  jobTitle: string | null;
  score: number | null;
  trend: PerformerTrend;
}

/** Cycle progress metrics (FR-6). */
export interface ICycleProgress {
  totalParticipants: number;
  goalSettingComplete: number;
  selfAssessmentComplete: number;
  managerReviewComplete: number;
  signedOff: number;
}

/** The whole dashboard overview — one payload for every AC-1 widget. */
export interface IDashboardOverview {
  /** Authoritative scope (AC-5/BR-1). The FE reflects this; it does not compute it. */
  scope: DashboardScope;
  /** Label for the scoped subject, e.g. "Acme Corp" (org) or "Jane's team". */
  scopeLabel: string;
  filterOptions: IDashboardFilterOptions;
  /** % of reviews completed → the completion donut (AC-1). 0–100. */
  completionRate: number;
  /** Average performance score across the scoped+filtered population (AC-1). */
  averageScore: number | null;
  /** The scale the scores are on (e.g. 100). Used to compute bar/donut percents. */
  scoreScaleMax: number;
  ratedCount: number;
  histogram: IHistogramBucket[];
  departmentAverages: IDepartmentAverage[];
  /** Top N performers — ORG scope only (BR-3). Empty for a manager. */
  topPerformers: IPerformerRow[];
  /** Bottom N performers — ORG scope only (BR-3). Empty for a manager. */
  bottomPerformers: IPerformerRow[];
  /** Team ranking — manager scope only (BR-3). Empty for org scope. */
  teamRanking: IPerformerRow[];
  cycleProgress: ICycleProgress;
  /** Which export formats the backend can produce now (FR-8 — PDF gated). */
  availableExportFormats: ExportFormat[];
}

/** One point in a trend series (FR-7). */
export interface ITrendPoint {
  cycleId: string;
  cycleLabel: string;
  averageScore: number | null;
}

/** A named line in the trend chart (org/team series, or a department overlay). */
export interface ITrendSeries {
  /** null/empty key = the org/team aggregate series; otherwise a departmentId. */
  key: string | null;
  label: string;
  points: ITrendPoint[];
}

/** Multi-cycle trend response (AC-3/FR-7). */
export interface ITrendResponse {
  series: ITrendSeries[];
  scoreScaleMax: number;
}

/** One employee row in a department drill-down (FR-5). */
export interface IDepartmentEmployeeScore {
  employeeId: string;
  employeeName: string;
  jobTitle: string | null;
  grade: string | null;
  score: number | null;
  trend: PerformerTrend;
}

/** Department drill-down payload (FR-5) — feeds the breadcrumb + employee list. */
export interface IDepartmentDrilldown {
  departmentId: string;
  departmentName: string;
  cycleLabel: string;
  averageScore: number | null;
  scoreScaleMax: number;
  employees: IDepartmentEmployeeScore[];
}

// ─── pure presentation helpers (shared by component + asserted by specs) ───

/** An empty filter selection (no filters applied). */
export function emptyFilters(): IDashboardFilters {
  return {
    cycleIds: [],
    departmentIds: [],
    grades: [],
    employmentTypes: [],
    locations: [],
  };
}

/** True when at least one filter facet has a selection (drives the "clear" CTA). */
export function hasActiveFilters(f: IDashboardFilters): boolean {
  return (
    f.cycleIds.length > 0 ||
    f.departmentIds.length > 0 ||
    f.grades.length > 0 ||
    f.employmentTypes.length > 0 ||
    f.locations.length > 0
  );
}

/** Total number of selected facets (badge count on the collapsed filter toggle). */
export function activeFilterCount(f: IDashboardFilters): number {
  return (
    f.cycleIds.length +
    f.departmentIds.length +
    f.grades.length +
    f.employmentTypes.length +
    f.locations.length
  );
}

/**
 * Bar height/length percent for a score on a 0..scaleMax scale (FR-1/FR-2 visuals).
 * Clamped to 0–100; null score → 0.
 */
export function scorePercent(score: number | null, scaleMax: number): number {
  if (score == null || scaleMax <= 0) {
    return 0;
  }
  return Math.max(0, Math.min(100, (score / scaleMax) * 100));
}

/** Tallest histogram bucket count — used to scale bar heights to the chart area. */
export function histogramPeak(buckets: IHistogramBucket[]): number {
  return buckets.reduce((max, b) => Math.max(max, b.count), 0);
}

/** A histogram bar's height percent relative to the tallest bucket (FR-1). */
export function histogramBarPercent(
  bucket: IHistogramBucket,
  peak: number,
): number {
  if (peak <= 0) {
    return 0;
  }
  return Math.max(2, Math.round((bucket.count / peak) * 100));
}

/**
 * Donut SVG geometry for a 0–100 completion rate (AC-1). Returns the
 * stroke-dasharray + dashoffset for a circle of the given radius so the template
 * stays declarative. Two arcs (filled + track) draw the donut with pure SVG — no
 * chart library (consistent with US-PRF-003/004/005, see the no-chart-lib memory).
 */
export function donutGeometry(
  ratePercent: number,
  radius: number,
): { circumference: number; dashOffset: number } {
  const pct = Math.max(0, Math.min(100, ratePercent));
  const circumference = 2 * Math.PI * radius;
  const dashOffset = circumference * (1 - pct / 100);
  return { circumference, dashOffset };
}

/** Tailwind classes for a performer trend indicator (FR-3). */
export const PERFORMER_TREND_CLASSES: Record<PerformerTrend, string> = {
  Up: 'text-emerald-600',
  Down: 'text-rose-600',
  Flat: 'text-neutral-400',
  New: 'text-sky-600',
};

/** Glyph for a performer trend indicator (FR-3). */
export const PERFORMER_TREND_GLYPH: Record<PerformerTrend, string> = {
  Up: '▲',
  Down: '▼',
  Flat: '–',
  New: '∗',
};

/**
 * Build an inline-SVG polyline `points` string for a trend series over an evenly
 * spaced X axis (AC-3/FR-7). The chart viewBox is `0 0 width height`; null scores are
 * skipped (the polyline only connects rated cycles). Returns "" when <1 point.
 */
export function trendPolylinePoints(
  series: ITrendSeries,
  scaleMax: number,
  width: number,
  height: number,
  pad = 6,
): string {
  const pts = series.points;
  if (pts.length === 0 || scaleMax <= 0) {
    return '';
  }
  const innerW = width - pad * 2;
  const innerH = height - pad * 2;
  const step = pts.length > 1 ? innerW / (pts.length - 1) : 0;
  return pts
    .map((p, i) => {
      if (p.averageScore == null) {
        return null;
      }
      const x = pad + step * i;
      const ratio = Math.max(0, Math.min(1, p.averageScore / scaleMax));
      const y = pad + innerH * (1 - ratio);
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .filter((s): s is string => s !== null)
    .join(' ');
}

/** Ordered, deterministic palette for trend overlay lines (no chart lib). */
export const TREND_SERIES_COLORS = [
  '#404040', // neutral-700 — primary org/team line
  '#2563eb', // blue-600
  '#059669', // emerald-600
  '#d97706', // amber-600
  '#7c3aed', // violet-600
  '#dc2626', // red-600
];

/** Stable color for the Nth trend series. */
export function trendSeriesColor(index: number): string {
  return TREND_SERIES_COLORS[index % TREND_SERIES_COLORS.length];
}
