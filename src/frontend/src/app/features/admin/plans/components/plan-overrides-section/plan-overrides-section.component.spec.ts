import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideToastr } from 'ngx-toastr';
import { provideTranslateService } from '@ngx-translate/core';
import { PlanOverridesSectionComponent } from './plan-overrides-section.component';
import { environment } from '../../../../../../environments/environment';
import { IPlanLimitOverride } from '../../models/plan.models';

describe('PlanOverridesSectionComponent', () => {
  let fixture: ComponentFixture<PlanOverridesSectionComponent>;
  let component: PlanOverridesSectionComponent;
  let httpMock: HttpTestingController;

  // Plan-rooted overrides route; the tenant is a query param on GET (BUG-471).
  const overridesUrl = `${environment.apiBaseUrl}/system/plans/overrides`;

  const existing: IPlanLimitOverride[] = [
    { id: 'o-1', limitKey: 'max_employees', value: 500, expiresAt: null },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PlanOverridesSectionComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideToastr(),
        provideTranslateService(),
      ],
    });
    fixture = TestBed.createComponent(PlanOverridesSectionComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('tenantId', 't-1');
  });

  afterEach(() => httpMock.verify());

  function expectLoad(): TestRequest {
    return httpMock.expectOne(
      (r) => r.url === overridesUrl && r.params.get('tenantId') === 't-1',
    );
  }

  function flushLoad(rows: IPlanLimitOverride[] = existing): void {
    fixture.detectChanges();
    expectLoad().flush(rows);
    fixture.detectChanges();
  }

  it('loads the tenant overrides on init (AC-5)', () => {
    flushLoad();
    expect(component.overrides().length).toBe(1);
    const rows = fixture.nativeElement.querySelectorAll('[data-testid="override-row"]');
    expect(rows.length).toBe(1);
  });

  it('surfaces a failed load instead of rendering an empty list (BUG-471)', () => {
    fixture.detectChanges();
    expectLoad().flush(
      { message: 'nope' },
      { status: 404, statusText: 'Not Found' },
    );
    fixture.detectChanges();

    // A broken API must be visibly distinct from "this tenant has no overrides" —
    // the silent-empty fallback is what hid three live 404s.
    expect(component.loadError()).toBeTruthy();
    expect(
      fixture.nativeElement.querySelector('[data-testid="overrides-error"]'),
    ).toBeTruthy();
    expect(
      fixture.nativeElement.querySelector('[data-testid="overrides-empty"]'),
    ).toBeNull();
  });

  it('adds an override with ONE POST of the single-item command (AC-5, BUG-471)', () => {
    flushLoad([]);

    component.addForm.setValue({
      limitKey: 'max_workflows',
      value: 50,
      expiresAt: '',
    });
    component.add();

    const req = httpMock.expectOne(overridesUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      tenantId: 't-1',
      limitKey: 'max_workflows',
      value: 50,
      expiresAt: null,
    });
    req.flush({
      id: 'o-9',
      tenantId: 't-1',
      limitKey: 'max_workflows',
      value: 50,
      expiresAt: null,
    });

    // The row appended is the SERVER's, so it carries the id that DELETE needs.
    expect(component.overrides()).toEqual([
      { id: 'o-9', limitKey: 'max_workflows', value: 50, expiresAt: null },
    ]);
  });

  it('removes an override with a DELETE keyed on its id (AC-5, BUG-471)', () => {
    flushLoad();

    component.remove(existing[0]);

    const req = httpMock.expectOne(`${overridesUrl}/o-1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);

    expect(component.overrides().length).toBe(0);
  });

  it('does not submit an invalid add form', () => {
    flushLoad([]);
    component.addForm.setValue({ limitKey: '', value: null, expiresAt: '' });
    component.add();
    // No POST issued — verify() in afterEach would fail if one were.
    expect(component.addForm.invalid).toBeTrue();
  });

  it('offers exactly the canonical snake_case limit keys (BUG-472)', () => {
    flushLoad([]);

    // PlanLimitKeys (HRM.Domain/Authorization/PlanModules.cs) is the canonical
    // vocabulary the BE validator accepts; anything else is rejected as
    // `limit_key_invalid`. camelCase keys — and `auditLogRetentionDays`, which is
    // a plan column but NOT an override key — are exactly what BUG-472 was.
    expect(component.availableKeys().map((f) => f.limitKey)).toEqual([
      'max_employees',
      'max_storage_gb',
      'max_api_calls_per_month',
      'max_email_sends_per_month',
      'max_custom_roles',
      'max_custom_fields_per_entity',
      'max_workflows',
      'max_template_language_variants',
    ]);
  });

  it('excludes already-overridden keys from the add dropdown', () => {
    flushLoad();
    const keys = component.availableKeys().map((f) => f.limitKey);
    expect(keys).not.toContain('max_employees');
    expect(keys).toContain('max_workflows');
  });
});
