import { TestBed, fakeAsync, tick, flush } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import * as signalR from '@microsoft/signalr';
import { NotificationService } from './notification.service';
import { AuthService } from '../../../core/auth/auth.service';
import { environment } from '../../../../environments/environment';
import {
  INotification,
  INotificationPage,
  NOTIFICATION_POLL_INTERVAL_MS,
} from '../models/notification.models';

/**
 * A minimal fake @microsoft/signalr HubConnection. Records registered client
 * handlers so the test can simulate a server "ReceiveNotification" push, and
 * lets each test decide whether start() resolves (live) or rejects (degrade to
 * polling, NFR-5).
 */
class FakeHub {
  state = signalR.HubConnectionState.Disconnected;
  startResult: Promise<void> = Promise.resolve();
  readonly handlers = new Map<string, (...args: unknown[]) => void>();
  reconnectingCb: (() => void) | null = null;
  reconnectedCb: (() => void) | null = null;
  closeCb: (() => void) | null = null;

  on(method: string, cb: (...args: unknown[]) => void): void {
    this.handlers.set(method, cb);
  }
  onreconnecting(cb: () => void): void {
    this.reconnectingCb = cb;
  }
  onreconnected(cb: () => void): void {
    this.reconnectedCb = cb;
  }
  onclose(cb: () => void): void {
    this.closeCb = cb;
  }
  start(): Promise<void> {
    return this.startResult.then(() => {
      this.state = signalR.HubConnectionState.Connected;
    });
  }
  stop(): Promise<void> {
    this.state = signalR.HubConnectionState.Disconnected;
    return Promise.resolve();
  }
  /** Test helper: simulate a server push. */
  push(dto: INotification): void {
    this.handlers.get('ReceiveNotification')?.(dto);
  }
}

function makeNotification(overrides: Partial<INotification> = {}): INotification {
  return {
    id: overrides.id ?? crypto.randomUUID(),
    type: overrides.type ?? 'leave_approved',
    title: overrides.title ?? 'Leave approved',
    message: overrides.message ?? 'Your leave was approved.',
    resourceType: overrides.resourceType ?? 'LeaveRequest',
    resourceId: overrides.resourceId ?? 'lr-1',
    isRead: overrides.isRead ?? false,
    createdAt: overrides.createdAt ?? new Date().toISOString(),
  };
}

function emptyPage(over: Partial<INotificationPage> = {}): INotificationPage {
  return {
    items: over.items ?? [],
    unreadCount: over.unreadCount ?? 0,
    totalCount: over.totalCount ?? 0,
    page: over.page ?? 1,
    pageSize: over.pageSize ?? 20,
  };
}

