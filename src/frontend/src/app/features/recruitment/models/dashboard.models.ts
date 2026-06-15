/**
 * US-REC-009: Recruitment dashboard & analytics models matching the backend API
 * contract. The DTO shape, query params, and export route are reconciled with the
 * backend agent; the DTO↔FE mapping is kept in ONE place (RecruitmentDashboardService).
 *
 * `apiBaseUrl` already includes `/api/v1`, so the resource is
 * `${apiBaseUrl}/recruitment/dashboard`.
 *
 * Enum-ish string unions are PascalCase to match the C# members the API serializes
 * via JsonStringEnumConverter (US-PLT-003). All charts are rendered client-side from
 * the returned numbers using small pure SVG/CSS helpers below — NO charting library
 * is a project dependency, mirroring US-ATT-010 + US-REC-006.
 */

import { ApplicantSource } from './applicant.models';
import { VacancyStatus } from './vacancy.models';

// ─── Date-range presets (FR-6) ───────────────────────────────────────────────

/** Preset date-range keys (FR-6 pills). `custom` activates the date pickers. */
export type DateRangePreset = '7d' | '30d' | '90d' | '1y' | 'custom';

/** The resolved date range applied globally to every dashboard query (FR-6). */
export interface IDateRange {
  /** Inclusive range start, `yyyy-MM-dd`. */
  from: string;
  /** Inclusive range end, `yyyy-MM-dd`. */
  to: string;
}

/** A selectable preset pill (label + key). */
export interface IPresetOption {
  key: Exclude<DateRangePreset, 'custom'>;
  label: string;
  /** Number of days back from today the preset spans. */
  days: number;
}

/** FR-6 preset pills shown above the dashboard. */
export const DATE_RANGE_PRESETS: IPresetOption[] = [
  { key: '7d', label: '7 days', days: 7 },
  { key: '30d', label: '30 days', days: 30 },
  { key: '90d', label: '90 days', days: 90 },
  { key: '1y', label: '1 year', days: 365 },
];

// ─── Query params (FR-6 / FR-7) ──────────────────────────────────────────────

/**
 * Query params sent on every dashboard read (FR-6 date range, FR-7 optional
 * department/vacancy drill-down). The backend scopes by tenant + RLS server-side
 * (AC-5/NFR-2); the FE never sends a tenant id.
 *
 *   GET /recruitment/dashboard?from=&to=&departmentId=&vacancyId=
 */
export interface IDashboardQuery {
  /** Inclusive range start, `yyyy-MM-dd` (FR-6). */
  from: string;
  /** Inclusive range end, `yyyy-MM-dd` (FR-6). */
  to: string;
  /** Optional department drill-down (FR-7). */
  departmentId?: string;
  /** Optional vacancy drill-down (FR-7). */
  vacancyId?: string;
}

// ─── KPI cards (FR-1 / AC-1) ─────────────────────────────────────────────────

/**
 * The dashboard KPI values (FR-1). Numeric metrics for the top row of cards.
 * `avgTimeToHireDays` is calendar days from `applied_at` to the Hired stage (BR-1);
 * `offerAcceptanceRate` is a 0–100 percentage (BR-2). `previous*` companions, when
 * present, drive the up/down trend arrow vs. the previous equal-length period (§8);
 * the FE treats them as optional.
 */
export interface IDashboardKpis {
  openVacancies: number;
  totalApplicants: number;
  hires: number;
  avgTimeToHireDays: number;
  /** 0–100 (BR-2). */
  offerAcceptanceRate: number;
  offersPending: number;
  // Optional previous-period companions for the trend arrows (§8).
  previousTotalApplicants?: number | null;
  previousHires?: number | null;
  previousAvgTimeToHireDays?: number | null;
  previousOfferAcceptanceRate?: number | null;
}

// ─── Funnel (FR-2 / AC-3) ────────────────────────────────────────────────────

/**
 * One pipeline stage in the recruitment funnel (FR-2/AC-3). `count` is the number
 * of applicants that reached the stage; `conversionRate` is the % that advanced
 * from the PREVIOUS stage (BR-3) — null/absent on the first stage. The backend may
 * compute conversion; if absent the FE derives it from adjacent counts.
 */
