import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TenantService } from './tenant.service';
import { environment } from '../../../environments/environment';

describe('TenantService', () => {
  let service: TenantService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(TenantService);
    httpMock = TestBed.inject(HttpTestingController);
    document.documentElement.style.removeProperty('--brand-primary');
  });

  afterEach(() => {
    httpMock.verify();
    document.documentElement.style.removeProperty('--brand-primary');
  });

  it('extracts tenant subdomains for localhost and production base domains', () => {
    expect(service.extractSubdomain('acme.localhost')).toBe('acme');
    expect(service.extractSubdomain('localhost')).toBe('');
  });

  it('marks reserved subdomains as non-tenant routes', async () => {
    await service.resolveForHostname('www.localhost');

    expect(service.tenantContext()).toEqual(
      jasmine.objectContaining({
        subdomain: 'www',
        isReserved: true,
        isValid: false,
        state: 'reserved',
      })
    );
  });

  it('uses the dev fallback subdomain when running on plain localhost', async () => {
    const resolution = service.resolveForHostname('localhost');
    const request = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/context`);

    expect(request.request.headers.get('X-Tenant-Subdomain')).toBe('platform');

    request.flush({}, { status: 404, statusText: 'Not Found' });
    await resolution;

    expect(service.tenantContext()).toEqual(
      jasmine.objectContaining({
        subdomain: 'platform',
        isValid: true,
        state: 'fallback',
      })
    );
  });

  it('hydrates branding and suspended status from the tenant context API', async () => {
    const resolution = service.resolveForHostname('acme.localhost');
    const request = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/context`);

    request.flush({
      data: {
        tenantId: 'tenant-1',
        subdomain: 'acme',
        name: 'Acme People',
        primaryColor: '#14532d',
        status: 'suspended',
        suspensionReason: 'Billing review required.',
      },
    });
    await resolution;

    expect(service.tenantContext()).toEqual(
      jasmine.objectContaining({
        tenantId: 'tenant-1',
        name: 'Acme People',
        state: 'suspended',
        suspensionReason: 'Billing review required.',
      })
    );
    expect(service.isTenantSuspended()).toBeTrue();
    expect(document.documentElement.style.getPropertyValue('--brand-primary')).toBe(
      '#14532d'
    );
  });

  it('marks real subdomain requests as not found when backend reports a missing workspace', async () => {
    const resolution = service.resolveForHostname('unknown.localhost');
    const request = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/context`);

    request.flush(
      { error: 'This workspace does not exist.' },
      { status: 404, statusText: 'Not Found' }
    );
    await resolution;

    expect(service.isWorkspaceNotFound()).toBeTrue();
    expect(service.tenantContext()).toEqual(
      jasmine.objectContaining({
        subdomain: 'unknown',
        isValid: false,
        state: 'not-found',
      })
    );
  });
});

