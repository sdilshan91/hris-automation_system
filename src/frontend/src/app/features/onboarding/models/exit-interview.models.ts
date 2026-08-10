/**
 * US-ONB-006: Exit Interview Recording models.
 *
 * Mirrors the PINNED backend contract under `/api/v1/exit-interviews` exactly
 * (paths/verbs/field names are fixed). Kept in ONE place so the service mapping,
 * the questionnaire form, and the analytics view share a single source of truth.
 *
 * CHARTING DECISION: this repo deliberately ships NO chart library (no chart.js /
 * ngx-charts / ng2-charts — verified absent from package.json + node_modules). The
 * analytics view draws its pie/bar/line charts in pure SVG/CSS, exactly like the
 * US-PRF-007 performance dashboard (donut arcs + inline-SVG polyline). The geometry
 * helpers below are pure functions so they unit-test without a component.
 */

// ─── Template (GET /exit-interviews/template) ────────────────────────────

/** Supported question render types (FR-1). */
export type ExitQuestionType =
  | 'rating'
  | 'multiple_choice'
  | 'free_text'
  | 'yes_no';

/** A single template question within a category (AC-1 / FR-1). */
export interface IExitQuestion {
  questionId: string;
  text: string;
  type: ExitQuestionType;
  /** Present for multiple_choice questions. */
  options?: string[];
  required: boolean;
}

/** A category groups related questions (e.g. "Reason for Leaving") (AC-1). */
export interface IExitCategory {
  category: string;
  questions: IExitQuestion[];
}

/** The full questionnaire template returned by GET /template. */
export interface IExitInterviewTemplate {
  templateId: string;
  categories: IExitCategory[];
}

// ─── Record (POST /exit-interviews) ──────────────────────────────────────

/** Who is filling the interview in (FR-2). */
export type InterviewMode = 'hr_conducted' | 'self_service';

/** One answer in the POST body. Only the field for the question type is set. */
export interface IExitResponse {
  questionId: string;
  rating?: number;
  selectedOption?: string;
  freeText?: string;
}

/** Body of POST /exit-interviews (Data Requirements §7 input fields). */
export interface IRecordExitInterviewRequest {
  offboardingId: string;
  interviewMode: InterviewMode;
  /** ISO date (yyyy-MM-dd); cannot be in the future. */
  interviewDate: string;
  responses: IExitResponse[];
  overallExperienceRating?: number;
  wouldRecommendEmployer?: boolean;
  additionalComments?: string;
}

/** The persisted interview returned by GET ?offboardingId= and POST. */
export interface IExitInterview {
  id: string;
  offboardingInstanceId: string;
  interviewMode: InterviewMode;
  interviewDate: string;
  responses: IExitResponse[];
  overallExperienceRating?: number | null;
  wouldRecommendEmployer?: boolean | null;
  additionalComments?: string | null;
}

// ─── Analytics (GET /exit-interviews/analytics) ──────────────────────────

/** One slice of the reason-distribution pie chart (AC-4 / FR-4). */
export interface IReasonDatum {
  reason: string;
  count: number;
}

/** One bar of the average-rating-per-category bar chart (AC-4 / FR-4). */
export interface ICategoryRatingDatum {
  category: string;
  averageRating: number;
}

/** One point of the trend line chart over time (AC-4 / FR-4). */
export interface ITrendDatum {
  period: string;
  count: number;
  averageRating: number;
}

/** The aggregated analytics payload (anonymised — aggregates only, FR-5). */
export interface IExitInterviewAnalytics {
  reasonDistribution: IReasonDatum[];
  averageRatingsPerCategory: ICategoryRatingDatum[];
  trend: ITrendDatum[];
}

// ─── Form constants ──────────────────────────────────────────────────────

/** Max chars for a single free_text response (Data Requirements §7). */
export const MAX_FREE_TEXT_LEN = 2000;

/** Max chars for the additional-comments field (Data Requirements §7). */
export const MAX_ADDITIONAL_COMMENTS_LEN = 5000;

/** Rating scale bounds (1–5) shared by the rating selector + validators. */
export const RATING_MIN = 1;
export const RATING_MAX = 5;

/** Human labels under each star/emoji on the 1–5 rating selector (UI/UX §8). */
export const RATING_LABELS: readonly string[] = [
  'Very poor',
  'Poor',
  'Neutral',
  'Good',
  'Excellent',
] as const;

/** Label for a 1–5 rating value (1-based). Empty string when out of range. */
export function ratingLabel(value: number): string {
  return RATING_LABELS[value - 1] ?? '';
}

// ─── Display + derivation helpers (pure — unit-tested directly) ──────────

/**
 * Today's date as an ISO yyyy-MM-dd string in local time — the interview date and
 * the `max` on the date input (cannot be in the future, Data Requirements §7).
 */