export interface IFunnelStage {
  /** Canonical pipeline stage name (Applied/Screening/Interview/Offer/Hired). */
  stage: string;
  count: number;
  /** 0–100 conversion from the previous stage (BR-3); null on the first stage. */
  conversionRate?: number | null;
}

// ─── Source effectiveness (FR-3 / AC-4) ──────────────────────────────────────

/**
 * Source-effectiveness row (FR-3/AC-4): applicants by source + hires + conversion.
 * `source` matches the {@link ApplicantSource} union (+ Manual Entry / custom, BR-6).
 * `conversionRate` = hires / applicants * 100 (0–100); the FE derives it if absent.
 */
export interface ISourceEffectiveness {
  source: ApplicantSource | string;
  applicants: number;
  hires: number;
  /** 0–100 hire conversion per source; FE derives from counts if absent. */
  conversionRate?: number | null;
}

// ─── Time-to-hire trend (FR-4) ───────────────────────────────────────────────

/**
 * One point of the time-to-hire trend (FR-4): the average days-to-hire for a period
 * bucket (weekly/monthly). `label` is the bucket label ("Jun", "W23"); `avgDays` is
 * the average time-to-hire for hires completed in that bucket.
 */
export interface ITimeToHirePoint {
  label: string;
  avgDays: number;
}

// ─── Vacancy status summary (FR-5) ───────────────────────────────────────────

/** Vacancy count by status (FR-5) — feeds the donut / stacked bar. */
export interface IVacancyStatusCount {
  status: VacancyStatus;
  count: number;
}

// ─── Recent activity feed (FR-9) ─────────────────────────────────────────────

/**
 * Recruitment activity event types for the feed (FR-9). PascalCase to match the
 * C# enum members (US-PLT-003). Drives the icon + accent per row (§8).
 */
export type ActivityType =
  | 'ApplicantApplied'
  | 'StageChanged'
  | 'InterviewScheduled'
  | 'OfferSent'
  | 'OfferAccepted'
  | 'Hired';

/**
 * One recent-activity event (FR-9). `occurredAt` is a UTC timestamp the FE renders
 * as a relative label ("2 hours ago"). `applicantId`/`vacancyId` let the row link to
 * the relevant record (§8).
 */
export interface IActivityEvent {
  id: string;
  type: ActivityType;
  /** Human-readable description shown verbatim (FR-9). */
  description: string;
  /** UTC timestamp; FE renders a relative label (§8). */
  occurredAt: string;
  applicantId?: string | null;
  vacancyId?: string | null;
}

// ─── The full dashboard payload (§7 Output) ──────────────────────────────────

/**
 * US-REC-009 (§7 Output): the full recruitment-dashboard DTO returned by the
 * single dashboard endpoint. The backend aggregates tenant-scoped data (AC-5);
 * the FE only formats + charts it.
 *
 *   GET /recruitment/dashboard?from=&to=&departmentId=&vacancyId=
 *     -> IRecruitmentDashboard   (tolerates a `{ data }` envelope)
 */
export interface IRecruitmentDashboard {
  /** Echoed applied range (FR-6) for display + the export filename. */
  range: IDateRange;
  kpis: IDashboardKpis;
  funnel: IFunnelStage[];
  sources: ISourceEffectiveness[];
  timeToHireTrend: ITimeToHirePoint[];
  vacancyStatus: IVacancyStatusCount[];
  recentActivity: IActivityEvent[];
}

// ─── Filter lookup options (FR-7) ────────────────────────────────────────────

/** Minimal lookup option for the department/vacancy drill-down selects (FR-7). */
export interface IDashboardFilterOption {
  id: string;
  label: string;
}

// ─── Export (FR-8) ───────────────────────────────────────────────────────────

/**
 * US-REC-009 (FR-8) export formats. PDF (QuestPDF) + xlsx (ClosedXML) are
 * server-generated; CSV is the Phase-1 button. The FE streams whatever the backend
 * returns and downloads the blob (filename from Content-Disposition).
 */
export type DashboardExportFormat = 'csv' | 'xlsx' | 'pdf';

// ─── §8 presentation maps ────────────────────────────────────────────────────

