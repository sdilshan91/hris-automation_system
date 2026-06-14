/**
 * US-ATT-001: Attendance clock-in models matching the backend API contract.
 *
 * Backend endpoint (backend agent building in parallel -- assumed contract):
 *   GET  /api/v1/attendance/status     - current employee's clock-in status for today (IClockStatus)
 *   POST /api/v1/attendance/clock-in   - create an attendance_log clock-in, returns IAttendanceLog
 *
 * NOTE: `apiBaseUrl` already includes `/api/v1`, so the resource is `${apiBaseUrl}/attendance`.
 *
 * Geolocation policy (BR-2) is tenant-level: the FE reads `requireGeolocation` from the
 * status endpoint to decide whether a denied browser permission BLOCKS clock-in (AC-3) or
 * is simply omitted (AC-4). The backend remains the authority for geo-fence (FR-3) and the
 * IP allowlist (FR-4, AC-5) — those are enforced server-side and surfaced as typed errors.
 */

/** Clock-in source channel recorded on the attendance log (§7 `source`). */
export type AttendanceSource = 'WEB' | 'MOBILE_WEB';

/**
 * Attendance log entity returned by the API after a successful clock-in (FR-1).
 * Geolocation fields are nullable (AC-4 — optional policy / denied permission).
 */
export interface IAttendanceLog {
  attendanceLogId: string;
  tenantId: string;
  employeeId: string;
  /** Clock-in timestamp in UTC (FR-7); the UI converts to local time for display. */
  clockIn: string;
  /** Set on clock-out; null while the record is open (BR-1). */
  clockOut: string | null;
  clockInLatitude: number | null;
  clockInLongitude: number | null;
  source: AttendanceSource;
}

/**
 * Request payload for clocking in (FR-1).
 * Coordinates are conditional (§7): required only when the tenant geo policy is
 * mandatory, otherwise omitted. The backend stamps IP + user-agent server-side (FR-5).
 */
export interface IClockInRequest {
  /** Latitude, when captured. Null/omitted when geo is optional and not granted (AC-4). */
  latitude: number | null;
  /** Longitude, when captured. Null/omitted when geo is optional and not granted (AC-4). */
  longitude: number | null;
  /** Channel the clock-in came from ('WEB' from a desktop browser). */
  source: AttendanceSource;
}

/**
 * Current clock-in status for the employee today (dashboard fast lookup, FR-6).
 * Drives the initial card state: already-clocked-in shows the live timer (AC-2 reflect),
 * and `requireGeolocation` decides the AC-3 vs AC-4 branch when permission is denied.
 */
export interface IClockStatus {
  /** True when there is an open (un-clocked-out) record for today (BR-1, AC-2). */
  isClockedIn: boolean;
  /** UTC clock-in timestamp of the open record, when `isClockedIn` is true. */
  clockedInAt: string | null;
  /** BR-2: tenant requires geolocation — a denied permission must block clock-in (AC-3). */
  requireGeolocation: boolean;
  /** Assigned shift display name for context (§8). Null when no shift is assigned yet. */
  shiftName: string | null;
  /** Expected shift start time (HH:mm, employee-local) for context (§8). Null if unknown. */
  shiftStart: string | null;
}

/**
 * Typed error body from the backend for clock-in (AC-2, AC-5).
 * `message` is shown verbatim inline; `code` is the machine-readable discriminator:
 *   - 409 `code: 'already_clocked_in'` -> "You have already clocked in..." (AC-2)
 *   - 403 `code: 'ip_not_allowed'`     -> IP allowlist rejection (AC-5)
 *   - 403 `code: 'geo_fence_violation'`-> coordinates outside the allowed radius (FR-3)
 */
export interface IClockInErrorResponse {
  message: string;
  code?: 'already_clocked_in' | 'ip_not_allowed' | 'geo_fence_violation' | string;
}

/**
 * Result of attempting to read the browser geolocation (AC-3, AC-4).
 * `denied` is true when the user refused permission or the browser blocked it;
 * `coords` carries the captured position when granted.
 */
export interface IGeolocationResult {
  granted: boolean;
  denied: boolean;
  coords: { latitude: number; longitude: number } | null;
  /** Human-readable reason when not granted (permission denied, unavailable, timeout). */
  error: string | null;
}

/**
 * Pure helper: format elapsed milliseconds as a live work timer "HH:MM:SS" (§8).
 * Clamps negatives to zero so a clock-skewed start never renders a negative timer.
 */
export function formatElapsed(elapsedMs: number): string {
  const totalSeconds = Math.max(0, Math.floor(elapsedMs / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  const pad = (n: number) => n.toString().padStart(2, '0');
  return `${pad(hours)}:${pad(minutes)}:${pad(seconds)}`;
}

/**
 * Pure helper: build a static OpenStreetMap embed URL for a tiny location preview (§8).
 * Uses the free OSM `export/embed` endpoint — no API key, no heavy maps dependency.
 * A small bounding box is derived around the point so the marker sits centered.
 */
export function buildStaticMapUrl(latitude: number, longitude: number): string {
  const delta = 0.005;
  const left = (longitude - delta).toFixed(6);
  const right = (longitude + delta).toFixed(6);
  const bottom = (latitude - delta).toFixed(6);
  const top = (latitude + delta).toFixed(6);
  const bbox = `${left},${bottom},${right},${top}`;
  const marker = `${latitude.toFixed(6)},${longitude.toFixed(6)}`;
  return (
    'https://www.openstreetmap.org/export/embed.html' +
    `?bbox=${encodeURIComponent(bbox)}&layer=mapnik&marker=${encodeURIComponent(marker)}`
  );
}
