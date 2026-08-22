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

import type { Schema } from '@core/api';

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

// ─── Wire contract → view-model mappers (US-PRF-007 D-perf slice 2) ────────────
//
// The three read responses are the GENERATED contract types, not hand-written
// guesses. The service maps every wire payload through here so a renamed C# property
// becomes a TypeScript compile error rather than a silent `undefined` on screen.
//
// **Why this was migrated.** The overview view-model diverged from the wire on almost
// every scalar: the FE read `scoreScaleMax`/`ratedCount`/`histogram`/`cycleProgress`
// and a top-level `completionRate`, while the API sends `ratingScaleMax`/
// `scoredEmployeeCount`/`scoreDistribution`/`progress` (with `completionRate` NESTED
// under `progress`) and the phase-completion counts are `*Completed`, not `*Complete`.
// The service cast the raw body straight to `IDashboardOverview`, so the completion
// donut, the "N rated", the histogram, the department bars and the cycle-progress
// metrics all rendered blank/undefined. Mapping through the generated type turns each
// of those into a compile error instead of a runtime blank.
//
// Fields with NO wire source are set to a null/empty/marked default here and reported
// (see the D-perf slice-2 report) — this file is the single place a backend addition
// would be wired in. Do NOT invent values for them.

export type DashboardOverviewWire = Schema<'PerformancePerformanceDashboardDto'>;
export type CycleProgressWire = Schema<'PerformanceCycleProgressDto'>;
export type ScoreBucketWire = Schema<'PerformanceScoreDistributionBucketDto'>;
export type DepartmentAverageWire = Schema<'PerformanceDepartmentAverageDto'>;
export type PerformerWire = Schema<'PerformancePerformerDto'>;
export type TrendWire = Schema<'PerformancePerformanceTrendDto'>;
export type CycleTrendPointWire = Schema<'PerformanceCycleTrendPointDto'>;
export type DepartmentTrendSeriesWire =
  Schema<'PerformanceDepartmentTrendSeriesDto'>;
export type DepartmentDrilldownWire = Schema<'PerformanceDepartmentDrilldownDto'>;
export type DepartmentEmployeeScoreWire =
  Schema<'PerformanceDepartmentEmployeeScoreDto'>;

/**
 * Fallback score-scale for the TREND and DRILL-DOWN surfaces. NOTE (finding): neither
 * `PerformancePerformanceTrendDto` nor `PerformanceDepartmentDrilldownDto` carries a
 * score-scale field, yet both are rendered against one (the polyline / the score bars).
 * The overview supplies `ratingScaleMax`; these two do not, so this constant stands in
 * until the backend adds the field (or the caller threads the overview's scale). The
 * dashboard scores share the cycle rating scale (default 5), so that is the fallback.
 */
export const DASHBOARD_SCALE_FALLBACK = 5;

/** Maps the wire cycle-progress block onto `ICycleProgress` (`*Completed` → `*Complete`). */
export function mapCycleProgress(
  w: CycleProgressWire | undefined,
): ICycleProgress {
  return {
    totalParticipants: w?.totalParticipants ?? 0,
    goalSettingComplete: w?.goalSettingCompleted ?? 0,
    selfAssessmentComplete: w?.selfAssessmentCompleted ?? 0,
    managerReviewComplete: w?.managerReviewCompleted ?? 0,
    signedOff: w?.signedOff ?? 0,
  };
}

/** Maps one wire histogram bucket onto `IHistogramBucket`. */
export function mapHistogramBucket(w: ScoreBucketWire): IHistogramBucket {
  return {
    rangeStart: w.rangeStart ?? 0,
    rangeEnd: w.rangeEnd ?? 0,
    label: w.label ?? '',
    count: w.count ?? 0,
  };
}

/** Maps one wire department-average row onto `IDepartmentAverage`. */
export function mapDepartmentAverage(
  w: DepartmentAverageWire,
): IDepartmentAverage {
  return {
    departmentId: w.departmentId ?? '',
    departmentName: w.departmentName ?? '',
    averageScore: w.averageScore ?? null,
    headcount: w.headcount ?? 0,
  };
}

/**
 * Maps one wire performer onto `IPerformerRow`. NOTE (finding): the wire
 * `PerformancePerformerDto` has NO `jobTitle` and NO `trend`, but the performer list
 * renders both (job title conditionally; the trend glyph always). `jobTitle` → null
 * (the `@if` hides it); `trend` → 'Flat' (a neutral glyph). Reported — not invented.
 */
export function mapPerformer(w: PerformerWire): IPerformerRow {
  return {
    employeeId: w.employeeId ?? '',
    employeeName: w.employeeName ?? '',
    departmentName: w.departmentName ?? '',
    jobTitle: null,
    score: w.score ?? null,
    trend: 'Flat',
  };
}

