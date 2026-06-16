import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { TenantMonitoringDetailComponent } from './tenant-monitoring-detail.component';
import { AuthService } from '../../../../../core/auth/auth.service';
import { environment } from '../../../../../../environments/environment';
import { ITenantMonitoringDetail } from '../../models/monitoring.models';

describe('TenantMonitoringDetailComponent', () => {
  let httpMock: HttpTestingController;

  const root = `${environment.apiBaseUrl.replace(/\/v1$/, '')}/admin/monitoring`;
  const detailUrl = `${root}/tenants/t-1`;

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

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('loads the tenant detail on init (AC-4)', () => {
    const { fixture, component } = setup('System Admin');
    fixture.detectChanges();
    httpMock.expectOne(detailUrl).flush(detail);

    expect(component.detail()?.name).toBe('Acme');
    expect(component.isLoading()).toBeFalse();
  });

  it('renders quick-action buttons as DISABLED (point to a later release)', () => {
    const { fixture } = setup('System Admin');
    fixture.detectChanges();
    httpMock.expectOne(detailUrl).flush(detail);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    const buttons = el.querySelectorAll(
      '[data-testid="quick-actions"] button',
    );
    expect(buttons.length).toBeGreaterThan(0);
    buttons.forEach((b) => expect((b as HTMLButtonElement).disabled).toBeTrue());
  });

  it('shows Suspend/Impersonate for System Admin (BR-1)', () => {
    const { fixture } = setup('System Admin');
    fixture.detectChanges();
    httpMock.expectOne(detailUrl).flush(detail);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="suspend-btn"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="impersonate-btn"]')).not.toBeNull();
    // View Audit Log is visible to all roles.
    expect(el.querySelector('[data-testid="audit-btn"]')).not.toBeNull();
  });

  it('HIDES Suspend/Impersonate for System Support (BR-1 read-only)', () => {
    const { fixture, component } = setup('System Support');
    fixture.detectChanges();
    httpMock.expectOne(detailUrl).flush(detail);
    fixture.detectChanges();

    expect(component.canManage()).toBeFalse();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="suspend-btn"]')).toBeNull();
    expect(el.querySelector('[data-testid="impersonate-btn"]')).toBeNull();
    // But the read-only View Audit Log button still renders (disabled).
    expect(el.querySelector('[data-testid="audit-btn"]')).not.toBeNull();
  });

  it('shows "Not available" placeholders for null SLA / trends', () => {
    const { fixture } = setup('System Admin');
    fixture.detectChanges();
    httpMock.expectOne(detailUrl).flush(detail);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="sla-na"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="detail-error-trend-na"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="last-activity-na"]')).not.toBeNull();
  });
});