export function todayIso(today: Date = new Date()): string {
  const y = today.getFullYear();
  const m = `${today.getMonth() + 1}`.padStart(2, '0');
  const d = `${today.getDate()}`.padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/** Total required questions across every category (drives the progress bar). */
export function totalRequiredQuestions(
  template: IExitInterviewTemplate | null,
): number {
  if (!template) return 0;
  return template.categories
    .flatMap((c) => c.questions)
    .filter((q) => q.required).length;
}

/**
 * Whether a raw answer value counts as "answered" for progress + validation.
 * A rating of 0 / empty string / null / undefined is unanswered.
 */
export function isAnswered(value: unknown): boolean {
  if (value === null || value === undefined) return false;
  if (typeof value === 'string') return value.trim().length > 0;
  if (typeof value === 'number') return value > 0;
  if (typeof value === 'boolean') return true;
  return false;
}

/**
 * Completion percent (0–100) across the REQUIRED questions only, used for the
 * progress bar at the top of the questionnaire (UI/UX §8). Pure over the answered
 * count so the component can derive it from the form state.
 */
export function completionPercent(
  answeredRequired: number,
  totalRequired: number,
): number {
  if (totalRequired <= 0) return 100;
  return Math.max(0, Math.min(100, Math.round((answeredRequired / totalRequired) * 100)));
}

// ─── Chart geometry (pure SVG/CSS — NO chart library) ────────────────────

/** Deterministic palette shared by the pie slices + bar/line accents. */
export const CHART_COLORS = [
  '#404040', // neutral-700
  '#2563eb', // blue-600
  '#059669', // emerald-600
  '#d97706', // amber-600
  '#7c3aed', // violet-600
  '#dc2626', // red-600
  '#0891b2', // cyan-600
  '#db2777', // pink-600
] as const;

/** Stable color for the Nth chart series/slice. */
export function chartColor(index: number): string {
  return CHART_COLORS[index % CHART_COLORS.length];
}

/** One computed pie slice: an SVG path `d` plus its color + percentage. */
export interface IPieSlice {
  reason: string;
  count: number;
  percent: number;
  color: string;
  /** SVG path `d` for the slice wedge within the given viewBox. */
  path: string;
}

/**
 * Build SVG wedge paths for a reason-distribution pie chart (AC-4). Center
 * (cx,cy) + radius are in viewBox units. Each slice is an arc from the running
 * angle; a single 100% slice degrades to a full circle path. Pure → unit-tested.
 */
export function pieSlices(
  data: IReasonDatum[],
  cx: number,
  cy: number,
  r: number,
): IPieSlice[] {
  const total = data.reduce((sum, d) => sum + d.count, 0);
  if (total <= 0) return [];
  let startAngle = -Math.PI / 2; // start at 12 o'clock
  return data.map((d, i) => {
    const fraction = d.count / total;
    const angle = fraction * 2 * Math.PI;
    const endAngle = startAngle + angle;
    const x1 = cx + r * Math.cos(startAngle);
    const y1 = cy + r * Math.sin(startAngle);
    const x2 = cx + r * Math.cos(endAngle);
    const y2 = cy + r * Math.sin(endAngle);
    const largeArc = angle > Math.PI ? 1 : 0;
    // A full circle (single slice) can't be drawn with one arc — close to a
    // near-full circle via two semicircles.
    const path =
      fraction >= 0.999
        ? `M ${cx} ${cy - r} A ${r} ${r} 0 1 1 ${(cx - 0.01).toFixed(3)} ${cy - r} Z`
        : `M ${cx} ${cy} L ${x1.toFixed(3)} ${y1.toFixed(3)} ` +
          `A ${r} ${r} 0 ${largeArc} 1 ${x2.toFixed(3)} ${y2.toFixed(3)} Z`;
    startAngle = endAngle;
    return {
      reason: d.reason,
      count: d.count,
      percent: Math.round(fraction * 100),
      color: chartColor(i),
      path,
    };
  });
}

/**
 * A horizontal bar's width percent for the average-rating-per-category chart,
 * relative to the 1–5 rating scale (AC-4). Pure helper.
 */
export function ratingBarPercent(averageRating: number): number {
  return Math.max(0, Math.min(100, (averageRating / RATING_MAX) * 100));
}

/**
 * Build an inline-SVG polyline `points` string for the trend chart's average
 * rating over evenly spaced periods (AC-4). The chart viewBox is `0 0 width
 * height`; ratings map onto the 1–5 scale. Returns "" when <1 point.
 */
export function trendPolyline(
  trend: ITrendDatum[],
  width: number,
  height: number,
  pad = 8,
): string {
  if (trend.length === 0) return '';
  const innerW = width - pad * 2;
  const innerH = height - pad * 2;
  const step = trend.length > 1 ? innerW / (trend.length - 1) : 0;
  return trend
    .map((t, i) => {
      const x = trend.length === 1 ? width / 2 : pad + step * i;
      const ratio = Math.max(0, Math.min(1, t.averageRating / RATING_MAX));
      const y = pad + innerH * (1 - ratio);
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .join(' ');
}
