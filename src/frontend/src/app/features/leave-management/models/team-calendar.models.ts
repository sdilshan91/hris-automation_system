/**
 * US-LV-009: Team Leave Calendar models matching the backend API contract.
 *
 * Backend endpoint base: /api/v1/leaves/team-calendar
 * `environment.apiBaseUrl` already includes `/api/v1`, so the resource base is
 * `${apiBaseUrl}/leaves/team-calendar`.
 *
 * The backend agent is building this in parallel. Assumed contract (camelCase JSON):
 *   GET /api/v1/leaves/team-calendar?from={date}&to={date}
 *       [&employeeId][&leaveTypeId][&status]
 *   →  ITeamCalendarResponse { entries: ITeamCalendarEntry[]; holidays: ITeamCalendarHoliday[] }
 *
 * SCOPE-AWARE SHAPE (BR-1, BR-2, AC-1, AC-2):
 *   - Manager scope: entries carry leaveTypeName, color and status (Approved/Pending).
 *   - Employee scope: the API SUPPRESSES pending requests and the leave-type detail —
 *     entries arrive WITHOUT `leaveTypeName`/`color`/`status`, rendered generically as
 *     "On leave". The frontend renders whatever the API returns and never assumes the
 *     manager-only fields are present. It does NOT request or infer hidden data.
 */

/** Leave status the calendar may surface (manager scope only). */
export type TeamCalendarStatus = 'Approved' | 'Pending';

/** Half-day session indicator (BR-5). */
export type TeamHalfDaySession = 'AM' | 'PM';

/**
 * A single leave entry on the team calendar.
 *
 * Manager-only fields are OPTIONAL because the employee-scope API suppresses them
 * (BR-1). The UI must render gracefully when `leaveTypeName`/`color`/`status` are
 * absent (shows a neutral "On leave" block).
 */
export interface ITeamCalendarEntry {
  employeeId: string;
  employeeName: string;
  /** Manager scope only — absent for employee scope (BR-1). */
  leaveTypeName?: string | null;
  /** Manager scope only — denormalized hex; absent for employee scope. */
  color?: string | null;
  /** ISO date 'YYYY-MM-DD' (date-only, no time/timezone). */
  startDate: string;
  /** ISO date 'YYYY-MM-DD' (date-only). Single-day leaves: equals startDate. */
  endDate: string;
  /** Manager scope only — absent for employee scope (only Approved is shown then). */
  status?: TeamCalendarStatus | null;
  totalDays: number;
  /** True for half-day leaves (BR-5). */
  isHalfDay?: boolean;
  /** AM/PM session when isHalfDay (BR-5). Null/absent for full-day. */
  halfDaySession?: TeamHalfDaySession | null;
}

/**
 * A public holiday in the requested range (FR-7). Rendered as a light-gray
 * background highlight on the calendar. Date-only string.
 */
export interface ITeamCalendarHoliday {
  /** ISO date 'YYYY-MM-DD'. */
  date: string;
  name: string;
}

/**
 * Combined team-calendar payload (FR-1). The frontend also tolerates a bare
 * `ITeamCalendarEntry[]` body (no holidays) — see TeamCalendarService.
 */
export interface ITeamCalendarResponse {
  entries: ITeamCalendarEntry[];
  holidays: ITeamCalendarHoliday[];
}

/** Optional server-side filters (FR-6). */
export interface ITeamCalendarFilters {
  employeeId?: string | null;
  leaveTypeId?: string | null;
  /** Manager scope only — the employee scope never returns Pending. */
  status?: TeamCalendarStatus | null;
}

/** Error response shape from the backend for team-calendar operations. */
export interface ITeamCalendarErrorResponse {
  message: string;
  code?: string;
}

/** Calendar view modes (FR-5, §8). */
export type TeamCalendarView = 'month' | 'week' | 'list';

// ---------------------------------------------------------------------------
// Color palette for leave-type blocks (§8). When the backend supplies `color`
// (manager scope) it wins; otherwise we deterministically assign a palette
// color per distinct leave type so the legend + blocks stay consistent.
// ---------------------------------------------------------------------------

