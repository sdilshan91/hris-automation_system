import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { tenantInterceptor } from './tenant.interceptor';
import { TenantService } from '../tenant/tenant.service';

describe('tenantInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let tenantService: jasmine.SpyObj<TenantService>;

  beforeEach(() => {
    tenantService = jasmine.createSpyObj<TenantService>('TenantService', [
      'requestSubdomain',
    ]);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([tenantInterceptor])),
        provideHttpClientTesting(),
        { provide: TenantService, useValue: tenantService },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('adds the resolved tenant subdomain header to outgoing requests', () => {
    tenantService.requestSubdomain.and.returnValue('acme');

    http.get('/api/v1/auth/login').subscribe();
    const request = httpMock.expectOne('/api/v1/auth/login');

    expect(request.request.headers.get('X-Tenant-Subdomain')).toBe('acme');
    request.flush({});
  });

  it('does not overwrite an explicit tenant subdomain header', () => {
    tenantService.requestSubdomain.and.returnValue('acme');

    http
      .get('/api/v1/auth/login', {
        headers: { 'X-Tenant-Subdomain': 'override' },
      })
      .subscribe();
    const request = httpMock.expectOne('/api/v1/auth/login');

    expect(request.request.headers.get('X-Tenant-Subdomain')).toBe('override');
    request.flush({});
  });
});

