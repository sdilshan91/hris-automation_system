import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { TenantListComponent } from './tenant-list.component';
import { ITenantSummary } from '../../models/tenant.models';
import { environment } from '../../../../../../environments/environment';

describe('TenantListComponent', () => {
  let component: TenantListComponent;
  let fixture: ComponentFixture<TenantListComponent>;
  let httpMock: HttpTestingController;

  const tenantsUrl = `${environment.apiBaseUrl}/system/tenants`;

  const mockTenants: ITenantSummary[] = [
    {
      tenantId: 't-1',
      name: 'Acme Corp',
      subdomain: 'acme',
      status: 'trial',
      plan: 'Starter',
      createdAt: '2026-06-01T00:00:00Z',
    },
    {
      tenantId: 't-2',
      name: 'Globex',
      subdomain: 'globex',
      status: 'active',
      plan: 'Pro',
      createdAt: '2026-06-02T00:00:00Z',
    },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TenantListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TenantListComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('loads tenants on init (AC-4)', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne(tenantsUrl);
    expect(req.request.method).toBe('GET');
    req.flush(mockTenants);

    expect(component.tenants().length).toBe(2);
    expect(component.tenants()[0].name).toBe('Acme Corp');
    expect(component.isLoading()).toBeFalse();
  });

  it('shows a loading state initially', () => {
    expect(component.isLoading()).toBeTrue();
    fixture.detectChanges();
    httpMock.expectOne(tenantsUrl).flush(mockTenants);
    expect(component.isLoading()).toBeFalse();
  });

  it('handles API errors gracefully', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne(tenantsUrl);
    req.flush('error', { status: 500, statusText: 'Server Error' });

    expect(component.errorMessage()).toBeTruthy();
    expect(component.isLoading()).toBeFalse();
  });

  it('maps status to the correct badge class', () => {
    expect(component.statusClass('active')).toContain('green');
    expect(component.statusClass('trial')).toContain('blue');
    expect(component.statusClass('suspended')).toContain('amber');
    expect(component.statusClass('terminated')).toContain('red');
    expect(component.statusClass('unknown')).toContain('neutral');
  });
});
