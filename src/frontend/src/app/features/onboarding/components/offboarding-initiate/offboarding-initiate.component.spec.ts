import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter, ActivatedRoute, Router } from '@angular/router';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { OffboardingInitiateComponent } from './offboarding-initiate.component';
import { OffboardingService } from '../../services/offboarding.service';
import { IOffboardingInstance } from '../../models/offboarding.models';

describe('OffboardingInitiateComponent', () => {
  let component: OffboardingInitiateComponent;
  let fixture: ComponentFixture<OffboardingInitiateComponent>;
  let serviceSpy: jasmine.SpyObj<OffboardingService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;
  let router: Router;
  let navigateSpy: jasmine.Spy;

  const created: IOffboardingInstance = {
    id: 'off-9',
    employeeId: 'emp-1',
    employeeName: 'Jane Doe',
    lastWorkingDay: '2026-12-31',
    reason: 'Resignation',
    status: 'in_progress',
    overallClearance: 'pending',
    progressPercent: 0,
    departments: [],
  };

  async function setup(query: Record<string, string> = {}): Promise<void> {
    serviceSpy = jasmine.createSpyObj('OffboardingService', ['initiate']);
    serviceSpy.initiate.and.returnValue(of(created));
    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error']);

    await TestBed.configureTestingModule({
      imports: [OffboardingInitiateComponent],
      providers: [
        provideAnimationsAsync(),
        provideRouter([]),
        { provide: OffboardingService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: () => 'emp-1' },
              queryParamMap: { get: (k: string) => query[k] ?? null },
            },
          },
        },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
    navigateSpy = spyOn(router, 'navigate').and.resolveTo(true);

    fixture = TestBed.createComponent(OffboardingInitiateComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('employeeId', 'emp-1');
    fixture.detectChanges();
  }

  it('creates with a valid default form (today LWD, Resignation reason)', async () => {
    await setup();
    expect(component).toBeTruthy();
    expect(component.form.valid).toBeTrue();
    expect(component.form.controls.reason.value).toBe('Resignation');
  });

  it('rejects a past last working day', async () => {
    await setup();
    component.form.controls.lastWorkingDay.setValue('2000-01-01');
    expect(component.form.controls.lastWorkingDay.hasError('pastDate')).toBeTrue();
    expect(component.form.invalid).toBeTrue();
  });

  it('does not submit when the form is invalid', async () => {
    await setup();
    component.form.controls.lastWorkingDay.setValue('');
    component.submit();
    expect(serviceSpy.initiate).not.toHaveBeenCalled();
  });

  it('reads the employee name from the route query param', async () => {
    await setup({ employeeName: 'Jane Doe' });
    expect(component.employeeName()).toBe('Jane Doe');
  });

  it('submits the request and navigates to the new dashboard (AC-1)', async () => {
    await setup({ employeeName: 'Jane Doe' });
    component.form.patchValue({
      reason: 'Termination',
      lastWorkingDay: '2026-12-31',
      offboardingTemplateId: '  tpl-1  ',
      notes: '  bye  ',
    });
    component.submit();

    expect(serviceSpy.initiate).toHaveBeenCalledWith({
      employeeId: 'emp-1',
      lastWorkingDay: '2026-12-31',
      offboardingTemplateId: 'tpl-1',
      reason: 'Termination',
      notes: 'bye',
    });
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith(['/offboarding', 'off-9']);
    expect(component.submitting()).toBeFalse();
  });

  it('sends null for blank optional fields', async () => {
    await setup();
    component.submit();
    expect(serviceSpy.initiate).toHaveBeenCalledWith(
      jasmine.objectContaining({ offboardingTemplateId: null, notes: null }),
    );
  });

  it('surfaces a BR-1 conflict inline without navigating (AC-1)', async () => {
    await setup();
    serviceSpy.initiate.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            error: { message: 'Employee is still active.' },
            status: 409,
          }),
      ),
    );
    component.submit();

    expect(component.submitError()).toBe('Employee is still active.');
    expect(navigateSpy).not.toHaveBeenCalled();
    expect(component.submitting()).toBeFalse();
  });
});
