import {
  ComponentFixture,
  TestBed,
  fakeAsync,
  tick,
  discardPeriodicTasks,
} from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { MonitoringDashboardComponent } from './monitoring-dashboard.component';
import { environment } from '../../../../../../environments/environment';
import {
  IPlatformHealth,
  ITenantUsageDashboard,
  ITenantUsageSummary,
} from '../../models/monitoring.models';

describe('MonitoringDashboardComponent', () => {
  let component: MonitoringDashboardComponent;
  let fixture: ComponentFixture<MonitoringDashboardComponent>;
  let httpMock: HttpTestingController;

  const root = `${environment.apiBaseUrl}/system/monitoring`;
  const healthUrl = `${root}/health`;
  const tenantsUrl = `${root}/tenant-usage`;

  const metricsUnavailableHealth: IPlatformHealth = {
    overallStatus: 'Degraded',
    activeTenantCount: 7,
    totalActiveUsers: 120,
    tenantsByStatus: [
      { status: 'Trial', count: 3 },
      { status: 'Active', count: 4 },
    ],
    databaseHealth: 'Healthy',
    redisHealth: 'NotConfigured',
    jobQueue: {
      available: true,
      enqueued: 5,
      processing: 0,
      scheduled: 0,
      succeeded: 80,
      failed: 2,
    },
    aggregateErrorRatePercent: null,
    p95LatencyMs: null,
    metricsStatus: 'RequiresObservabilityPipeline',
    generatedAtUtc: '2026-06-19T00:00:00Z',
  };

  const metricsAvailableHealth: IPlatformHealth = {
    ...metricsUnavailableHealth,
    overallStatus: 'Healthy',
    aggregateErrorRatePercent: 1.25,
    p95LatencyMs: 180,
    metricsStatus: 'Available',
  };

  const tenants: ITenantUsageSummary[] = [
    {
      tenantId: 't-1',
      name: 'Acme',
      subdomain: 'acme',
      status: 'Active',
      plan: 'Pro',
      activeEmployees: 5,
      employeeLimit: 10,
      usagePercent: 50,
      band: 'Green',
      limitKnown: true,
      gauges: [],
    },
    {
      tenantId: 't-2',
      name: 'Globex',
      subdomain: 'globex',
      status: 'Trial',
      plan: 'Starter',
      activeEmployees: 9,
      employeeLimit: 10,
      usagePercent: 90,
      band: 'Amber',
      limitKnown: true,
      gauges: [],
    },
    {
      tenantId: 't-3',
      name: 'Initech',
      subdomain: 'initech',
      status: 'Active',
      plan: 'Pro',
      activeEmployees: 12,
      employeeLimit: 10,
      usagePercent: 120,
      band: 'Breached',
      limitKnown: true,
      gauges: [],
    },
  ];

  function dashboard(
    rows: ITenantUsageSummary[] = tenants,
    breachQueue: ITenantUsageSummary[] = [],
  ): ITenantUsageDashboard {
    return {
      tenants: rows,
      quotaBreachQueue: breachQueue,
      attentionRequiredQueue: [],
      attentionQueueStatus: 'RequiresObservabilityPipeline',
      generatedAtUtc: '2026-06-19T00:00:00Z',
    };
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MonitoringDashboardComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MonitoringDashboardComponent);
    component = fixture.componentInstance;
  });

  /** Flush the health + tenant-usage requests for one refresh cycle. */
  function flushCycle(
    health: IPlatformHealth = metricsAvailableHealth,
    usage: ITenantUsageDashboard = dashboard(),
  ): void {
    httpMock.expectOne(healthUrl).flush(health);
    httpMock.expectOne(tenantsUrl).flush(usage);
  }

  it('loads health + tenants on init and renders KPIs (AC-1)', () => {
    fixture.detectChanges();
    flushCycle();
    fixture.detectChanges();

    expect(component.health()?.activeTenantCount).toBe(7);
    expect(component.tenants().length).toBe(3);

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="active-tenants"]')?.textContent).toContain('7');
    expect(el.querySelector('[data-testid="job-queue-enqueued"]')?.textContent).toContain('5');
    expect(el.querySelector('[data-testid="error-rate"]')?.textContent).toContain('1.25');
  });

  it('shows the "Not available" placeholder when error/latency metrics are null', () => {
    fixture.detectChanges();
    flushCycle(metricsUnavailableHealth);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="error-rate"]')).toBeNull();
    expect(el.querySelector('[data-testid="error-rate-na"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="p95-latency-na"]')).not.toBeNull();
    expect(el.textContent).toContain('requires observability pipeline');
  });

  it('reads the tenant rows from the dashboard wrapper object (not a bare array)', () => {
    fixture.detectChanges();
    flushCycle();

    expect(component.tenants().length).toBe(3);
    expect(component.tenants()[0].activeEmployees).toBe(5);
  });

  it('polling triggers a refetch on the interval (AC-1)', fakeAsync(() => {
    fixture.detectChanges();
    flushCycle(); // initial load (no interval emission yet)

    expect(httpMock.match(() => true).length).toBe(0);

    tick(component.refreshIntervalMs);
    expect(httpMock.match(healthUrl).length).toBe(1);
    expect(httpMock.match(tenantsUrl).length).toBe(1);

    discardPeriodicTasks();
  }));

  it('manual refresh refetches health + tenants', () => {
    fixture.detectChanges();
    flushCycle();

    component.refresh();
    expect(httpMock.match(healthUrl).length).toBe(1);
    expect(httpMock.match(tenantsUrl).length).toBe(1);
  });

  it('stops polling after destroy (takeUntil teardown)', fakeAsync(() => {
    fixture.detectChanges();
    flushCycle();

    fixture.destroy();
    tick(component.refreshIntervalMs * 2);
    expect(httpMock.match(() => true).length).toBe(0);
  }));

  it('maps gauge band to the correct colour class', () => {
    expect(component.bandClass('Green')).toContain('green');
    expect(component.bandClass('Amber')).toContain('amber');
    expect(component.bandClass('Red')).toContain('red');
    expect(component.bandClass('Breached')).toContain('red');
  });

  it('breach panel lists tenants at/above 80% derived from rows (FR-3)', () => {
    fixture.detectChanges();
    // Empty server breach queue -> the component derives from the visible rows.
    flushCycle();
    fixture.detectChanges();

    // Globex (90%) and Initech (120%) need attention; Acme (50%) does not.
    expect(component.attentionTenants().length).toBe(2);
    const ids = component.attentionTenants().map((t) => t.tenantId);
    expect(ids).toContain('t-2');
    expect(ids).toContain('t-3');
    expect(ids).not.toContain('t-1');

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="attention-count"]')?.textContent).toContain('2');
  });

  it('prefers the server-computed quota-breach queue when present', () => {
    fixture.detectChanges();
    flushCycle(metricsAvailableHealth, dashboard(tenants, [tenants[2]]));

    expect(component.attentionTenants().length).toBe(1);
    expect(component.attentionTenants()[0].tenantId).toBe('t-3');
  });

  it('records a "last updated" timestamp after a successful health load', () => {
    expect(component.lastUpdated()).toBeNull();
    fixture.detectChanges();
    flushCycle();
    expect(component.lastUpdated()).not.toBeNull();
  });

  it('applying filters re-queries tenants with the search term', () => {
    fixture.detectChanges();
    flushCycle();

    component.searchTerm.set('acme');
    component.applyFilters();

    const req = httpMock.expectOne((r) => r.url === tenantsUrl);
    expect(req.request.params.get('search')).toBe('acme');
    req.flush(dashboard([tenants[0]]));

    httpMock.verify();
  });

  it('surfaces an error message when health fails', () => {
    fixture.detectChanges();
    httpMock.expectOne(healthUrl).flush('err', { status: 500, statusText: 'Server Error' });
    httpMock.expectOne(tenantsUrl).flush(dashboard());

    expect(component.healthError()).toBeTruthy();
    expect(component.healthLoading()).toBeFalse();
  });
});
