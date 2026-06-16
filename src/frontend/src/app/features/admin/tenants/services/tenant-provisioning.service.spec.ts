import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TenantProvisioningService } from './tenant-provisioning.service';
import {
  IProvisionTenantRequest,
  IProvisionTenantResponse,
  ISubdomainAvailability,
  ISubscriptionPlan,
  ITenantSummary,
} from '../models/tenant.models';
import { environment } from '../../../../../environments/environment';

describe('TenantProvisioningService', () => {
  let service: TenantProvisioningService;
  let httpMock: HttpTestingController;

  // Admin console is rooted at /api/admin (the /v1 tenant suffix is stripped).
  const adminUrl = `${environment.apiBaseUrl.replace(/\/v1$/, '')}/admin`;

  const mockPlans: ISubscriptionPlan[] = [
    { id: 'plan-1', name: 'Starter', code: 'STARTER', priceMonthly: 0, trialDays: 14, maxEmployees: 25 },
    { id: 'plan-2', name: 'Pro', code: 'PRO', priceMonthly: 49, trialDays: 0, maxEmployees: 200 },
  ];

  const mockTenants: ITenantSummary[] = [
    {
      tenantId: 't-1',
      name: 'Acme Corp',
      subdomain: 'acme',
      status: 'trial',
      plan: 'Starter',
      createdAt: '2026-06-01T00:00:00Z',
    },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        TenantProvisioningService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(TenantProvisioningService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getSubscriptionPlans', () => {
    it('GETs active plans for the picker', () => {
      let result: ISubscriptionPlan[] | undefined;
      service.getSubscriptionPlans().subscribe((p) => (result = p));

      const req = httpMock.expectOne(`${adminUrl}/subscription-plans`);
      expect(req.request.method).toBe('GET');
      req.flush(mockPlans);

      expect(result?.length).toBe(2);
      expect(result?.[0].name).toBe('Starter');
    });
  });

  describe('getTenants', () => {
    it('GETs the tenant list (AC-4)', () => {
      let result: ITenantSummary[] | undefined;
      service.getTenants().subscribe((t) => (result = t));

      const req = httpMock.expectOne(`${adminUrl}/tenants`);
      expect(req.request.method).toBe('GET');
      req.flush(mockTenants);

      expect(result?.length).toBe(1);
      expect(result?.[0].subdomain).toBe('acme');
    });
  });

  describe('checkSubdomainAvailability', () => {
    it('GETs availability with the subdomain as a query param (AC-2)', () => {
      const response: ISubdomainAvailability = { available: true };
      let result: ISubdomainAvailability | undefined;

      service.checkSubdomainAvailability('acme').subscribe((r) => (result = r));

      const req = httpMock.expectOne(
        `${adminUrl}/tenants/subdomain-available?subdomain=acme`,
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.params.get('subdomain')).toBe('acme');
      req.flush(response);

      expect(result?.available).toBeTrue();
    });

    it('maps a taken subdomain to available:false with a reason', () => {
      let result: ISubdomainAvailability | undefined;
      service.checkSubdomainAvailability('admin').subscribe((r) => (result = r));

      const req = httpMock.expectOne(
        `${adminUrl}/tenants/subdomain-available?subdomain=admin`,
      );
      req.flush({ available: false, reason: 'Subdomain is reserved' });

      expect(result?.available).toBeFalse();
      expect(result?.reason).toBe('Subdomain is reserved');
    });
  });

  describe('provisionTenant', () => {
    it('POSTs the provisioning request body and maps the response (AC-1)', () => {
      const request: IProvisionTenantRequest = {
        name: 'Acme Corp',
        subdomain: 'acme',
        subscriptionPlanId: 'plan-1',
        ownerEmail: 'owner@acme.com',
        region: 'us-east',
        trialDays: 14,
      };
      const response: IProvisionTenantResponse = {
        tenantId: 't-1',
        subdomain: 'acme',
        status: 'trial',
        createdAt: '2026-06-01T00:00:00Z',
      };

      let result: IProvisionTenantResponse | undefined;
      service.provisionTenant(request).subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${adminUrl}/tenants`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      req.flush(response);

      expect(result?.tenantId).toBe('t-1');
      expect(result?.status).toBe('trial');
    });
  });
});
