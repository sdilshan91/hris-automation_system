/**
 * US-RPT-005: Dashboard with KPI Widgets — frontend contract.
 *
 * The backend returns the ROLE-APPROPRIATE set of widgets for the signed-in
 * user (HR / Manager / Employee). The frontend renders them GENERICALLY through
 * ONE widget-card renderer — there is no role logic client-side beyond layout
 * (BR-1; role visibility is server-authoritative, FR-1/FR-8).
 *
 * TENANT-scoped endpoint under `/api/v1/dashboard...` (NOT the system-admin
 * console). Tenant isolation is enforced server-side via ITenantContext + EF
 * global filters (AC-5 / FR-8); the FE just carries the auth cookie + the
 * X-Tenant-Subdomain header (added by the tenantInterceptor). Services consume
 * BARE payloads (the global ApiResponse unwrap interceptor strips the envelope),
 * matching the rest of the FE.
 */

/** The user's dashboard persona (server-authoritative, BR-1). */
export type DashboardRole = 'hr' | 'manager' | 'employee';

/** Trend direction for the KPI arrow (FR-2 / BR-2). */
export type TrendDirection = 'up' | 'down' | 'flat';

/** Mini-visualisation kind for a widget (FR-2). */
export type MiniChartType = 'sparkline' | 'donut' | 'progress';

/** A single data point for a mini chart series. */
export interface IDashboardChartPoint {
  label: string;
  value: number;
}

/**
 * The optional mini visualisation embedded in a widget card (FR-2):
 *   - 'sparkline' → a compact line (rendered via the US-RPT-001 chart wrapper)
 *   - 'donut'     → a pie/donut (e.g. leave balance consumed vs remaining)
 *   - 'progress'  → a Tailwind progress bar (value/max)
 */
export interface IDashboardMiniChart {
  type: MiniChartType;
  series: IDashboardChartPoint[];
  /** Denominator for the progress bar (and donut total); null when N/A. */
  max: number | null;
}

/**
 * One actionable / linkable list row inside a widget (FR-6/FR-7). Used by the
 * pending-approvals, quick-actions, pending-actions and activity-feed widgets.
 */
export interface IDashboardItem {
  label: string;
  /** Optional secondary value (e.g. a count or a date). */
  value: string | null;
  /** Click-through target for this row (FR-5); null → not clickable. */
  linkUrl: string | null;
  /** Pre-applied filters carried as query params on click-through (FR-5). */
  linkFilters: Record<string, unknown> | null;
}

/**
 * ONE flexible widget shape (FR-2 / §7). Only the fields relevant to a given
 * widget are populated; the rest are null. The FE renders by which fields are
 * present — KPI value, trend, mini chart, item list, and/or card click-through.
 */
export interface IDashboardWidget {
  /** Stable kebab key, e.g. 'headcount', 'pending-leave' (§7 widget_key). */
  widgetKey: string;
  /** Display label (server-localised or literal). */
  label: string;
  /** Primary metric (numeric KPI or a short string); null when N/A. */
  value: number | string | null;
  /** Unit suffix, e.g. '%' or 'days'; null when N/A. */
  unit: string | null;
  /** Previous-period value for the trend (BR-2); null when N/A. */
  previousValue: number | null;
  /** Trend arrow direction; null hides the arrow. */
  trendDirection: TrendDirection | null;
  /** Percentage change vs the previous period (BR-2). */
  trendPercentage: number | null;
  /**
   * Whether the trend is GOOD for this metric (FR-2 / §8). Colour by THIS, not
   * by direction: true → green, false → red, null → neutral. (Headcount up is
   * good; turnover up is bad — the server decides, the FE just paints.)
   */
  trendIsPositive: boolean | null;
  /** Optional mini visualisation (sparkline / donut / progress). */
  miniChart: IDashboardMiniChart | null;
  /** Optional list rows (approvals / quick-actions / pending tasks). */
  items: IDashboardItem[] | null;
  /** Card-level click-through target (FR-5 / AC-4); null → not clickable. */
  linkUrl: string | null;
  /** Pre-applied filters carried as query params on card click-through. */
  linkFilters: Record<string, unknown> | null;
}

/**
 * The full dashboard payload from `GET /api/v1/dashboard/widgets`. The server
 * sends the persona, the greeting name, a generated-at timestamp, and the
 * role-appropriate widget array (FR-1/FR-3).
 */
export interface IDashboardResponse {
  role: DashboardRole;
  greetingName: string;
  generatedAt: string;
  widgets: IDashboardWidget[];
}
