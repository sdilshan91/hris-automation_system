import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { PlatformMonitoringService } from './platform-monitoring.service';
import { environment } from '../../../../../environments/environment';
import {
  IPlatformHealth,
  ITenantMonitoringDetail,
  ITenantUsageSummary,
} from '../models/monitoring.models';

describe('PlatformMonitoringService', () => {
  let service: PlatformMonitoringService;
  let httpMock: HttpTestingController;

  const root = `${environment.apiBaseUrl.replace(/\/v1$/, '')}/admin/monitoring`;

  const mockHealth: IPlatformHealth = {
    status: 'healthy',
    activeTenants: 12,
    activeUsers: 340,
    tenantsByStatus: { trial: 4, active: 8 },
    dbHealth: 'healthy',
    redisHealth: 'notConfigured',
    hangfire: { queued: 2, processing: 1, succeeded: 100, failed: 0 },
    errorRate: null,
    p95LatencyMs: null,
    metricsAvailable: false,
  };

  const mockTenants: ITenantUsageSummary[] = [
    {
      tenantId: 't-1',
      name: 'Acme',
      subdomain: 'acme',
      status: 'active',
      plan: 'Pro',
      employeeUsage: { used: 5, limit: 10, percent: 50, band: 'green' },
      storageUsage: null,
      apiUsage: null,
      emailUsage: null,
    },
  ];

  const mockDetail: ITenantMonitoringDetail = {
    tenantId: 't-1',
    name: 'Acme',
    subdomain: 'acme',
    status: 'active',
    plan: 'Pro',
    ownerEmail: 'owner@acme.test',
    createdAt: '2026-06-01T00:00:00Z',
    lastActivityAt: null,
    employeeUsage: { used: 5, limit: 10, percent: 50, band: 'green' },
    hangfire: { queued: 0, processing: 0, succeeded: 5, failed: 1 },
    errorTrend: null,
    latencyTrend: null,
    slaUptime: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        PlatformMonitoringService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(PlatformMonitoringService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('GET /health hits the admin monitoring root and maps the payload (AC-1)', () => {
    let result: IPlatformHealth | undefined;
    service.getHealth().subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${root}/health`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockHealth);

    expect(result).toEqual(mockHealth);
    expect(result?.metricsAvailable).toBeFalse();
    expect(result?.errorRate).toBeNull();
  });

  it('GET /tenants with no filters sends no query params (AC-2)', () => {
    let result: ITenantUsageSummary[] | undefined;
    service.getTenantUsage().subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${root}/tenants`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys().length).toBe(0);
    req.flush(mockTenants);

    expect(result?.length).toBe(1);
    expect(result?.[0].storageUsage).toBeNull();
  });

  it('GET /tenants forwards all filter query params (FR-4)', () => {
    service
      .getTenantUsage({
        search: 'acme',
        status: 'active',
        plan: 'Pro',
        region: 'us-east',
        createdFrom: '2026-01-01',
        createdTo: '2026-06-01',
      })
      .subscribe();

    const req = httpMock.expectOne(
      (r) => r.url === `${root}/tenants`,
    );
    expect(req.request.params.get('search')).toBe('acme');
    expect(req.request.params.get('status')).toBe('active');
    expect(req.request.params.get('plan')).toBe('Pro');
    expect(req.request.params.get('region')).toBe('us-east');
    expect(req.request.params.get('createdFrom')).toBe('2026-01-01');
    expect(req.request.params.get('createdTo')).toBe('2026-06-01');
    req.flush(mockTenants);
  });

  it('GET /tenants/{id} hits the detail endpoint (AC-4)', () => {
    let result: ITenantMonitoringDetail | undefined;
    service.getTenantDetail('t-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${root}/tenants/t-1`);
    expect(req.request.method).toBe('GET');
    req.flush(mockDetail);

    expect(result?.tenantId).toBe('t-1');
    expect(result?.slaUptime).toBeNull();
    expect(result?.errorTrend).toBeNull();
  });
});