/** Notion-muted palette for distinct leave types when no API color is given. */
export const TEAM_CALENDAR_PALETTE: string[] = [
  '#2563eb', // blue
  '#16a34a', // green
  '#ea580c', // orange
  '#9333ea', // purple
  '#db2777', // pink
  '#0891b2', // cyan
  '#ca8a04', // amber
  '#dc2626', // red
];

/** Neutral block color used when a leave type is unknown (employee scope). */
export const TEAM_CALENDAR_NEUTRAL_COLOR = '#64748b'; // slate-500

/** Generic label for employee-scope entries with no leave-type detail (BR-1). */
export const TEAM_CALENDAR_GENERIC_LABEL = 'On leave';

/** Light-gray background token for public-holiday highlights (FR-7). */
export const TEAM_CALENDAR_HOLIDAY_BG = '#f1f5f9'; // slate-100

/**
 * Pure: resolve the display color for an entry.
 * Prefers the API-provided color (manager scope); else assigns a stable
 * palette color keyed off the leave-type name; else the neutral color.
 */
export function resolveEntryColor(
  entry: ITeamCalendarEntry,
  paletteIndex: Map<string, string>
): string {
  if (entry.color) return entry.color;
  const key = entry.leaveTypeName ?? '';
  if (key && paletteIndex.has(key)) return paletteIndex.get(key)!;
  return TEAM_CALENDAR_NEUTRAL_COLOR;
}

/** A legend item for the color key at the top of the calendar (§8). */
export interface ITeamCalendarLegendItem {
  label: string;
  color: string;
}

/**
 * Pure: build the leave-type color legend + a name→color index from entries.
 *
 * Manager scope: one legend item per distinct leaveTypeName (color from the API
 * when present, else a stable palette color). Employee scope (no leaveTypeName on
 * any entry): a single generic "On leave" legend item with the neutral color.
 */
export function buildLegend(entries: ITeamCalendarEntry[]): {
  legend: ITeamCalendarLegendItem[];
  paletteIndex: Map<string, string>;
} {
  const paletteIndex = new Map<string, string>();
  const apiColor = new Map<string, string>();
  const order: string[] = [];

  for (const e of entries) {
    const name = e.leaveTypeName;
    if (!name) continue;
    if (!order.includes(name)) order.push(name);
    if (e.color && !apiColor.has(name)) apiColor.set(name, e.color);
  }

  // Assign palette colors deterministically by first-seen order, skipping
  // names that already carry an API color.
  let p = 0;
  for (const name of order) {
    if (apiColor.has(name)) {
      paletteIndex.set(name, apiColor.get(name)!);
    } else {
      paletteIndex.set(name, TEAM_CALENDAR_PALETTE[p % TEAM_CALENDAR_PALETTE.length]);
      p++;
    }
  }

  if (order.length === 0) {
    // Employee scope (no leave-type detail at all) → one generic legend entry.
    const hasEntries = entries.length > 0;
    return {
      legend: hasEntries
        ? [{ label: TEAM_CALENDAR_GENERIC_LABEL, color: TEAM_CALENDAR_NEUTRAL_COLOR }]
        : [],
      paletteIndex,
    };
  }

  const legend: ITeamCalendarLegendItem[] = order.map((name) => ({
    label: name,
    color: paletteIndex.get(name)!,
  }));
  return { legend, paletteIndex };
}

/** Display label for an entry (BR-1: generic when no leave-type detail). */
export function entryLabel(entry: ITeamCalendarEntry): string {
  return entry.leaveTypeName ?? TEAM_CALENDAR_GENERIC_LABEL;
}

// ---------------------------------------------------------------------------
// Date helpers (date-only, no timezone parsing — slice strings to avoid TZ
// off-by-one, consistent with the US-LV-007 holiday calendar).
// ---------------------------------------------------------------------------

/** Pure: 'YYYY-MM-DD' for a year/0-based-month/day. No timezone shifts. */
export function toIsoDate(year: number, month0: number, day: number): string {
  const m = String(month0 + 1).padStart(2, '0');
  const d = String(day).padStart(2, '0');
  return `${year}-${m}-${d}`;
}

/** Pure: extract the 4-digit year from an ISO date string. */
export function yearOf(isoDate: string): number {
  return parseInt(isoDate.substring(0, 4), 10);
}

