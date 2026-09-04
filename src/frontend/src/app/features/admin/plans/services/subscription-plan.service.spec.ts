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
  IPlanCreateResult,
  PlanListItemWire,
  PlanDetailWire,
  PlanCreateResultWire,
  PlanLimitOverrideWire,
  emptyFeatureFlags,
} from '../models/plan.models';

describe('SubscriptionPlanService', () => {
  let service: SubscriptionPlanService;
  let httpMock: HttpTestingController;

  // Plans are rooted at /api/v1/system/plans (NOT /api/admin).
  const base = `${environment.apiBaseUrl}/system/plans`;
  // Overrides are plan-rooted, not a per-tenant sub-resource (BUG-471).
  const overrides = `${base}/overrides`;

  /**
   * D1 — these fixtures are the WIRE shapes (`Schema<'…'>`), not the view models.
   * Flushing the view model asserted that the service handled a body the API never
   * sends, which is exactly the drift this migration exists to catch.
   */
  const summariesWire: PlanListItemWire[] = [
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

  const detailWire: PlanDetailWire = {
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
    featureFlags: {
      sso: false,
      customDomain: false,
      whiteLabel: false,
      scim: false,
      sandbox: false,
    },
    auditLogRetentionDays: 90,
    slaTier: 'standard',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: null,
    activeTenantCount: 12,
  };

  /** What POST /system/plans ACTUALLY returns — id + code, nothing else. */
  const createResultWire: PlanCreateResultWire = { id: 'p-1', code: 'growth' };

  const overridesWire: PlanLimitOverrideWire[] = [
    {
      id: 'o-1',
      tenantId: 't-1',
      limitKey: 'max_employees',
      value: 500,
      expiresAt: null,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: null,
    },
  ];

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
    req.flush(summariesWire);

    expect(result?.length).toBe(1);
    expect(result?.[0].activeTenantCount).toBe(12);
  });

  it('defaults every optional wire field on a sparse plan list row', () => {
    let result: IPlanSummary[] | undefined;
    service.list().subscribe((r) => (result = r));

    // Every generated property is optional (Swashbuckle emits no `required`), so a
    // row with nulls must not produce `undefined` in the list template.
    httpMock.expectOne(base).flush([{ id: 'p-2', code: null, name: null }]);

    expect(result?.[0]).toEqual({
      id: 'p-2',
      code: '',
      name: '',
      priceMonthly: null,
      priceYearly: null,
      currency: '',
      isPublic: false,
      isActive: false,
      activeTenantCount: 0,
    });
  });

  it('GET /system/plans/{id} returns the full plan (AC-2)', () => {
    let result: IPlanDetail | undefined;
    service.get('p-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/p-1`);
    expect(req.request.method).toBe('GET');
    req.flush(detailWire);

    expect(result?.code).toBe('growth');
    // null limit is preserved (BR-3 "Unlimited").
    expect(result?.maxApiCallsPerMonth).toBeNull();
    expect(result?.featureFlags).toEqual(emptyFeatureFlags());
  });

  it('maps an absent featureFlags object to all-flags-off rather than undefined', () => {
    let result: IPlanDetail | undefined;
    service.get('p-1').subscribe((r) => (result = r));

    httpMock.expectOne(`${base}/p-1`).flush({ id: 'p-1', code: 'free' });

    expect(result?.featureFlags).toEqual(emptyFeatureFlags());
    expect(result?.enabledModules).toEqual([]);
    // Non-nullable int on the wire, "Unlimited" sentinel here when absent (BR-3).
    expect(result?.auditLogRetentionDays).toBeNull();
  });

  it('POST /system/plans returns only { id, code } — not a full plan (AC-2)', () => {
    let result: IPlanCreateResult | undefined;
    service.create(upsert).subscribe((r) => (result = r));

    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(upsert);
    // SubscriptionPlansCreatePlanResultDto — the endpoint has never sent a PlanDetail.
    req.flush(createResultWire);

    expect(result).toEqual({ id: 'p-1', code: 'growth' });
  });

  it('PUT /system/plans/{id} completes with no payload (AC-3)', () => {
    let completed = false;
    service.update('p-1', upsert).subscribe(() => (completed = true));

    const req = httpMock.expectOne(`${base}/p-1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(upsert);
    // Bare ApiResponse — the envelope interceptor leaves no `data` behind.
    req.flush(null);

    expect(completed).toBeTrue();
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

  it('GET /system/plans/overrides?tenantId= lists a tenant\'s overrides (AC-5, BUG-471)', () => {
    let result: IPlanLimitOverride[] | undefined;
    service.getOverrides('t-1').subscribe((r) => (result = r));

    // The tenant is a QUERY parameter on the plan-rooted route — the old
    // /system/tenants/{id}/plan-overrides address is not served by the API at all.
    const req = httpMock.expectOne(
      (r) => r.url === overrides && r.params.get('tenantId') === 't-1',
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(overridesWire);

    // `id` is retained (DELETE keys on it); tenantId/createdAt/updatedAt are dropped.
    expect(result).toEqual([
      { id: 'o-1', limitKey: 'max_employees', value: 500, expiresAt: null },
    ]);
  });

  it('POST /system/plans/overrides upserts ONE override and returns the row (AC-5, BUG-471)', () => {
    let result: IPlanLimitOverride | undefined;
    service
      .upsertOverride('t-1', {
        limitKey: 'max_employees',
        value: 500,
        expiresAt: null,
      })
      .subscribe((r) => (result = r));

    const req = httpMock.expectOne(overrides);
    expect(req.request.method).toBe('POST');
    // UpsertPlanLimitOverrideCommand — tenantId travels in the BODY, and the limit key
    // must be the canonical snake_case one or the BE returns limit_key_invalid.
    expect(req.request.body).toEqual({
      tenantId: 't-1',
      limitKey: 'max_employees',
      value: 500,
      expiresAt: null,
    });
    req.flush(overridesWire[0]);

    expect(result).toEqual({
      id: 'o-1',
      limitKey: 'max_employees',
      value: 500,
      expiresAt: null,
    });
  });

  it('DELETE /system/plans/overrides/{overrideId} removes one override by ID (AC-5, BUG-471)', () => {
    service.deleteOverride('o-1').subscribe();

    // Keyed on the override's own id — NOT its limitKey.
    const req = httpMock.expectOne(`${overrides}/o-1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
