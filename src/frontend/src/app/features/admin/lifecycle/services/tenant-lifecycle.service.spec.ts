import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TenantLifecycleService } from './tenant-lifecycle.service';
import { environment } from '../../../../../environments/environment';
import {
  ITenantLifecycleEvent,
  ITenantLifecycleResult,
} from '../models/lifecycle.models';

describe('TenantLifecycleService', () => {
  let service: TenantLifecycleService;
  let httpMock: HttpTestingController;

  // Lifecycle is rooted at /api/v1/system/tenants (NOT /api/admin).
  const base = `${environment.apiBaseUrl}/system/tenants`;
  const tenantId = 't-1';
  const lifecycle = `${base}/${tenantId}/lifecycle`;

  const result: ITenantLifecycleResult = {
    tenantId,
    subdomain: 'acme',
    status: 'suspended',
    suspendedAt: '2026-06-17T10:00:00Z',
    suspendedReason: 'Payment failure',
    terminationScheduledAt: null,
    eventType: 'suspended',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        TenantLifecycleService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(TenantLifecycleService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('POST .../lifecycle/suspend sends the reason body (AC-1)', () => {
    let res: ITenantLifecycleResult | undefined;
    service
      .suspend(tenantId, { reason: 'Repeated ToS violations on the platform' })
      .subscribe((r) => (res = r));

    const req = httpMock.expectOne(`${lifecycle}/suspend`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    expect(req.request.body).toEqual({
      reason: 'Repeated ToS violations on the platform',
    });
    // The wire sends PascalCase (`Suspended`); the view model is lowercase. Flushing the view model here
    // is what let that translation go unverified — the spec asserted the service handled a body the API
    // does not send.
    req.flush({ ...result, status: 'Suspended' });

    expect(res?.status).toBe('suspended');
  });

  it('POST .../lifecycle/terminate sends { reason, graceDays } (AC-3)', () => {
    let res: ITenantLifecycleResult | undefined;
    service
      .terminate(tenantId, {
        reason: 'Account closed at customer request',
        graceDays: 30,
      })
      .subscribe((r) => (res = r));

    const req = httpMock.expectOne(`${lifecycle}/terminate`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      reason: 'Account closed at customer request',
      graceDays: 30,
    });
    req.flush({
      ...result,
      status: 'Terminating',
      eventType: 'termination_initiated',
      terminationScheduledAt: '2026-07-17T10:00:00Z',
    });

    expect(res?.status).toBe('terminating');
    expect(res?.terminationScheduledAt).toBeTruthy();
  });

  it('POST .../lifecycle/reactivate sends no body (AC-5)', () => {
    let res: ITenantLifecycleResult | undefined;
    service.reactivate(tenantId).subscribe((r) => (res = r));

    const req = httpMock.expectOne(`${lifecycle}/reactivate`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush({ ...result, status: 'Active', eventType: 'reactivated' });

    expect(res?.status).toBe('active');
  });

  it('POST .../lifecycle/restore sends no body (AC-6)', () => {
    let res: ITenantLifecycleResult | undefined;
    service.restore(tenantId).subscribe((r) => (res = r));

    const req = httpMock.expectOne(`${lifecycle}/restore`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush({ ...result, status: 'Suspended', eventType: 'restored' });

    expect(res?.status).toBe('suspended');
  });

  it('GET .../lifecycle/history returns the event timeline', () => {
    const events: ITenantLifecycleEvent[] = [
      {
        id: 'e-1',
        tenantId,
        eventType: 'suspended',
        detailJson: '{"reason":"Payment failure"}',
        createdAt: '2026-06-17T10:00:00Z',
        createdBy: 'admin@yourhrm.com',
      },
    ];

    let res: ITenantLifecycleEvent[] | undefined;
    service.getHistory(tenantId).subscribe((r) => (res = r));

    const req = httpMock.expectOne(`${lifecycle}/history`);
    expect(req.request.method).toBe('GET');
    req.flush(events);

    expect(res?.length).toBe(1);
    expect(res?.[0].eventType).toBe('suspended');
  });
});
