import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { SubscriptionPlanService } from './subscription-plan.service';
import { environment } from '../../../../../environments/environment';
import {
  IPlanSummary,
  IPlanDetail,
  IPlanUpsert,
  IPlanLimitOverride,
  emptyFeatureFlags,
} from '../models/plan.models';

describe('SubscriptionPlanService', () => {
  let service: SubscriptionPlanService;
  let httpMock: HttpTestingController;

  // Plans are rooted at /api/v1/system/plans (NOT /api/admin).
  const base = `${environment.apiBaseUrl}/system/plans`;
  const tenants = `${environment.apiBaseUrl}/system/tenants`;

  const summaries: IPlanSummary[] = [
    {
      id: 'p-1',
      code: 'growth',
      name: 'Growth',
      priceMonthly: 49,
      priceYearly: 490,
      currency: 'USD',
      isPublic: true,
      isActive: true,
      activeTenantCount: 12,
    },
  ];

  const detail: IPlanDetail = {
    id: 'p-1',
    code: 'growth',
    name: 'Growth',
    description: 'For growing teams',
    isPublic: true,
    isActive: true,
    priceMonthly: 49,
    priceYearly: 490,
    currency: 'USD',
    trialDays: 14,
    maxEmployees: 100,
    maxStorageGb: 50,
    maxApiCallsPerMonth: null,
    maxEmailSendsPerMonth: 10000,
    maxCustomRoles: 5,
    maxCustomFieldsPerEntity: 20,
    maxWorkflows: 10,
    enabledModules: ['CoreHR', 'Leave', 'Attendance'],
    featureFlags: emptyFeatureFlags(),
    auditLogRetentionDays: 90,
    slaTier: 'standard',
  };

  const upsert: IPlanUpsert = {
    code: 'growth',
    name: 'Growth',
    description: 'For growing teams',
    isPublic: true,
    isActive: true,
    priceMonthly: 49,
    priceYearly: 490,
    currency: 'USD',
    trialDays: 14,
    maxEmployees: 100,
    maxStorageGb: 50,
    maxApiCallsPerMonth: null,
    maxEmailSendsPerMonth: 10000,
    maxCustomRoles: 5,
    maxCustomFieldsPerEntity: 20,
    maxWorkflows: 10,
    enabledModules: ['CoreHR', 'Leave'],
    featureFlags: emptyFeatureFlags(),
    auditLogRetentionDays: 90,
    slaTier: 'standard',
  };

  const overrides: IPlanLimitOverride[] = [
    { limitKey: 'maxEmployees', value: 500, expiresAt: null },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        SubscriptionPlanService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(SubscriptionPlanService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('GET /system/plans lists plans with the active tenant count (AC-1/FR-5)', () => {
    let result: IPlanSummary[] | undefined;
    service.list().subscribe((r) => (result = r));

    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(summaries);

    expect(result?.length).toBe(1);
    expect(result?.[0].activeTenantCount).toBe(12);
  });

  it('GET /system/plans/{id} returns the full plan (AC-2)', () => {
    let result: IPlanDetail | undefined;
    service.get('p-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/p-1`);
    expect(req.request.method).toBe('GET');
    req.flush(detail);

    expect(result?.code).toBe('growth');
    // null limit is preserved (BR-3 "Unlimited").
    expect(result?.maxApiCallsPerMonth).toBeNull();
  });

  it('POST /system/plans creates a plan with the full body (AC-2)', () => {
    let result: IPlanDetail | undefined;
    service.create(upsert).subscribe((r) => (result = r));

    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(upsert);
    req.flush(detail);

    expect(result?.id).toBe('p-1');
  });

  it('PUT /system/plans/{id} updates a plan (AC-3)', () => {
    service.update('p-1', upsert).subscribe();

    const req = httpMock.expectOne(`${base}/p-1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(upsert);
    req.flush(detail);
  });

  it('POST /system/plans/{id}/archive archives a plan (AC-4)', () => {
    service.archive('p-1').subscribe();

    const req = httpMock.expectOne(`${base}/p-1/archive`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush(null);
  });

  it('DELETE /system/plans/{id} deletes a plan (FR-7)', () => {
    service.delete('p-1').subscribe();

    const req = httpMock.expectOne(`${base}/p-1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('GET /system/tenants/{id}/plan-overrides lists overrides (AC-5)', () => {
    let result: IPlanLimitOverride[] | undefined;
    service.getOverrides('t-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${tenants}/t-1/plan-overrides`);
    expect(req.request.method).toBe('GET');
    req.flush(overrides);

    expect(result?.[0].limitKey).toBe('maxEmployees');
    expect(result?.[0].value).toBe(500);
  });

  it('PUT /system/tenants/{id}/plan-overrides replaces the override set (AC-5)', () => {
    service.saveOverrides('t-1', overrides).subscribe();

    const req = httpMock.expectOne(`${tenants}/t-1/plan-overrides`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(overrides);
    req.flush(overrides);
  });

  it('DELETE /system/tenants/{id}/plan-overrides/{key} removes one override', () => {
    service.deleteOverride('t-1', 'maxEmployees').subscribe();

    const req = httpMock.expectOne(`${tenants}/t-1/plan-overrides/maxEmployees`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
