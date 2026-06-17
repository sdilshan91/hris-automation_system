/**
 * US-NTF-003: Notification Preferences per User.
 *
 * FE-facing models for the per-user, per-tenant-membership preference matrix and
 * the Quiet Hours setting. Enums are plain strings, times are "HH:mm"/"HH:mm:ss"
 * strings, and the global apiEnvelopeInterceptor unwraps the ApiResponse<T>
 * envelope so services see these BARE payloads (matching US-NTF-001/002).
 */

/** A single category row: its channel toggles + whether the org forces it on. */
export interface ICategoryPreference {
  /** Stable category key, e.g. "leave_updates" (used in the PUT path). */
  category: string;
  /** Human-readable name shown as the row/card label. */
  categoryName: string;
  /** Short explanation of what events the category covers (tooltip/info). */
  description: string;
  /** In-app channel enabled for this category. */
  channelInApp: boolean;
  /** Email channel enabled for this category. */
  channelEmail: boolean;
  /**
   * Mandatory categories (e.g. Security Alerts) cannot be opted out of — both
   * toggles render disabled/locked (AC-3/AC-4, BR-2).
   */
  isMandatory: boolean;
}

/** The two delivery channels supported in Phase 1 (SMS deferred). */
export type NotificationChannel = 'channelInApp' | 'channelEmail';

/**
 * Quiet Hours: queue emails outside the window (BR-5). `start`/`end` are "HH:mm"
 * strings (or "HH:mm:ss" from the server); null when never configured.
 */
export interface IQuietHours {
  enabled: boolean;
  start: string | null;
  end: string | null;
  /** IANA timezone, e.g. "Asia/Colombo"; null until set. */
  timezone: string | null;
}

/** The complete preference matrix returned by GET / reset. */
export interface IPreferenceMatrix {
  categories: ICategoryPreference[];
  quietHours: IQuietHours;
}

/** Body for the per-category PUT (the two channel booleans). */
export interface ICategoryUpdate {
  channelInApp: boolean;
  channelEmail: boolean;
}

/** Body for the quiet-hours PUT. */
export interface IQuietHoursUpdate {
  enabled: boolean;
  start: string | null;
  end: string | null;
  timezone: string | null;
}

/**
 * A curated subset of IANA timezones for the Quiet Hours selector. Kept small and
 * dependency-free; the user's current zone is added at runtime if missing so the
 * pre-selected value always resolves.
 */
export const COMMON_TIMEZONES: readonly string[] = [
  'UTC',
  'Africa/Cairo',
  'Africa/Johannesburg',
  'America/Chicago',
  'America/Los_Angeles',
  'America/New_York',
  'America/Sao_Paulo',
  'Asia/Colombo',
  'Asia/Dubai',
  'Asia/Kolkata',
  'Asia/Shanghai',
  'Asia/Singapore',
  'Asia/Tokyo',
  'Australia/Sydney',
  'Europe/Berlin',
  'Europe/London',
  'Europe/Paris',
  'Pacific/Auckland',
];

/** The browser's resolved IANA timezone, or "UTC" when unavailable. */
export function detectBrowserTimezone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
}

/** Build the timezone option list, inserting the user's zone if not already present. */
export function buildTimezoneOptions(current: string | null): string[] {
  const zone = current ?? detectBrowserTimezone();
  const set = new Set<string>(COMMON_TIMEZONES);
  set.add(zone);
  return Array.from(set).sort();
}

/** Normalise a server time ("HH:mm:ss") down to the "HH:mm" used by <input type=time>. */
export function toInputTime(value: string | null): string {
  if (!value) return '';
  return value.length >= 5 ? value.slice(0, 5) : value;
}
