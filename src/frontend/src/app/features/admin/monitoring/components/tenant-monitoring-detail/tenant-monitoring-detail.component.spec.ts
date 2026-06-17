import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideTranslateService } from '@ngx-translate/core';
import { TenantMonitoringDetailComponent } from './tenant-monitoring-detail.component';
import { AuthService } from '../../../../../core/auth/auth.service';
import { environment } from '../../../../../../environments/environment';
import { ITenantMonitoringDetail } from '../../models/monitoring.models';

describe('TenantMonitoringDetailComponent', () => {
  let httpMock: HttpTestingController;

  const root = `${environment.apiBaseUrl.replace(/\/v1$/, '')}/admin/monitoring`;
  const detailUrl = `${root}/tenants/t-1`;
  // US-ADM-004: lifecycle history is loaded alongside the detail.
  const historyUrl = `${environment.apiBaseUrl}/system/tenants/t-1/lifecycle/history`;

  const detail: ITenantMonitoringDetail = {
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

  /** Build the component with an AuthService whose hasRole returns the given role. */
  function setup(role: 'System Admin' | 'System Support'): {
    fixture: ComponentFixture<TenantMonitoringDetailComponent>;
    component: TenantMonitoringDetailComponent;
  } {
    const authStub: Partial<AuthService> = {
      hasRole: (r: string) => r === role,
    };

    TestBed.configureTestingModule({
      imports: [TenantMonitoringDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideTranslateService(),
        { provide: AuthService, useValue: authStub },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['id', 't-1']]) } },
        },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(TenantMonitoringDetailComponent);
    const component = fixture.componentInstance;
    return { fixture, component };
  }

  /**
   * Flush both the detail GET and the lifecycle-history GET that the component
   * issues on init. `d` overrides the tenant detail; history defaults to empty.
   */
  function flushLoad(d: Partial<ITenantMonitoringDetail> = {}): void {
    httpMock.expectOne(detailUrl).flush({ ...detail, ...d });
    httpMock.expectOne(historyUrl).flush([]);
  }

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('loads the tenant detail on init (AC-4)', () => {
    const { fixture, component } = setup('System Admin');
    fixture.detectChanges();
    flushLoad();

    expect(component.detail()?.name).toBe('Acme');
    expect(component.isLoading()).toBeFalse();
  });

  it('shows Suspend + Terminate for an active tenant, keeps Audit disabled (US-ADM-004 BR-1)', () => {
    const { fixture } = setup('System Admin');
    fixture.detectChanges();
    flushLoad();
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    const suspend = el.querySelector('[data-testid="suspend-btn"]') as HTMLButtonElement;
    const terminate = el.querySelector('[data-testid="terminate-btn"]') as HTMLButtonElement;
    const audit = el.querySelector('[data-testid="audit-btn"]') as HTMLButtonElement;

    // Active → Suspend and Terminate are now wired and enabled.
    expect(suspend).not.toBeNull();
    expect(suspend.disabled).toBeFalse();
    expect(terminate).not.toBeNull();
    // Reactivate / Restore do not apply to an active tenant.
    expect(el.querySelector('[data-testid="reactivate-btn"]')).toBeNull();
    expect(el.querySelector('[data-testid="restore-btn"]')).toBeNull();
    // View Audit Log (US-ADM-008) is still a later release — stays disabled.
    expect(audit.disabled).toBeTrue();
  });

  it('shows Reactivate + Terminate for a suspended tenant (AC-5)', () => {
    const { fixture } = setup('System Admin');
    fixture.detectChanges();
    flushLoad({ status: 'suspended' });
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="reactivate-btn"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="terminate-btn"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="suspend-btn"]')).toBeNull();
  });

  it('shows Restore for a terminating tenant (AC-6)', () => {
    const { fixture } = setup('System Admin');
    fixture.detectChanges();
    flushLoad({ status: 'terminating' });
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="restore-btn"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="suspend-btn"]')).toBeNull();
    expect(el.querySelector('[data-testid="terminate-btn"]')).toBeNull();
  });

  it('shows NO lifecycle actions for a terminated tenant (BR-3)', () => {
    const { fixture } = setup('System Admin');
    fixture.detectChanges();
    flushLoad({ status: 'terminated' });
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="suspend-btn"]')).toBeNull();
    expect(el.querySelector('[data-testid="terminate-btn"]')).toBeNull();
    expect(el.querySelector('[data-testid="reactivate-btn"]')).toBeNull();
    expect(el.querySelector('[data-testid="restore-btn"]')).toBeNull();
  });

  it('opens the suspend modal when Suspend is clicked', () => {
    const { fixture, component } = setup('System Admin');
    fixture.detectChanges();
    flushLoad();
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    (el.querySelector('[data-testid="suspend-btn"]') as HTMLButtonElement).click();
    expect(component.suspendOpen()).toBeTrue();
  });

  it('ENABLES Impersonate for a non-terminated tenant (US-ADM-003 AC-1)', () => {
    const { fixture, component } = setup('System Admin');
    fixture.detectChanges();
    flushLoad();
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    const impersonate = el.querySelector(
      '[data-testid="impersonate-btn"]',
    ) as HTMLButtonElement;
    expect(impersonate.disabled).toBeFalse();

    impersonate.click();
    expect(component.impersonateOpen()).toBeTrue();
  });

  it('DISABLES Impersonate for a terminated tenant (BR-5)', () => {
    const { fixture } = setup('System Admin');
    fixture.detectChanges();
    flushLoad({ status: 'terminated' });
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    const impersonate = el.querySelector(
      '[data-testid="impersonate-btn"]',
    ) as HTMLButtonElement;
    expect(impersonate.disabled).toBeTrue();
  });

  it('HIDES lifecycle + impersonate actions for System Support (BR-7 read-only)', () => {
    const { fixture, component } = setup('System Support');
    fixture.detectChanges();
    flushLoad();
    fixture.detectChanges();

    expect(component.canManage()).toBeFalse();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="suspend-btn"]')).toBeNull();
    expect(el.querySelector('[data-testid="terminate-btn"]')).toBeNull();
    expect(el.querySelector('[data-testid="impersonate-btn"]')).toBeNull();
    // But the read-only View Audit Log button still renders (disabled).
    expect(el.querySelector('[data-testid="audit-btn"]')).not.toBeNull();
  });

  it('renders the lifecycle history timeline (US-ADM-004)', () => {
    const { fixture } = setup('System Admin');
    fixture.detectChanges();
    httpMock.expectOne(detailUrl).flush(detail);
    httpMock.expectOne(historyUrl).flush([
      {
        id: 'e-1',
        tenantId: 't-1',
        eventType: 'suspended',
        detailJson: '{"reason":"Payment failure"}',
        createdAt: '2026-06-17T10:00:00Z',
        createdBy: 'admin@yourhrm.com',
      },
    ]);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="history-list"]')).not.toBeNull();
  });

  it('shows "Not available" placeholders for null SLA / trends', () => {
    const { fixture } = setup('System Admin');
    fixture.detectChanges();
    flushLoad();
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="sla-na"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="detail-error-trend-na"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="last-activity-na"]')).not.toBeNull();
  });
});