/** Pure: true when `iso` is within [from, to] inclusive (lexical compare is valid for ISO dates). */
export function isWithin(iso: string, from: string, to: string): boolean {
  return iso >= from && iso <= to;
}

/** Pure: true when an entry's [startDate, endDate] overlaps the given day. */
export function entryCoversDate(entry: ITeamCalendarEntry, iso: string): boolean {
  return entry.startDate <= iso && entry.endDate >= iso;
}

/** Month name labels (full). */
export const MONTH_NAMES = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

/** Weekday header labels (Sun-first, matching getDay()). */
export const WEEKDAY_LABELS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

/** A single month-grid cell (AC-1). */
export interface ICalendarCell {
  /** ISO date for the cell, or null for leading/trailing padding cells. */
  date: string | null;
  /** Day-of-month (1..31) or null for padding cells. */
  day: number | null;
  /** Whether this cell falls in the displayed month. */
  inMonth: boolean;
  /** True when this date is today (AC-1: today highlighted). */
  isToday: boolean;
  /** True when a public holiday falls on this date (FR-7 background highlight). */
  isHoliday: boolean;
  /** Holiday name(s) for the cell tooltip. */
  holidayNames: string[];
  /** Leave entries covering this date (already filtered upstream). */
  entries: ITeamCalendarEntry[];
}

/**
 * Pure: build the 6-row (42-cell) month grid for a given year/month.
 * Sunday-first weeks. Buckets each entry onto every date it covers, marks
 * today + public-holiday dates.
 *
 * @param year       4-digit year
 * @param month0     0-based month index
 * @param entries    leave entries (already filtered upstream)
 * @param holidayMap date → holiday names (FR-7)
 * @param todayIso   today's date as 'YYYY-MM-DD' (injected for testability)
 */
export function buildMonthGrid(
  year: number,
  month0: number,
  entries: ITeamCalendarEntry[],
  holidayMap: Map<string, string[]>,
  todayIso: string
): ICalendarCell[] {
  const firstDow = new Date(year, month0, 1).getDay(); // 0=Sun
  const daysInMonth = new Date(year, month0 + 1, 0).getDate();

  const cells: ICalendarCell[] = [];
  for (let i = 0; i < firstDow; i++) {
    cells.push(paddingCell());
  }
  for (let day = 1; day <= daysInMonth; day++) {
    const date = toIsoDate(year, month0, day);
    const holidayNames = holidayMap.get(date) ?? [];
    cells.push({
      date,
      day,
      inMonth: true,
      isToday: date === todayIso,
      isHoliday: holidayNames.length > 0,
      holidayNames,
      entries: entries.filter((e) => entryCoversDate(e, date)),
    });
  }
  while (cells.length < 42) {
    cells.push(paddingCell());
  }
  return cells;
}

function paddingCell(): ICalendarCell {
  return {
    date: null,
    day: null,
    inMonth: false,
    isToday: false,
    isHoliday: false,
    holidayNames: [],
    entries: [],
  };
}

/** A single day column in the week (Gantt) view (AC-3). */
export interface IWeekDay {
  /** ISO date 'YYYY-MM-DD'. */
  date: string;
  /** Weekday short label. */
  label: string;
  /** Day-of-month number. */
  day: number;
  isToday: boolean;
  isHoliday: boolean;
}

/**
 * Pure: build the 7 day columns for the week containing `anchorIso` (Sunday-first).
 */
export function buildWeekDays(
  anchorIso: string,
  holidayMap: Map<string, string[]>,
  todayIso: string
): IWeekDay[] {
  const anchor = new Date(
    yearOf(anchorIso),
    parseInt(anchorIso.substring(5, 7), 10) - 1,
    parseInt(anchorIso.substring(8, 10), 10)
  );
  const sunday = new Date(anchor);
  sunday.setDate(anchor.getDate() - anchor.getDay());

  const days: IWeekDay[] = [];
  for (let i = 0; i < 7; i++) {
    const d = new Date(sunday);
    d.setDate(sunday.getDate() + i);
    const iso = toIsoDate(d.getFullYear(), d.getMonth(), d.getDate());
    days.push({
      date: iso,
      label: WEEKDAY_LABELS[d.getDay()],
      day: d.getDate(),
      isToday: iso === todayIso,
      isHoliday: holidayMap.has(iso),
    });
  }
  return days;
}

