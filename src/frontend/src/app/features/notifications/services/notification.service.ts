import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, Subscription, timer } from 'rxjs';
import { tap } from 'rxjs/operators';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { environment } from '../../../../environments/environment';
import { AuthService } from '../../../core/auth/auth.service';
import {
  IMarkAllReadResult,
  INotification,
  INotificationPage,
  IUnreadCountResult,
  NOTIFICATION_PAGE_SIZE,
  NOTIFICATION_POLL_INTERVAL_MS,
  NotificationConnectionState,
  badgeText,
} from '../models/notification.models';

/**
 * US-NTF-001: In-App Notification System (Real-Time via SignalR).
 *
 * App-wide singleton (providedIn: 'root') that owns:
 *  - the SignalR connection to `${apiHost}/hubs/notifications`, authenticated via
 *    `accessTokenFactory` returning the in-memory JWT (AC-1 / FR-1), with built-in
 *    exponential-backoff automatic reconnect (FR-9);
 *  - the reactive notification list, unread count and connection-state signals
 *    consumed by the bell + panel;
 *  - the REST calls for initial load, pagination, mark-read, mark-all-read and
 *    unread-count;
 *  - a graceful 30s polling fallback when the WebSocket cannot connect (NFR-5).
 *
 * Tenant isolation is server-side: the hub joins `t:{tenantId}:user:{userId}`
 * groups (AC-6) and REST calls are tenant-scoped via the tenantInterceptor + RLS.
 * Responses are the BARE payload — the apiEnvelopeInterceptor unwraps ApiResponse<T>.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  /** REST base — `${apiBaseUrl}/notifications` (apiBaseUrl already ends with /api/v1). */
  private readonly restBase = `${environment.apiBaseUrl}/notifications`;

  /**
   * The SignalR hub lives at the HOST root (`/hubs/notifications`), NOT under
   * `/api/v1`. Strip the trailing `/api/v1` (or any `/api/...`) segment from the
   * configured API base so the hub URL is correct regardless of environment.
   */
  private readonly hubUrl = `${environment.apiBaseUrl.replace(/\/api(\/.*)?$/, '')}/hubs/notifications`;

  private hub: HubConnection | null = null;
  private pollSub: Subscription | null = null;
  /** Most-recent live notification id announced to the ARIA live region. */
  private readonly announceSignal = signal<string>('');

  // ─── Reactive state ───────────────────────────────────────
  readonly notifications = signal<INotification[]>([]);
  readonly unreadCount = signal(0);
  readonly totalCount = signal(0);
  readonly connectionState = signal<NotificationConnectionState>('disconnected');
  readonly loading = signal(false);
  /** Whether more pages remain (drives infinite scroll, FR-6). */
  readonly hasMore = signal(false);
  /** The current loaded page index (1-based). */
  private readonly page = signal(0);

  /** Cap-aware badge label ("", "5", "99+") — FR-5. */
  readonly badge = computed(() => badgeText(this.unreadCount()));
  /** Text for the ARIA live region announcing the latest arrival (NFR-6). */
  readonly liveAnnouncement = this.announceSignal.asReadonly();

  // ─── Connection lifecycle ─────────────────────────────────

  /**
   * Establish the realtime connection (AC-1). Idempotent: a second call while
   * already connected/connecting is a no-op. On failure, degrades to polling.
   */
  async start(): Promise<void> {
    if (this.hub || this.connectionState() === 'polling') {
      return;
    }

    // Initial REST load so the panel/badge are populated immediately, even
    // before (or instead of) the WebSocket handshake completing.
    this.loadFirstPage();

    this.connectionState.set('connecting');
    const hub = new HubConnectionBuilder()
      .withUrl(this.hubUrl, {
        accessTokenFactory: () => this.auth.getAccessToken() ?? '',
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    hub.on('ReceiveNotification', (dto: INotification) =>
      this.onNotificationReceived(dto),
    );
    hub.onreconnecting(() => this.connectionState.set('reconnecting'));
    hub.onreconnected(() => {
      this.connectionState.set('connected');
      // Re-sync after a gap so any missed notifications appear (test hint).
      this.refreshUnreadCount();
      this.loadFirstPage();
    });
    hub.onclose(() => {
      // The connection dropped and automatic-reconnect gave up — fall back.
      if (this.connectionState() !== 'polling') {
        this.startPolling();
      }
    });

    this.hub = hub;
    try {
      await hub.start();
      this.connectionState.set('connected');
    } catch {
      // WebSocket unavailable — gracefully degrade to polling (NFR-5).
      this.hub = null;
      this.startPolling();
    }
  }

  /** Tear everything down (call on logout). */
  async stop(): Promise<void> {
    this.stopPolling();
    if (this.hub) {
      const h = this.hub;
      this.hub = null;
      try {
        await h.stop();
      } catch {
        /* already stopping/stopped */
      }
    }
    this.connectionState.set('disconnected');
  }

  /** True when the realtime hub is live (vs. polling/disconnected). */
  isLive(): boolean {
    return this.hub?.state === HubConnectionState.Connected;
  }

  // ─── Polling fallback (NFR-5) ─────────────────────────────

  private startPolling(): void {
    if (this.pollSub) {
      return;
    }
    this.connectionState.set('polling');
    // timer(0, n) fires immediately then every n ms.
    this.pollSub = timer(0, NOTIFICATION_POLL_INTERVAL_MS).subscribe(() => {
      this.refreshUnreadCount();
      this.loadFirstPage();
    });
  }

  private stopPolling(): void {
    this.pollSub?.unsubscribe();
    this.pollSub = null;
  }

  // ─── Incoming push ────────────────────────────────────────

  /** Prepend a freshly pushed notification + bump the unread badge (AC-2). */
  private onNotificationReceived(dto: INotification): void {
    this.notifications.update((list) => [dto, ...list]);
    this.totalCount.update((n) => n + 1);
    if (!dto.isRead) {
      this.unreadCount.update((n) => n + 1);
    }
    // ARIA live announcement (NFR-6). Unique value forces screen-reader re-read.
    this.announceSignal.set(`${dto.title}. ${dto.message}`);
  }

  // ─── REST: load / paginate ────────────────────────────────

  /** Reset to and load the first page (used by initial load + re-sync). */
  loadFirstPage(): void {
    this.fetchPage(1, true);
  }

  /** Load the next page for infinite scroll (FR-6); no-op when none remain. */
  loadNextPage(): void {
    if (this.loading() || !this.hasMore()) {
      return;
    }
    this.fetchPage(this.page() + 1, false);
  }

  private fetchPage(page: number, replace: boolean): void {
    this.loading.set(true);
    const params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(NOTIFICATION_PAGE_SIZE));

    this.http
      .get<INotificationPage>(this.restBase, { params, withCredentials: true })
      .subscribe({
        next: (res) => {
          if (replace) {
            this.notifications.set(res.items);
          } else {
            // Append, de-duping any ids already present (a push may have
            // prepended an item that also lands in a later REST page).
            this.notifications.update((existing) => {
              const seen = new Set(existing.map((n) => n.id));
              return [...existing, ...res.items.filter((n) => !seen.has(n.id))];
            });
          }
          this.unreadCount.set(res.unreadCount);
          this.totalCount.set(res.totalCount);
          this.page.set(res.page);
          this.hasMore.set(res.page * res.pageSize < res.totalCount);
          this.loading.set(false);
        },
        error: () => this.loading.set(false),
      });
  }

  /** Lightweight unread-count refresh (used by polling + reconnect). */
  refreshUnreadCount(): void {
    this.http
      .get<IUnreadCountResult>(`${this.restBase}/unread-count`, {
        withCredentials: true,
      })
      .subscribe({
        next: (res) => this.unreadCount.set(res.count),
        error: () => {
          /* transient — keep last known count */
        },
      });
  }

  // ─── REST: mark read ──────────────────────────────────────

  /**
   * Mark a single notification read (AC-4). Optimistically updates local state
   * (decrement badge, flip flag) then persists; reverts on failure.
   */
  markRead(id: string): Observable<unknown> {
    const target = this.notifications().find((n) => n.id === id);
    const wasUnread = target ? !target.isRead : false;
    if (wasUnread) {
      this.applyRead(id);
    }
    return this.http
      .post(`${this.restBase}/${id}/read`, null, { withCredentials: true })
      .pipe(
        tap({
          error: () => {
            if (wasUnread) {
              this.revertRead(id);
            }
          },
        }),
      );
  }

  /** Mark all notifications read (AC-5); badge resets to zero. */
  markAllRead(): Observable<IMarkAllReadResult> {
    const previous = this.notifications();
    const previousUnread = this.unreadCount();
    this.notifications.update((list) =>
      list.map((n) => (n.isRead ? n : { ...n, isRead: true })),
    );
    this.unreadCount.set(0);
    return this.http
      .post<IMarkAllReadResult>(`${this.restBase}/read-all`, null, {
        withCredentials: true,
      })
      .pipe(
        tap({
          error: () => {
            this.notifications.set(previous);
            this.unreadCount.set(previousUnread);
          },
        }),
      );
  }

  private applyRead(id: string): void {
    this.notifications.update((list) =>
      list.map((n) => (n.id === id ? { ...n, isRead: true } : n)),
    );
    this.unreadCount.update((n) => Math.max(0, n - 1));
  }

  private revertRead(id: string): void {
    this.notifications.update((list) =>
      list.map((n) => (n.id === id ? { ...n, isRead: false } : n)),
    );
    this.unreadCount.update((n) => n + 1);
  }
}
