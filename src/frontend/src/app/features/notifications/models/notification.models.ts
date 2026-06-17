/**
 * US-NTF-001: In-App Notification System (Real-Time via SignalR).
 *
 * Frontend models for the notification bell + panel. The shapes mirror the
 * PINNED FE↔BE contract: JSON is camelCase, enums arrive as strings, and the
 * apiEnvelopeInterceptor (US-PLT-001) has already unwrapped the ApiResponse<T>
 * envelope, so services receive the bare payload (these types directly).
 */

/** A single notification record (matches NotificationDto on the wire). */
export interface INotification {
  id: string;
  /** e.g. "leave_approved", "task_assigned", "payroll_completed". */
  type: string;
  title: string;
  message: string;
  /** e.g. "LeaveRequest", "OnboardingTask"; null when not deep-linkable. */
  resourceType: string | null;
  resourceId: string | null;
  isRead: boolean;
  /** ISO-8601 timestamp. */
  createdAt: string;
}

/** GET /notifications?page&pageSize — one paginated page (FR-6). */
export interface INotificationPage {
  items: INotification[];
  unreadCount: number;
  totalCount: number;
  page: number;
  pageSize: number;
}

/** GET /notifications/unread-count. */
export interface IUnreadCountResult {
  count: number;
}

/** POST /notifications/read-all. */
export interface IMarkAllReadResult {
  updated: number;
}

/**
 * SignalR / fallback connection lifecycle as surfaced to the UI.
 * - connecting: initial handshake or reconnect in flight
 * - connected:  live WebSocket established (real-time push active)
 * - reconnecting: automatic-reconnect backoff in progress (FR-9)
 * - polling:    WebSocket unavailable; degraded 30s polling (NFR-5)
 * - disconnected: no live channel and not polling
 */
export type NotificationConnectionState =
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'polling'
  | 'disconnected';

/** Page size for the notification list (FR-6: 20 per page). */
export const NOTIFICATION_PAGE_SIZE = 20;

/** Polling cadence when WebSocket is unavailable (NFR-5: every 30s). */
export const NOTIFICATION_POLL_INTERVAL_MS = 30_000;

/**
 * Map a notification `type` to a type-specific icon + colour token (§8: icon on
 * the left, colour-coded). Pure helper — kept out of the component for testing.
 * `icon` is a Material symbol ligature; `tone` selects the tinted background.
 */
export interface INotificationVisual {
  icon: string;
  /** Tailwind tone key used by the panel for the icon chip colour. */
  tone: 'green' | 'blue' | 'amber' | 'purple' | 'red' | 'neutral';
}

export function notificationVisual(type: string): INotificationVisual {
  const key = (type || '').toLowerCase();
  if (key.includes('approved') || key.includes('completed') || key.includes('success')) {
    return { icon: 'check_circle', tone: 'green' };
  }
  if (key.includes('rejected') || key.includes('failed') || key.includes('declined')) {
    return { icon: 'cancel', tone: 'red' };
  }
  if (key.includes('reminder') || key.includes('overdue') || key.includes('due')) {
    return { icon: 'schedule', tone: 'amber' };
  }
  if (key.includes('task') || key.includes('assigned') || key.includes('onboarding')) {
    return { icon: 'assignment', tone: 'purple' };
  }
  if (key.includes('payroll') || key.includes('payslip') || key.includes('salary')) {
    return { icon: 'payments', tone: 'blue' };
  }
  if (key.includes('leave')) {
    return { icon: 'event', tone: 'blue' };
  }
  return { icon: 'notifications', tone: 'neutral' };
}

/**
 * Resolve the in-app route for a notification's linked resource (FR-8 / AC-4).
 * Returns null when there is nothing to navigate to. Pure + table-driven so the
 * routing can be unit-tested independently of the component.
 */
export function notificationRoute(
  resourceType: string | null,
  resourceId: string | null,
): string[] | null {
  if (!resourceType) {
    return null;
  }
  const type = resourceType.toLowerCase();
  switch (type) {
    case 'leaverequest':
      return ['/leave', 'requests', ...(resourceId ? [resourceId] : [])];
    case 'attendance':
    case 'regularization':
      return ['/attendance'];
    case 'onboardingtask':
    case 'onboarding':
      return ['/onboarding', 'my-checklist'];
    case 'payslip':
    case 'payroll':
      return ['/my-payslips'];
    case 'performancereview':
    case 'review':
      return ['/my-review'];
    case 'vacancy':
    case 'application':
      return ['/recruitment'];
    default:
      // Unknown resource type: fall back to the full notifications list so the
      // click is never a dead-end.
      return ['/notifications'];
  }
}

/**
 * Relative-time formatter for the timestamp column (§8: "2 min ago"). Pure so it
 * is deterministic under test (pass an explicit `now`).
 */
export function relativeTime(iso: string, now: number = Date.now()): string {
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) {
    return '';
  }
  const diffSec = Math.max(0, Math.round((now - then) / 1000));
  if (diffSec < 45) {
    return 'just now';
  }
  const diffMin = Math.round(diffSec / 60);
  if (diffMin < 60) {
    return `${diffMin} min ago`;
  }
  const diffHr = Math.round(diffMin / 60);
  if (diffHr < 24) {
    return `${diffHr} hr ago`;
  }
  const diffDay = Math.round(diffHr / 24);
  if (diffDay < 7) {
    return `${diffDay} day${diffDay === 1 ? '' : 's'} ago`;
  }
  return new Date(then).toLocaleDateString();
}

/** Badge text for the unread count (FR-5: cap at "99+"). */
export function badgeText(count: number): string {
  if (count <= 0) {
    return '';
  }
  return count > 99 ? '99+' : String(count);
}
