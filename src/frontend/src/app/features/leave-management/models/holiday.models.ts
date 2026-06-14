/**
 * US-LV-007: Holiday Calendar models matching the backend API contract.
 *
 * Backend endpoint base: /api/v1/holidays
 * The backend agent is building this in parallel. Assumed contract (camelCase JSON):
 *   - id (uuid), name (string, varchar(100)), date (ISO date 'YYYY-MM-DD'),
 *     type ('public' | 'restricted' | 'optional'), locationId (uuid, nullable),
 *     locationName (string, nullable, denormalized for display),
 *     description (string, nullable), isRecurring (boolean), isActive (boolean).
 *
 * Color coding (§8): public = blue, restricted = orange, optional = green.
 */

/** Holiday type per FR-2. */
export type HolidayType = 'public' | 'restricted' | 'optional';

/** Holiday entity returned by the API. */
export interface IHoliday {
  id: string;
  name: string;
  /** ISO date string, 'YYYY-MM-DD' (date-only, no time component). */
  date: string;
  type: HolidayType;
  locationId: string | null;
  locationName: string | null;
  description: string | null;
  isRecurring: boolean;
  isActive: boolean;
}

/** Request payload for creating a holiday (AC-1, FR-2). */
export interface ICreateHolidayRequest {
  name: string;
  date: string;
  type: HolidayType;
  locationId?: string | null;
  description?: string | null;
  isRecurring: boolean;
}

/** Request payload for updating a holiday (FR-1). */
export interface IUpdateHolidayRequest extends ICreateHolidayRequest {}

/** A single parsed CSV row staged for preview before confirm (AC-3). */
export interface IHolidayImportRow {
  /** 1-based row number in the source file (header excluded). */
  rowNumber: number;
  name: string;
  date: string;
  type: string;
  /** Client-side validation messages; empty = valid. */
  errors: string[];
  /** True when this row's (date, type) duplicates another row or an existing holiday (BR-1). */
  isDuplicate: boolean;
}

/** Aggregated result of parsing + validating a CSV file (AC-3). */
export interface IHolidayImportPreview {
  rows: IHolidayImportRow[];
  validCount: number;
  invalidCount: number;
  duplicateCount: number;
}

/** Response from the backend bulk-import endpoint (AC-3). */
export interface IHolidayImportResult {
  total: number;
  imported: number;
  skipped: number;
  errors: { row: number; message: string }[];
}

/** Error response shape from the backend for holiday operations. */
export interface IHolidayErrorResponse {
  message: string;
  /** e.g. 'duplicate_date' (BR-1), 'payroll_locked' (BR-4). */
  code?: 'duplicate_date' | 'payroll_locked' | string;
}

/** Holiday type display labels + Tailwind/hex color tokens (§8). */
export const HOLIDAY_TYPE_OPTIONS: {
  value: HolidayType;
  label: string;
  /** Hex used for calendar dot markers + swatches. */
  hex: string;
  /** Tailwind classes for the list-view badge. */
  badgeClasses: string;
}[] = [
  {
    value: 'public',
    label: 'Public',
    hex: '#2563eb', // blue
    badgeClasses: 'bg-blue-50 text-blue-700 ring-blue-600/20',
  },
  {
    value: 'restricted',
    label: 'Restricted',
    hex: '#ea580c', // orange
    badgeClasses: 'bg-orange-50 text-orange-700 ring-orange-600/20',
  },
  {
    value: 'optional',
    label: 'Optional',
    hex: '#16a34a', // green
    badgeClasses: 'bg-green-50 text-green-700 ring-green-600/20',
  },
];

/** Map a holiday type to its display color (hex). Public is the safe default. */
export function getHolidayTypeColor(type: HolidayType | string): string {
  const opt = HOLIDAY_TYPE_OPTIONS.find((o) => o.value === type);
  return opt ? opt.hex : '#2563eb';
}

/** Map a holiday type to its list-view badge classes. */
export function getHolidayTypeBadgeClasses(type: HolidayType | string): string {
  const opt = HOLIDAY_TYPE_OPTIONS.find((o) => o.value === type);
  return opt ? opt.badgeClasses : HOLIDAY_TYPE_OPTIONS[0].badgeClasses;
}

/** Map a holiday type to its human label. */
export function getHolidayTypeLabel(type: HolidayType | string): string {
  const opt = HOLIDAY_TYPE_OPTIONS.find((o) => o.value === type);
  return opt ? opt.label : String(type);
}

/**
 * Pure: derive the date string ('YYYY-MM-DD') for a given year/month/day.
 * Month is 0-based (JS convention). Pads to two digits. No timezone shifts.
 */
export function toIsoDate(year: number, month0: number, day: number): string {
  const m = String(month0 + 1).padStart(2, '0');
  const d = String(day).padStart(2, '0');
  return `${year}-${m}-${d}`;
}

/** Pure: extract the 4-digit year from an ISO date string ('YYYY-MM-DD'). */
export function yearOf(isoDate: string): number {
  return parseInt(isoDate.substring(0, 4), 10);
}

/** A single calendar grid cell for the month-grid view. */
export interface ICalendarCell {
  /** ISO date for the cell, or null for leading/trailing padding cells. */
  date: string | null;
  /** Day-of-month number (1..31) or null for padding cells. */
  day: number | null;
  /** Whether this cell falls in the currently displayed month. */
  inMonth: boolean;
  /** Holidays falling on this date (already location-filtered). */
  holidays: IHoliday[];
}

/** Month name labels (full). */
export const MONTH_NAMES = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

/** Weekday header labels (Sun-first, matching getDay()). */
export const WEEKDAY_LABELS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