/** A Gantt row for the week view: one employee with their bar segments (AC-3). */
export interface IWeekRow {
  employeeId: string;
  employeeName: string;
  /** Per-day-column cells: null = no leave, else the covering entry. */
  cells: (ITeamCalendarEntry | null)[];
}

/**
 * Pure: group entries by employee into Gantt rows aligned to `weekDays` (AC-3).
 * Each row has one cell per week day; a cell holds the entry covering that day
 * (first match) or null. Rows are sorted by employee name.
 */
export function buildWeekRows(
  entries: ITeamCalendarEntry[],
  weekDays: IWeekDay[]
): IWeekRow[] {
  const byEmployee = new Map<string, { name: string; entries: ITeamCalendarEntry[] }>();
  for (const e of entries) {
    const bucket = byEmployee.get(e.employeeId) ?? { name: e.employeeName, entries: [] };
    bucket.entries.push(e);
    byEmployee.set(e.employeeId, bucket);
  }

  const rows: IWeekRow[] = [];
  for (const [employeeId, { name, entries: emps }] of byEmployee) {
    const cells = weekDays.map(
      (wd) => emps.find((e) => entryCoversDate(e, wd.date)) ?? null
    );
    // Only keep rows that have at least one covered day in this week.
    if (cells.some((c) => c !== null)) {
      rows.push({ employeeId, employeeName: name, cells });
    }
  }
  rows.sort((a, b) => a.employeeName.localeCompare(b.employeeName));
  return rows;
}

/** A date-grouped bucket for the list view (AC-4). */
export interface IListGroup {
  /** ISO date 'YYYY-MM-DD'. */
  date: string;
  isHoliday: boolean;
  holidayNames: string[];
  entries: ITeamCalendarEntry[];
}

/**
 * Pure: group entries by their START date, sorted chronologically (AC-4).
 * Each group lists the entries that begin on that date. Holiday flag is set
 * when a public holiday falls on the group's date.
 */
export function buildListGroups(
  entries: ITeamCalendarEntry[],
  holidayMap: Map<string, string[]>
): IListGroup[] {
  const byDate = new Map<string, ITeamCalendarEntry[]>();
  for (const e of entries) {
    const list = byDate.get(e.startDate) ?? [];
    list.push(e);
    byDate.set(e.startDate, list);
  }
  const dates = [...byDate.keys()].sort((a, b) => a.localeCompare(b));
  return dates.map((date) => ({
    date,
    isHoliday: holidayMap.has(date),
    holidayNames: holidayMap.get(date) ?? [],
    entries: byDate
      .get(date)!
      .sort((a, b) => a.employeeName.localeCompare(b.employeeName)),
  }));
}

/** Pure: build a date→holiday-names lookup from the holiday list (FR-7). */
export function buildHolidayMap(
  holidays: ITeamCalendarHoliday[]
): Map<string, string[]> {
  const map = new Map<string, string[]>();
  for (const h of holidays) {
    const list = map.get(h.date) ?? [];
    list.push(h.name);
    map.set(h.date, list);
  }
  return map;
}

/** Pure: distinct employees from entries, sorted by name (for the filter bar, FR-6). */
export function distinctEmployees(
  entries: ITeamCalendarEntry[]
): { id: string; name: string }[] {
  const map = new Map<string, string>();
  for (const e of entries) {
    if (!map.has(e.employeeId)) map.set(e.employeeId, e.employeeName);
  }
  return [...map.entries()]
    .map(([id, name]) => ({ id, name }))
    .sort((a, b) => a.name.localeCompare(b.name));
}

/** Pure: distinct leave-type names from entries (manager scope only, FR-6). */
export function distinctLeaveTypes(entries: ITeamCalendarEntry[]): string[] {
  const set = new Set<string>();
  for (const e of entries) {
    if (e.leaveTypeName) set.add(e.leaveTypeName);
  }
  return [...set].sort((a, b) => a.localeCompare(b));
}

/** Pure: true when ANY entry carries a status (i.e. manager scope, FR-6). */
export function hasStatusScope(entries: ITeamCalendarEntry[]): boolean {
  return entries.some((e) => !!e.status);
}
