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
  ITenantUsageDashboard,
} from '../models/monitoring.models';

describe('PlatformMonitoringService', () => {
  let service: PlatformMonitoringService;
  let httpMock: HttpTestingController;

  // The system-admin monitoring namespace: …/api/v1/system/monitoring (the URL
  // the deployed AdminMonitoringController actually serves).
  const root = `${environment.apiBaseUrl}/system/monitoring`;

  // BE PlatformHealthDto — PascalCase enum strings, object jobQueue, array tenantsByStatus.
  const mockHealth: IPlatformHealth = {
    overallStatus: 'Healthy',
    activeTenantCount: 12,
    totalActiveUsers: 340,
    tenantsByStatus: [
      { status: 'Trial', count: 4 },
      { status: 'Active', count: 8 },
    ],
    databaseHealth: 'Healthy',
    redisHealth: 'NotConfigured',
    jobQueue: {
      available: true,
      enqueued: 2,
      processing: 1,
      scheduled: 0,
      succeeded: 100,
      failed: 0,
    },
    aggregateErrorRatePercent: null,
    p95LatencyMs: null,
    metricsStatus: 'RequiresObservabilityPipeline',
    generatedAtUtc: '2026-06-19T00:00:00Z',
  };

  // BE TenantUsageDashboardDto — an OBJECT wrapper, not a bare array.
  const mockDashboard: ITenantUsageDashboard = {
    tenants: [
      {
        tenantId: 't-1',
        name: 'Acme',
        subdomain: 'acme',
        status: 'Active',
        plan: 'starter',
        activeEmployees: 5,
        employeeLimit: 10,
        usagePercent: 50,
        band: 'Green',
        limitKnown: true,
        gauges: [
          {
            resource: 'Employees',
            available: true,
            used: 5,
            limit: 10,
            usagePercent: 50,
            band: 'Green',
          },
          {
            resource: 'Storage',
            available: false,
            used: null,
            limit: null,
            usagePercent: null,
            band: null,
          },
        ],
      },
    ],
    quotaBreachQueue: [],
    attentionRequiredQueue: [],
    attentionQueueStatus: 'RequiresObservabilityPipeline',
    generatedAtUtc: '2026-06-19T00:00:00Z',
  };

  const mockDetail: ITenantMonitoringDetail = {
    tenantId: 't-1',
    name: 'Acme',
    subdomain: 'acme',
    status: 'Active',
    plan: 'starter',
    ownerEmail: 'owner@acme.test',
    createdAt: '2026-06-01T00:00:00Z',
    lastActivityAt: null,
    employeeUsage: {
      resource: 'Employees',
      available: true,
      used: 5,
      limit: 10,
      usagePercent: 50,
      band: 'Green',
    },
    jobQueue: {
      available: true,
      enqueued: 0,
      processing: 0,
      scheduled: 0,
      succeeded: 5,
      failed: 1,
    },
    errorRateTrend24h: [],
    latencyTrend24h: [],
    topErrors: [],
    slaUptimePercent: null,
    metricsStatus: 'RequiresObservabilityPipeline',
    generatedAtUtc: '2026-06-19T00:00:00Z',
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

  it('GET /health hits the system monitoring root and maps the payload (AC-1)', () => {
    let result: IPlatformHealth | undefined;
    service.getHealth().subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${root}/health`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockHealth);

    expect(result).toEqual(mockHealth);
    expect(result?.metricsStatus).toBe('RequiresObservabilityPipeline');
    expect(result?.aggregateErrorRatePercent).toBeNull();
    expect(result?.jobQueue.enqueued).toBe(2);
  });

  it('GET /tenant-usage with no filters returns the dashboard wrapper (AC-2)', () => {
    let result: ITenantUsageDashboard | undefined;
    service.getTenantUsage().subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${root}/tenant-usage`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys().length).toBe(0);
    req.flush(mockDashboard);

    expect(result?.tenants.length).toBe(1);
    expect(result?.tenants[0].activeEmployees).toBe(5);
    expect(result?.tenants[0].gauges.length).toBe(2);
    expect(result?.quotaBreachQueue).toEqual([]);
    expect(result?.attentionQueueStatus).toBe('RequiresObservabilityPipeline');
  });

  it('GET /tenant-usage forwards all filter query params (FR-4)', () => {
    service
      .getTenantUsage({
        search: 'acme',
        status: 'Active',
        plan: 'starter',
        region: 'us-east',
        createdFrom: '2026-01-01',
        createdTo: '2026-06-01',
      })
      .subscribe();

    const req = httpMock.expectOne((r) => r.url === `${root}/tenant-usage`);
    expect(req.request.params.get('search')).toBe('acme');
    expect(req.request.params.get('status')).toBe('Active');
    expect(req.request.params.get('plan')).toBe('starter');
    expect(req.request.params.get('region')).toBe('us-east');
    expect(req.request.params.get('createdFrom')).toBe('2026-01-01');
    expect(req.request.params.get('createdTo')).toBe('2026-06-01');
    req.flush(mockDashboard);
  });

  it('GET /tenants/{id} hits the detail endpoint (AC-4)', () => {
    let result: ITenantMonitoringDetail | undefined;
    service.getTenantDetail('t-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${root}/tenants/t-1`);
    expect(req.request.method).toBe('GET');
    // BE serializes the lifecycle status as PascalCase ("Active").
    req.flush({ ...mockDetail, status: 'Active' });

    expect(result?.tenantId).toBe('t-1');
    expect(result?.slaUptimePercent).toBeNull();
    expect(result?.metricsStatus).toBe('RequiresObservabilityPipeline');
    expect(result?.jobQueue.succeeded).toBe(5);
    // The service normalises status to lowercase for the lifecycle gating helpers.
    expect(result?.status).toBe('active');
  });
});