/**
 * Pure: build the 6-row (42-cell) month grid for a given year/month.
 * Holidays are bucketed onto their date cell. Sunday-first weeks.
 *
 * @param year     4-digit year
 * @param month0   0-based month index
 * @param holidays holidays for the year (already location-filtered upstream)
 */
export function buildMonthGrid(
  year: number,
  month0: number,
  holidays: IHoliday[]
): ICalendarCell[] {
  const byDate = new Map<string, IHoliday[]>();
  for (const h of holidays) {
    const list = byDate.get(h.date) ?? [];
    list.push(h);
    byDate.set(h.date, list);
  }

  const firstDow = new Date(year, month0, 1).getDay(); // 0=Sun
  const daysInMonth = new Date(year, month0 + 1, 0).getDate();

  const cells: ICalendarCell[] = [];
  // Leading padding cells.
  for (let i = 0; i < firstDow; i++) {
    cells.push({ date: null, day: null, inMonth: false, holidays: [] });
  }
  // In-month cells.
  for (let day = 1; day <= daysInMonth; day++) {
    const date = toIsoDate(year, month0, day);
    cells.push({
      date,
      day,
      inMonth: true,
      holidays: byDate.get(date) ?? [],
    });
  }
  // Trailing padding to complete a 6x7 grid (42 cells).
  while (cells.length < 42) {
    cells.push({ date: null, day: null, inMonth: false, holidays: [] });
  }
  return cells;
}

/**
 * Pure: group a year's holidays by month for the mobile / list-by-month view.
 * Returns 12 buckets (Jan..Dec), each holiday list sorted by date ascending.
 */
export function groupByMonth(holidays: IHoliday[]): IHoliday[][] {
  const buckets: IHoliday[][] = Array.from({ length: 12 }, () => []);
  for (const h of holidays) {
    const month0 = parseInt(h.date.substring(5, 7), 10) - 1;
    if (month0 >= 0 && month0 < 12) {
      buckets[month0].push(h);
    }
  }
  for (const bucket of buckets) {
    bucket.sort((a, b) => a.date.localeCompare(b.date));
  }
  return buckets;
}

/**
 * Pure: parse raw CSV text into staged import rows with validation (AC-3).
 *
 * Expected columns (header, case-insensitive): name, date, type.
 * `date` must be a valid ISO 'YYYY-MM-DD'. `type` must be one of the
 * HolidayType values. Duplicate (date, type) rows are flagged (BR-1),
 * and rows whose (date, type) match an existing holiday are also flagged.
 *
 * @param csvText          raw file contents
 * @param existingHolidays current holidays for the active year (for dup detection)
 */
export function parseHolidayCsv(
  csvText: string,
  existingHolidays: IHoliday[]
): IHolidayImportPreview {
  const validTypes: string[] = HOLIDAY_TYPE_OPTIONS.map((o) => o.value);
  const isoDateRe = /^\d{4}-\d{2}-\d{2}$/;

  const existingKeys = new Set(
    existingHolidays.map((h) => `${h.date}|${h.type}`)
  );

  const lines = csvText
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter((l) => l.length > 0);

  const rows: IHolidayImportRow[] = [];
  if (lines.length === 0) {
    return { rows, validCount: 0, invalidCount: 0, duplicateCount: 0 };
  }

  // Detect + skip a header row (when the first row contains the word 'name').
  let startIndex = 0;
  const headerCells = splitCsvLine(lines[0]).map((c) => c.toLowerCase());
  if (headerCells.includes('name') && headerCells.includes('date')) {
    startIndex = 1;
  }

  const seenKeys = new Set<string>();

  for (let i = startIndex; i < lines.length; i++) {
    const cells = splitCsvLine(lines[i]);
    const name = (cells[0] ?? '').trim();
    const date = (cells[1] ?? '').trim();
    const type = (cells[2] ?? '').trim().toLowerCase();
    const errors: string[] = [];

    if (!name) {
      errors.push('Name is required.');
    } else if (name.length > 100) {
      errors.push('Name must be 100 characters or fewer.');
    }
    if (!date) {
      errors.push('Date is required.');
    } else if (!isoDateRe.test(date) || isNaN(Date.parse(date))) {
      errors.push('Date must be a valid YYYY-MM-DD value.');
    }
    if (!type) {
      errors.push('Type is required.');
    } else if (!validTypes.includes(type)) {
      errors.push(`Type must be one of: ${validTypes.join(', ')}.`);
    }

    const key = `${date}|${type}`;
    let isDuplicate = false;
    if (errors.length === 0) {
      if (seenKeys.has(key) || existingKeys.has(key)) {
        isDuplicate = true;
      }
      seenKeys.add(key);
    }

    rows.push({
      rowNumber: i - startIndex + 1,
      name,
      date,
      type,
      errors,
      isDuplicate,
    });
  }

  const invalidCount = rows.filter((r) => r.errors.length > 0).length;
  const duplicateCount = rows.filter((r) => r.isDuplicate).length;
  const validCount = rows.filter(
    (r) => r.errors.length === 0 && !r.isDuplicate
  ).length;

  return { rows, validCount, invalidCount, duplicateCount };
}

/**
 * Minimal CSV line splitter supporting double-quoted fields with escaped
 * quotes ("") and embedded commas. Sufficient for the holiday import format.
 */
export function splitCsvLine(line: string): string[] {
  const result: string[] = [];
  let current = '';
  let inQuotes = false;
  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (inQuotes) {
      if (ch === '"') {
        if (line[i + 1] === '"') {
          current += '"';
          i++;
        } else {
          inQuotes = false;
        }
      } else {
        current += ch;
      }
    } else if (ch === '"') {
      inQuotes = true;
    } else if (ch === ',') {
      result.push(current);
      current = '';
    } else {
      current += ch;
    }
  }
  result.push(current);
  return result;
}
