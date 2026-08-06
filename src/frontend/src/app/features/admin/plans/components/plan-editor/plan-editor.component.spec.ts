import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideToastr } from 'ngx-toastr';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideTranslateService } from '@ngx-translate/core';
import { PlanEditorComponent } from './plan-editor.component';
import { environment } from '../../../../../../environments/environment';
import { IPlanDetail, IPlanUpsert, emptyFeatureFlags } from '../../models/plan.models';

describe('PlanEditorComponent', () => {
  let httpMock: HttpTestingController;

  const base = `${environment.apiBaseUrl}/system/plans`;

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
    maxApiCallsPerMonth: 100000,
    maxEmailSendsPerMonth: 10000,
    maxCustomRoles: 5,
    maxCustomFieldsPerEntity: 20,
    maxWorkflows: 10,
    enabledModules: ['CoreHR', 'Leave', 'Payroll'],
    featureFlags: { ...emptyFeatureFlags(), sso: true },
    auditLogRetentionDays: 90,
    slaTier: 'business',
  };

  /** Build the editor in create (id=null) or edit (id given) mode. */
  function setup(id: string | null): {
    fixture: ComponentFixture<PlanEditorComponent>;
    component: PlanEditorComponent;
  } {
    TestBed.configureTestingModule({
      imports: [PlanEditorComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideToastr(),
        provideAnimationsAsync(),
        provideTranslateService(),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: new Map(id ? [['id', id]] : []) },
          },
        },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PlanEditorComponent);
    return { fixture, component: fixture.componentInstance };
  }

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('toggling "Unlimited" nulls the limit and disables the input (BR-3)', () => {
    const { fixture, component } = setup(null);
    fixture.detectChanges();

    expect(component.isUnlimited('maxEmployees')).toBeFalse();
    component.toggleUnlimited('maxEmployees');

    expect(component.isUnlimited('maxEmployees')).toBeTrue();
    expect(component.limitsGroup.get('maxEmployees')?.value).toBeNull();
    expect(component.limitsGroup.get('maxEmployees')?.disabled).toBeTrue();

    // Un-toggling restores an editable 0.
    component.toggleUnlimited('maxEmployees');
    expect(component.isUnlimited('maxEmployees')).toBeFalse();
    expect(component.limitsGroup.get('maxEmployees')?.value).toBe(0);
    expect(component.limitsGroup.get('maxEmployees')?.enabled).toBeTrue();
  });

  it('renders the module grid with CoreHR disabled and checked (FR-6)', () => {
    const { fixture } = setup(null);
    fixture.detectChanges();

    const coreHr = fixture.nativeElement.querySelector(
      '[data-testid="module-CoreHR"]',
    ) as HTMLInputElement;
    const leave = fixture.nativeElement.querySelector(
      '[data-testid="module-Leave"]',
    ) as HTMLInputElement;

    expect(coreHr).not.toBeNull();
    expect(coreHr.disabled).toBeTrue();
    expect(coreHr.checked).toBeTrue();
    expect(leave.disabled).toBeFalse();
  });

  it('creates a plan with the correct body, including unlimited limits as null (AC-2)', () => {
    const { fixture, component } = setup(null);
    fixture.detectChanges();

    component.form.patchValue({
      code: 'pro',
      name: 'Pro',
      priceMonthly: 99,
      priceYearly: 990,
      currency: 'USD',
      trialDays: 30,
      slaTier: 'business',
    });
    // maxApiCallsPerMonth defaults to null ("Unlimited") — leave it so the body
    // carries null. Enable the Payroll module + the SCIM flag.
    expect(component.isUnlimited('maxApiCallsPerMonth')).toBeTrue();
    (component.form.get('modulesGroup.Payroll'))?.setValue(true);
    (component.form.get('flags.scim'))?.setValue(true);

    component.save();

    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    const body = req.request.body as IPlanUpsert;
    expect(body.code).toBe('pro');
    expect(body.maxApiCallsPerMonth).toBeNull(); // unlimited
    expect(body.enabledModules).toContain('CoreHR');
    expect(body.enabledModules).toContain('Payroll');
    expect(body.featureFlags.scim).toBeTrue();
    req.flush(detail);
  });

  it('renders the code field read-only when editing (FR-3)', () => {
    const { fixture, component } = setup('p-1');
    fixture.detectChanges();
    httpMock.expectOne(`${base}/p-1`).flush(detail);
    fixture.detectChanges();

    expect(component.isEdit()).toBeTrue();
    expect(component.form.get('code')?.disabled).toBeTrue();
    expect(component.form.get('code')?.value).toBe('growth');
    // Loaded module + flag state reflected.
    expect(component.form.get('modulesGroup.Payroll')?.value).toBeTrue();
    expect(component.form.get('flags.sso')?.value).toBeTrue();
  });

  it('surfaces the unique-code error on a 409 create (FR-3)', () => {
    const { fixture, component } = setup(null);
    fixture.detectChanges();
    component.form.patchValue({ code: 'growth', name: 'Growth', currency: 'USD' });

    component.save();
    httpMock.expectOne(base).flush('conflict', {
      status: 409,
      statusText: 'Conflict',
    });

    expect(component.formError()).toContain('already exists');
  });

  it('surfaces the delete-409 (referenced) error and suggests archiving (FR-7)', () => {
    const { fixture, component } = setup('p-1');
    fixture.detectChanges();
    httpMock.expectOne(`${base}/p-1`).flush(detail);
    fixture.detectChanges();

    component.remove();
    httpMock.expectOne(`${base}/p-1`).flush('referenced', {
      status: 409,
      statusText: 'Conflict',
    });

    expect(component.formError()).toContain('Archive it instead');
  });

  it('archives via POST {id}/archive (AC-4)', () => {
    const { fixture, component } = setup('p-1');
    fixture.detectChanges();
    httpMock.expectOne(`${base}/p-1`).flush(detail);
    fixture.detectChanges();

    component.archive();
    const req = httpMock.expectOne(`${base}/p-1/archive`);
    expect(req.request.method).toBe('POST');
    req.flush(null);
  });

  // ── ISSUE-358: flags with no feature behind them must not be sellable ──────────────────────────────
  //
  // Of the five plan feature flags only `sso` and `whiteLabel` gate real behaviour. The other three are
  // inert — SCIM middleware matches no controller, the custom-domain branch is self-documented as inert,
  // and the sandbox branch is unreachable (`requestedSandbox` is hard-coded false). Offering them as
  // toggles in the plan editor is selling capability the platform does not deliver, which is the charge
  // ISSUE-358 was raised on.
  it('disables the feature flags that gate nothing, and says why (ISSUE-358)', () => {
    const { fixture } = setup(null);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    for (const key of ['customDomain', 'scim', 'sandbox']) {
      const toggle = el.querySelector<HTMLInputElement>(`[data-testid="flag-${key}"]`);
      expect(toggle)
        .withContext(`${key} toggle should still be rendered, not deleted`)
        .toBeTruthy();
      expect(toggle!.hasAttribute('disabled'))
        .withContext(`${key} gates no feature, so it must not be sellable`)
        .toBeTrue();
      expect(toggle!.getAttribute('aria-describedby'))
        .withContext(`${key} must explain WHY it is disabled, or it reads as a bug`)
        .toBeTruthy();
    }
  });

  it('leaves the flags that DO gate behaviour enabled (ISSUE-358)', () => {
    const { fixture } = setup(null);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    for (const key of ['sso', 'whiteLabel']) {
      const toggle = el.querySelector<HTMLInputElement>(`[data-testid="flag-${key}"]`);
      expect(toggle!.hasAttribute('disabled'))
        .withContext(`${key} gates real behaviour and must stay sellable`)
        .toBeFalse();
    }
  });
});