describe('NotificationService', () => {
  let service: NotificationService;
  let httpMock: HttpTestingController;
  let fakeHub: FakeHub;
  const restBase = `${environment.apiBaseUrl}/notifications`;

  let lastBuilder: signalR.HubConnectionBuilder;

  beforeEach(() => {
    fakeHub = new FakeHub();

    // Use the REAL builder (so the service's `new HubConnectionBuilder()` works
    // regardless of ESM binding), but stub `build()` on the prototype to hand
    // back our fake hub. withUrl's args are still captured for URL/token asserts.
    spyOn(
      signalR.HubConnectionBuilder.prototype,
      'build',
    ).and.returnValue(fakeHub as unknown as signalR.HubConnection);
    spyOn(
      signalR.HubConnectionBuilder.prototype,
      'withUrl',
    ).and.callFake(function (this: signalR.HubConnectionBuilder) {
      lastBuilder = this;
      return this;
    });

    TestBed.configureTestingModule({
      providers: [
        NotificationService,
        provideHttpClient(withInterceptors([])),
        provideHttpClientTesting(),
        {
          provide: AuthService,
          useValue: { getAccessToken: () => 'jwt-token' },
        },
      ],
    });

    service = TestBed.inject(NotificationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  it('is created with a disconnected default state', () => {
    expect(service).toBeTruthy();
    expect(service.connectionState()).toBe('disconnected');
    expect(service.unreadCount()).toBe(0);
  });

  it('builds the hub URL at the host root (no /api/v1) (AC-1)', async () => {
    await service.start();
    const withUrl = signalR.HubConnectionBuilder.prototype
      .withUrl as jasmine.Spy;
    expect(withUrl).toHaveBeenCalled();
    const url = withUrl.calls.mostRecent().args[0] as string;
    expect(url.endsWith('/hubs/notifications')).toBeTrue();
    expect(url).not.toContain('/api/v1');
    expect(lastBuilder).toBeTruthy();
    // initial REST load fired by start()
    httpMock.expectOne(`${restBase}?page=1&pageSize=20`).flush(emptyPage());
  });

  it('passes the JWT via accessTokenFactory (AC-1)', async () => {
    await service.start();
    const withUrl = signalR.HubConnectionBuilder.prototype
      .withUrl as jasmine.Spy;
    const opts = withUrl.calls.mostRecent().args[1] as {
      accessTokenFactory: () => string;
    };
    expect(opts.accessTokenFactory()).toBe('jwt-token');
    httpMock.expectOne(`${restBase}?page=1&pageSize=20`).flush(emptyPage());
  });

  it('sets connected state and loads the first page on start()', async () => {
    await service.start();
    expect(service.connectionState()).toBe('connected');
    const req = httpMock.expectOne(`${restBase}?page=1&pageSize=20`);
    req.flush(
      emptyPage({
        items: [makeNotification()],
        unreadCount: 1,
        totalCount: 1,
      }),
    );
    expect(service.notifications().length).toBe(1);
    expect(service.unreadCount()).toBe(1);
  });

  it('prepends a pushed notification and increments the badge (AC-2)', async () => {
    await service.start();
    httpMock.expectOne(`${restBase}?page=1&pageSize=20`).flush(emptyPage());

    fakeHub.push(makeNotification({ id: 'new-1', title: 'New one' }));

    expect(service.notifications()[0].id).toBe('new-1');
    expect(service.unreadCount()).toBe(1);
    expect(service.badge()).toBe('1');
    expect(service.liveAnnouncement()).toContain('New one');
  });

  it('caps the badge label at 99+ (FR-5)', async () => {
    await service.start();
    httpMock
      .expectOne(`${restBase}?page=1&pageSize=20`)
      .flush(emptyPage({ unreadCount: 150, totalCount: 150 }));
    expect(service.badge()).toBe('99+');
  });

  it('degrades to polling when the WebSocket fails to connect (NFR-5)', fakeAsync(() => {
    fakeHub.startResult = Promise.reject(new Error('ws down'));

    service.start();
    // initial REST load fired synchronously by start()
    httpMock.expectOne(`${restBase}?page=1&pageSize=20`).flush(emptyPage());

    tick(); // let the rejected start() promise settle -> startPolling()
    expect(service.connectionState()).toBe('polling');

    // timer(0, interval) does not emit on subscribe — advance 0ms so the FIRST
    // poll cycle fires both the unread-count AND the first-page requests. The
    // page response also carries unreadCount, so flush them consistently.
    tick(0);
    httpMock.expectOne(`${restBase}/unread-count`).flush({ count: 3 });
    httpMock
      .expectOne(`${restBase}?page=1&pageSize=20`)
      .flush(emptyPage({ unreadCount: 3 }));
    expect(service.unreadCount()).toBe(3);

    // advance 30s -> second poll cycle
    tick(NOTIFICATION_POLL_INTERVAL_MS);
    httpMock.expectOne(`${restBase}/unread-count`).flush({ count: 4 });
    httpMock
      .expectOne(`${restBase}?page=1&pageSize=20`)
      .flush(emptyPage({ unreadCount: 4 }));
    expect(service.unreadCount()).toBe(4);

    flush();
    service.stop();
  }));

  it('appends de-duplicated items on loadNextPage (FR-6)', async () => {
    await service.start();
    httpMock.expectOne(`${restBase}?page=1&pageSize=20`).flush(
      emptyPage({
        items: [makeNotification({ id: 'a' })],
        totalCount: 25,
        page: 1,
        pageSize: 20,
      }),
    );
    expect(service.hasMore()).toBeTrue();

    service.loadNextPage();
    httpMock.expectOne(`${restBase}?page=2&pageSize=20`).flush(
      emptyPage({
        // 'a' is a duplicate and must be dropped
        items: [makeNotification({ id: 'a' }), makeNotification({ id: 'b' })],
        totalCount: 25,
        page: 2,
        pageSize: 20,
      }),
    );

    const ids = service.notifications().map((n) => n.id);
    expect(ids).toEqual(['a', 'b']);
    expect(service.hasMore()).toBeFalse();
  });

  it('marks a single notification read optimistically (AC-4)', async () => {
    await service.start();
    httpMock.expectOne(`${restBase}?page=1&pageSize=20`).flush(
      emptyPage({
        items: [makeNotification({ id: 'r1', isRead: false })],
        unreadCount: 1,
        totalCount: 1,
      }),
    );

    service.markRead('r1').subscribe();
    // optimistic update already applied before the POST resolves
    expect(service.notifications()[0].isRead).toBeTrue();
    expect(service.unreadCount()).toBe(0);

    httpMock.expectOne(`${restBase}/r1/read`).flush({});
  });

  it('reverts an optimistic single read when the POST fails', async () => {
    await service.start();
    httpMock.expectOne(`${restBase}?page=1&pageSize=20`).flush(
      emptyPage({
        items: [makeNotification({ id: 'r1', isRead: false })],
        unreadCount: 1,
        totalCount: 1,
      }),
    );

    service.markRead('r1').subscribe({ error: () => undefined });
    httpMock
      .expectOne(`${restBase}/r1/read`)
      .flush('err', { status: 500, statusText: 'Server Error' });

    expect(service.notifications()[0].isRead).toBeFalse();
    expect(service.unreadCount()).toBe(1);
  });

  it('marks all read and resets the badge to zero (AC-5)', async () => {
    await service.start();
    httpMock.expectOne(`${restBase}?page=1&pageSize=20`).flush(
      emptyPage({
        items: [
          makeNotification({ id: 'a', isRead: false }),
          makeNotification({ id: 'b', isRead: false }),
        ],
        unreadCount: 2,
        totalCount: 2,
      }),
    );

    service.markAllRead().subscribe();
    expect(service.unreadCount()).toBe(0);
    expect(service.notifications().every((n) => n.isRead)).toBeTrue();

    httpMock.expectOne(`${restBase}/read-all`).flush({ updated: 2 });
  });

  afterEach(() => {
    httpMock.verify();
  });
});
