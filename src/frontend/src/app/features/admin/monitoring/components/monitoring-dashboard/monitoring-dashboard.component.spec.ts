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
  ITenantUsageSummary,
} from '../../models/monitoring.models';

describe('MonitoringDashboardComponent', () => {
  let component: MonitoringDashboardComponent;
  let fixture: ComponentFixture<MonitoringDashboardComponent>;
  let httpMock: HttpTestingController;

  const root = `${environment.apiBaseUrl.replace(/\/v1$/, '')}/admin/monitoring`;
  const healthUrl = `${root}/health`;
  const tenantsUrl = `${root}/tenants`;

  const metricsUnavailableHealth: IPlatformHealth = {
    status: 'degraded',
    activeTenants: 7,
    activeUsers: 120,
    tenantsByStatus: { trial: 3, active: 4 },
    dbHealth: 'healthy',
    redisHealth: 'notConfigured',
    hangfire: { queued: 5, processing: 0, succeeded: 80, failed: 2 },
    errorRate: null,
    p95LatencyMs: null,
    metricsAvailable: false,
  };

  const metricsAvailableHealth: IPlatformHealth = {
    ...metricsUnavailableHealth,
    status: 'healthy',
    errorRate: 1.25,
    p95LatencyMs: 180,
    metricsAvailable: true,
  };

  const tenants: ITenantUsageSummary[] = [
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
    {
      tenantId: 't-2',
      name: 'Globex',
      subdomain: 'globex',
      status: 'trial',
      plan: 'Starter',
      employeeUsage: { used: 9, limit: 10, percent: 90, band: 'amber' },
      storageUsage: null,
      apiUsage: null,
      emailUsage: null,
    },
    {
      tenantId: 't-3',
      name: 'Initech',
      subdomain: 'initech',
      status: 'active',
      plan: 'Pro',
      employeeUsage: { used: 12, limit: 10, percent: 120, band: 'breached' },
      storageUsage: null,
      apiUsage: null,
      emailUsage: null,
    },
  ];

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

  /** Flush the health + tenants requests for one refresh cycle. */
  function flushCycle(
    health: IPlatformHealth = metricsAvailableHealth,
    rows: ITenantUsageSummary[] = tenants,
  ): void {
    httpMock.expectOne(healthUrl).flush(health);
    httpMock.expectOne(tenantsUrl).flush(rows);
  }

  it('loads health + tenants on init and renders KPIs (AC-1)', () => {
    fixture.detectChanges();
    flushCycle();
    fixture.detectChanges();

    expect(component.health()?.activeTenants).toBe(7);
    expect(component.tenants().length).toBe(3);

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="active-tenants"]')?.textContent).toContain('7');
    expect(el.querySelector('[data-testid="error-rate"]')?.textContent).toContain('1.25');
  });

  it('shows the "Not available" placeholder when metricsAvailable is false / errorRate null', () => {
    fixture.detectChanges();
    flushCycle(metricsUnavailableHealth);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="error-rate"]')).toBeNull();
    expect(el.querySelector('[data-testid="error-rate-na"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="p95-latency-na"]')).not.toBeNull();
    expect(el.textContent).toContain('requires observability pipeline');
  });

  it('polling triggers a refetch on the interval (AC-1)', fakeAsync(() => {
    fixture.detectChanges();
    flushCycle(); // initial load (no interval emission yet)

    // No outstanding requests right after the initial cycle.
    expect(httpMock.match(() => true).length).toBe(0);

    // Advance one polling interval -> a second refresh fires (health + tenants).
    tick(component.refreshIntervalMs);
    expect(httpMock.match(healthUrl).length).toBe(1);
    expect(httpMock.match(tenantsUrl).length).toBe(1);

    discardPeriodicTasks();
  }));

  it('manual refresh refetches health + tenants', () => {
    fixture.detectChanges();
    flushCycle();

    component.refresh();
    // A manual refresh issues exactly one health + one tenants request.
    expect(httpMock.match(healthUrl).length).toBe(1);
    expect(httpMock.match(tenantsUrl).length).toBe(1);
  });

  it('stops polling after destroy (takeUntil teardown)', fakeAsync(() => {
    fixture.detectChanges();
    flushCycle();

    fixture.destroy();
    // After destroy, advancing time must NOT issue new requests.
    tick(component.refreshIntervalMs * 2);
    expect(httpMock.match(() => true).length).toBe(0);
  }));

  it('maps gauge band to the correct colour class', () => {
    expect(component.bandClass('green')).toContain('green');
    expect(component.bandClass('amber')).toContain('amber');
    expect(component.bandClass('red')).toContain('red');
    expect(component.bandClass('breached')).toContain('red');
  });

  it('breach panel lists tenants at/above 80% (FR-3)', () => {
    fixture.detectChanges();
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
    req.flush([tenants[0]]);

    httpMock.verify();
  });

  it('surfaces an error message when health fails', () => {
    fixture.detectChanges();
    httpMock.expectOne(healthUrl).flush('err', { status: 500, statusText: 'Server Error' });
    httpMock.expectOne(tenantsUrl).flush(tenants);

    expect(component.healthError()).toBeTruthy();
    expect(component.healthLoading()).toBeFalse();
  });
});
