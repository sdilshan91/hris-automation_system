import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
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

  const overridesUrl = `${environment.apiBaseUrl}/system/tenants/t-1/plan-overrides`;

  const existing: IPlanLimitOverride[] = [
    { limitKey: 'maxEmployees', value: 500, expiresAt: null },
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

  function flushLoad(rows: IPlanLimitOverride[] = existing): void {
    fixture.detectChanges();
    httpMock.expectOne(overridesUrl).flush(rows);
    fixture.detectChanges();
  }

  it('loads the tenant overrides on init (AC-5)', () => {
    flushLoad();
    expect(component.overrides().length).toBe(1);
    const rows = fixture.nativeElement.querySelectorAll('[data-testid="override-row"]');
    expect(rows.length).toBe(1);
  });

  it('adds an override and PUTs the full set (AC-5)', () => {
    flushLoad([]);

    component.addForm.setValue({
      limitKey: 'maxWorkflows',
      value: 50,
      expiresAt: '',
    });
    component.add();

    const req = httpMock.expectOne(overridesUrl);
    expect(req.request.method).toBe('PUT');
    const body = req.request.body as IPlanLimitOverride[];
    expect(body.length).toBe(1);
    expect(body[0]).toEqual({
      limitKey: 'maxWorkflows',
      value: 50,
      expiresAt: null,
    });
    req.flush(body);

    expect(component.overrides().length).toBe(1);
  });

  it('removes an override and PUTs the reduced set (AC-5)', () => {
    flushLoad();

    component.remove('maxEmployees');

    const req = httpMock.expectOne(overridesUrl);
    expect(req.request.method).toBe('PUT');
    expect((req.request.body as IPlanLimitOverride[]).length).toBe(0);
    req.flush([]);

    expect(component.overrides().length).toBe(0);
  });

  it('does not submit an invalid add form', () => {
    flushLoad([]);
    component.addForm.setValue({ limitKey: '', value: null, expiresAt: '' });
    component.add();
    // No PUT issued — verify() in afterEach would fail if one were.
    expect(component.addForm.invalid).toBeTrue();
  });

  it('excludes already-overridden keys from the add dropdown', () => {
    flushLoad();
    const keys = component.availableKeys().map((f) => f.limitKey);
    expect(keys).not.toContain('maxEmployees');
    expect(keys).toContain('maxWorkflows');
  });
});
