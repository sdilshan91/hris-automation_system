import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { NotificationPreferenceService } from './notification-preference.service';
import { environment } from '../../../../environments/environment';
import {
  ICategoryPreference,
  IPreferenceMatrix,
  IQuietHours,
} from '../models/notification-preference.models';

/**
 * US-NTF-003: NotificationPreferenceService — REST + signal state. Asserts URLs,
 * methods, request bodies and the cache-sync side effects on categories()/
 * quietHours(). Tenant + user isolation is server-side; here we just confirm the
 * contract the backend was built against.
 */
describe('NotificationPreferenceService', () => {
  let service: NotificationPreferenceService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/notification-preferences`;

  const matrix: IPreferenceMatrix = {
    categories: [
      {
        category: 'leave_updates',
        categoryName: 'Leave Updates',
        description: 'Approvals, rejections, balance changes.',
        channelInApp: true,
        channelEmail: true,
        isMandatory: false,
      },
      {
        category: 'security_alerts',
        categoryName: 'Security Alerts',
        description: 'Sign-ins and security events.',
        channelInApp: true,
        channelEmail: true,
        isMandatory: true,
      },
    ],
    quietHours: {
      enabled: true,
      start: '22:00:00',
      end: '07:00:00',
      timezone: 'Asia/Colombo',
    },
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        NotificationPreferenceService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(NotificationPreferenceService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('load GETs the matrix and populates both signals', () => {
    service.load();
    expect(service.loading()).toBeTrue();

    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(matrix);

    expect(service.loading()).toBeFalse();
    expect(service.hasLoaded()).toBeTrue();
    expect(service.categories().length).toBe(2);
    expect(service.mandatoryCount()).toBe(1);
    expect(service.quietHours().timezone).toBe('Asia/Colombo');
  });

  it('load clears the loading flag on error', () => {
    service.load();
    httpMock
      .expectOne(base)
      .flush('boom', { status: 500, statusText: 'Server Error' });
    expect(service.loading()).toBeFalse();
  });

  it('updateCategory PUTs the channel booleans and swaps the cached row', () => {
    // seed the matrix so we can assert the in-place row replace
    service.load();
    httpMock.expectOne(base).flush(matrix);

    const updated: ICategoryPreference = {
      ...matrix.categories[0],
      channelEmail: false,
    };
    let result: ICategoryPreference | undefined;
    service
      .updateCategory('leave_updates', { channelInApp: true, channelEmail: false })
      .subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/leave_updates`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ channelInApp: true, channelEmail: false });
    req.flush(updated);

    expect(result?.channelEmail).toBeFalse();
    expect(service.saving()).toBeFalse();
    const row = service.categories().find((c) => c.category === 'leave_updates');
    expect(row?.channelEmail).toBeFalse();
  });

  it('updateCategory clears saving on error (caller handles revert)', () => {
    service
      .updateCategory('leave_updates', { channelInApp: false, channelEmail: false })
      .subscribe({ next: () => {}, error: () => {} });
    httpMock
      .expectOne(`${base}/leave_updates`)
      .flush(
        { message: 'At least one channel must stay on.' },
        { status: 422, statusText: 'Unprocessable Entity' },
      );
    expect(service.saving()).toBeFalse();
  });

  it('updateQuietHours PUTs the body and updates the signal', () => {
    const qh: IQuietHours = {
      enabled: true,
      start: '23:00',
      end: '06:00',
      timezone: 'Europe/London',
    };
    let result: IQuietHours | undefined;
    service.updateQuietHours(qh).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/quiet-hours`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(qh);
    req.flush(qh);

    expect(result?.timezone).toBe('Europe/London');
    expect(service.quietHours().enabled).toBeTrue();
  });

  it('resetToDefaults POSTs and replaces the whole matrix', () => {
    let result: IPreferenceMatrix | undefined;
    service.resetToDefaults().subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/reset`);
    expect(req.request.method).toBe('POST');
    req.flush(matrix);

    expect(result?.categories.length).toBe(2);
    expect(service.categories().length).toBe(2);
    expect(service.quietHours().enabled).toBeTrue();
  });
});