/**
 * Maps the whole overview wire payload onto `IDashboardOverview`.
 *
 * NOTE (findings): fields that have no wire source are defaulted + reported here, not invented.
 * This list was WRONG about one of them, so it is worth stating precisely which is which:
 *   • `teamRanking`       → NOT a wire gap. FIXED (ISSUE-379). The API sends it: in Team scope the
 *                           server's `topPerformers` IS the ranking (BR-3 leaves `bottomPerformers`
 *                           empty for a manager). This mapper was discarding it, which is why the
 *                           widget rendered blank — a FRONTEND bug filed for four weeks as a missing
 *                           backend field.
 *   • `scopeLabel`        → '' (the scope subtitle renders blank) — genuinely absent from the wire.
 *   • `filterOptions`     → empty lists (the FR-4 filter panel has no options) — genuinely absent.
 *   • `availableExportFormats` → [] (no export buttons render) — genuinely absent from THIS payload,
 *                           though the export endpoint itself exists and accepts csv/xlsx/pdf.
 *
 * The lesson worth keeping: "the API never sends it" is a claim about the API, and it needs checking
 * against the API. Three of the four here were right; one was a mapper bug wearing a backend label.
 * The renames that WERE broken (`ratingScaleMax`/`scoredEmployeeCount`/
 * `scoreDistribution`/`progress`, nested `completionRate`) are fixed below.
 */
export function mapDashboardOverview(
  w: DashboardOverviewWire,
): IDashboardOverview {
  return {
    scope: (w.scope ?? 'Organization') as DashboardScope,
    scopeLabel: '',
    filterOptions: {
      cycles: [],
      departments: [],
      grades: [],
      employmentTypes: [],
      locations: [],
    },
    completionRate: w.progress?.completionRate ?? 0,
    averageScore: w.averageScore ?? null,
    scoreScaleMax: w.ratingScaleMax ?? 0,
    ratedCount: w.scoredEmployeeCount ?? 0,
    histogram: (w.scoreDistribution ?? []).map(mapHistogramBucket),
    departmentAverages: (w.departmentAverages ?? []).map(mapDepartmentAverage),
    topPerformers: (w.topPerformers ?? []).map(mapPerformer),
    bottomPerformers: (w.bottomPerformers ?? []).map(mapPerformer),
    // ISSUE-379: this was hardcoded `[]`, and the ledger recorded it as a BACKEND gap — a field the API
    // "has never sent". It sends it. In Team scope the server's topPerformers IS the team ranking, and
    // BR-3 deliberately leaves bottomPerformers empty for a manager
    // (`PerformanceDashboardService.cs:670-672`). The data was on the wire the whole time; the MAPPER
    // discarded it, so the Team-ranking widget rendered blank.
    //
    // Derived from the server's own `scope` rather than recomputed: the FE reflects scope, it does not
    // decide it (AC-5/BR-1), and the template gates this widget on the same value.
    teamRanking:
      (w.scope ?? 'Organization') === 'Team' ? (w.topPerformers ?? []).map(mapPerformer) : [],
    cycleProgress: mapCycleProgress(w.progress),
    availableExportFormats: [],
  };
}

/** Maps one wire trend point onto `ITrendPoint` (`cycleName` → `cycleLabel`). */
export function mapTrendPoint(w: CycleTrendPointWire): ITrendPoint {
  return {
    cycleId: w.cycleId ?? '',
    cycleLabel: w.cycleName ?? '',
    averageScore: w.averageScore ?? null,
  };
}

/**
 * Maps the wire trend payload onto `ITrendResponse`. The wire keeps the aggregate
 * (`points`) and the per-department overlays (`departmentSeries`) apart; the FE flattens
 * them into one `series` list where the first, keyless series is the org/team aggregate.
 * NOTE (finding): the wire carries NO score-scale, so `scoreScaleMax` falls back to
 * `DASHBOARD_SCALE_FALLBACK` (reported).
 */
export function mapTrendResponse(w: TrendWire): ITrendResponse {
  const aggregate: ITrendSeries = {
    key: null,
    label: w.scope === 'Team' ? 'Team' : 'Organization',
    points: (w.points ?? []).map(mapTrendPoint),
  };
  const overlays: ITrendSeries[] = (w.departmentSeries ?? []).map((d) => ({
    key: d.departmentId ?? null,
    label: d.departmentName ?? '',
    points: (d.points ?? []).map(mapTrendPoint),
  }));
  return {
    series: [aggregate, ...overlays],
    scoreScaleMax: DASHBOARD_SCALE_FALLBACK,
  };
}

/**
 * Maps one wire drill-down employee onto `IDepartmentEmployeeScore`. NOTE (finding):
 * the wire has no `grade` and no `trend` (it carries a review `status` string instead),
 * yet both are rendered — `grade` conditionally (`@if` hides null) and the trend glyph
 * always. `grade` → null, `trend` → 'Flat'. Reported — not invented.
 */
export function mapDepartmentEmployeeScore(
  w: DepartmentEmployeeScoreWire,
): IDepartmentEmployeeScore {
  return {
    employeeId: w.employeeId ?? '',
    employeeName: w.employeeName ?? '',
    jobTitle: w.jobTitle ?? null,
    grade: null,
    score: w.score ?? null,
    trend: 'Flat',
  };
}

/**
 * Maps the wire drill-down payload onto `IDepartmentDrilldown`. NOTE (findings): the
 * wire has no cycle LABEL (only a `cycleId`) and no score-scale, both of which are
 * rendered. `cycleLabel` → '' and `scoreScaleMax` → `DASHBOARD_SCALE_FALLBACK`.
 * Reported — not invented.
 */
export function mapDepartmentDrilldown(
  w: DepartmentDrilldownWire,
): IDepartmentDrilldown {
  return {
    departmentId: w.departmentId ?? '',
    departmentName: w.departmentName ?? '',
    cycleLabel: '',
    averageScore: w.averageScore ?? null,
    scoreScaleMax: DASHBOARD_SCALE_FALLBACK,
    employees: (w.employees ?? []).map(mapDepartmentEmployeeScore),
  };
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