/** Notion-style accent per vacancy status, reused on the donut + summary list (§8). */
export const VACANCY_STATUS_COLOR: Record<VacancyStatus, string> = {
  Draft: '#9ca3af', // gray-400
  Open: '#16a34a', // green-600
  OnHold: '#d97706', // amber-600
  Closed: '#dc2626', // red-600
  Cancelled: '#6b7280', // gray-500
};

/** Source-bar palette, keyed by canonical source (custom sources fall back, §8). */
export const SOURCE_COLOR: Record<string, string> = {
  Public: '#6366f1', // indigo-500
  Internal: '#0ea5e9', // sky-500
  Referral: '#10b981', // emerald-500
};

/** Default bar color for an unknown/custom source (BR-6). */
export const SOURCE_COLOR_FALLBACK = '#a78bfa'; // violet-400

/** Funnel stage bar color (single accent; opacity steps down the funnel, §8). */
export const FUNNEL_COLOR = '#6366f1'; // indigo-500

/** Icon glyph + accent per activity type for the feed (§8). */
export const ACTIVITY_META: Record<ActivityType, { label: string; dot: string }> = {
  ApplicantApplied: { label: 'New applicant', dot: 'bg-indigo-500' },
  StageChanged: { label: 'Stage change', dot: 'bg-sky-500' },
  InterviewScheduled: { label: 'Interview scheduled', dot: 'bg-violet-500' },
  OfferSent: { label: 'Offer sent', dot: 'bg-amber-500' },
  OfferAccepted: { label: 'Offer accepted', dot: 'bg-emerald-500' },
  Hired: { label: 'Hired', dot: 'bg-green-600' },
};

// ─── Pure helpers (unit-tested) ──────────────────────────────────────────────

/** Current date as `yyyy-MM-dd` in the browser's local timezone. */
export function todayIso(now: Date = new Date()): string {
  const y = now.getFullYear();
  const m = (now.getMonth() + 1).toString().padStart(2, '0');
  const d = now.getDate().toString().padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/**
 * Resolve a preset key to an `{ from, to }` range ending today (FR-6). `7d` spans
 * the last 7 calendar days inclusive of today (so `from = today - 6`).
 */
export function presetRange(days: number, now: Date = new Date()): IDateRange {
  const end = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const start = new Date(end);
  start.setDate(end.getDate() - (Math.max(1, days) - 1));
  return { from: todayIso(start), to: todayIso(end) };
}

/**
 * Derive the funnel conversion rate from the previous stage when the backend omits
 * it (BR-3). Returns null for the first stage or when the previous count is 0.
 */
export function funnelConversion(
  stages: IFunnelStage[],
  index: number,
): number | null {
  if (index <= 0) {
    return null;
  }
  const prev = stages[index - 1]?.count ?? 0;
  const curr = stages[index]?.count ?? 0;
  if (prev <= 0) {
    return null;
  }
  return Math.round((curr / prev) * 100);
}

/**
 * Derive a source's hire-conversion rate from its counts when the backend omits it.
 * hires / applicants * 100, rounded; 0 when there are no applicants.
 */
export function sourceConversion(row: ISourceEffectiveness): number {
  if (row.conversionRate != null) {
    return Math.round(row.conversionRate);
  }
  if (row.applicants <= 0) {
    return 0;
  }
  return Math.round((row.hires / row.applicants) * 100);
}

/** Bar width as a 0–100 percentage of the series max (CSS horizontal bars, §8). */
export function barPercent(value: number, max: number): number {
  if (max <= 0) {
    return 0;
  }
  return Math.round((Math.max(0, value) / max) * 100);
}

/** Max across a numeric series (shared bar/line scale). */
export function seriesMax(values: number[]): number {
  return values.reduce((m, v) => Math.max(m, v), 0);
}

/** The source bar color, falling back for custom sources (BR-6, §8). */
export function sourceColor(source: string): string {
  return SOURCE_COLOR[source] ?? SOURCE_COLOR_FALLBACK;
}

// ─── Donut geometry (vacancy status, FR-5) — pure SVG, no chart lib ──────────

/** A donut-ring arc segment for the vacancy-status chart (FR-5). */
export interface IDonutSegment {
  path: string;
  color: string;
  label: string;
  value: number;
  /** 0–100 share of the whole. */
  percent: number;
}

function round2(n: number): number {
  return Math.round(n * 100) / 100;
}

/**
 * Build donut-ring arc segments for a set of labelled values, centred at (cx,cy)
 * with the given radius. Pure + unit-tested. Each segment is a stroked arc (hollow
 * centre). Returns [] when the total is zero; a single non-zero value = full ring.
 */
export function buildDonutSegments(
  data: { label: string; value: number; color: string }[],
  cx: number,
  cy: number,
  radius: number,
): IDonutSegment[] {
  const total = data.reduce((s, d) => s + Math.max(0, d.value), 0);
  if (total <= 0) {
    return [];
  }
  let angle = -Math.PI / 2; // 12 o'clock
  return data
    .filter((d) => d.value > 0)
    .map((d) => {
      const frac = Math.max(0, d.value) / total;
      const sweep = frac * Math.PI * 2;
      const end = angle + sweep;
      const x1 = cx + radius * Math.cos(angle);
      const y1 = cy + radius * Math.sin(angle);
      const x2 = cx + radius * Math.cos(end);
      const y2 = cy + radius * Math.sin(end);
      const largeArc = sweep > Math.PI ? 1 : 0;
      const path =
        frac >= 0.999
          ? `M ${cx} ${cy - radius} A ${radius} ${radius} 0 1 1 ${cx - 0.01} ${cy - radius}`
          : `M ${round2(x1)} ${round2(y1)} A ${radius} ${radius} 0 ${largeArc} 1 ${round2(
              x2,
            )} ${round2(y2)}`;
      angle = end;
      return { path, color: d.color, label: d.label, value: d.value, percent: frac * 100 };
    });
}

// ─── Trend line geometry (time-to-hire, FR-4) — pure SVG ─────────────────────

/** Build SVG polyline points for a trend series scaled into a width×height box. */
export function buildTrendPoints(
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
    x: values.length > 1 ? round2(i * stepX) : round2(width / 2),
    y: round2(height - (Math.max(0, v) / max) * height),
  }));
}

/** Stringify points for an SVG `points` attribute. */
export function trendPointsToString(points: { x: number; y: number }[]): string {
  return points.map((p) => `${p.x},${p.y}`).join(' ');
}

// ─── Relative timestamps (FR-9) ──────────────────────────────────────────────

/**
 * Render a UTC timestamp as a compact relative label ("just now", "2 hours ago",
 * "3 days ago") for the activity feed (§8). Falls back to "" on an invalid date.
 */
export function relativeTime(iso: string, now: Date = new Date()): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) {
    return '';
  }
  const diffSec = Math.floor((now.getTime() - d.getTime()) / 1000);
  if (diffSec < 45) {
    return 'just now';
  }
  const units: { limit: number; div: number; one: string; many: string }[] = [
    { limit: 3600, div: 60, one: 'minute', many: 'minutes' },
    { limit: 86400, div: 3600, one: 'hour', many: 'hours' },
    { limit: 2592000, div: 86400, one: 'day', many: 'days' },
    { limit: 31536000, div: 2592000, one: 'month', many: 'months' },
    { limit: Infinity, div: 31536000, one: 'year', many: 'years' },
  ];
  for (const u of units) {
    if (diffSec < u.limit) {
      const n = Math.floor(diffSec / u.div);
      return `${n} ${n === 1 ? u.one : u.many} ago`;
    }
  }
  return '';
}

/** Format a calendar-day count for a KPI ("12.4 days", "—" for null/NaN). */
export function formatDays(days: number | null | undefined): string {
  if (days == null || Number.isNaN(days)) {
    return '—';
  }
  const rounded = Math.round(days * 10) / 10;
  return `${rounded} ${rounded === 1 ? 'day' : 'days'}`;
}

/**
 * Compute a period-over-period trend direction for a KPI (§8 arrow). Returns
 * 'up' | 'down' | 'flat'; null when there is no previous value to compare.
 */
export function trendDirection(
  current: number,
  previous: number | null | undefined,
): 'up' | 'down' | 'flat' | null {
  if (previous == null || Number.isNaN(previous)) {
    return null;
  }
  if (current > previous) {
    return 'up';
  }
  if (current < previous) {
    return 'down';
  }
  return 'flat';
}
