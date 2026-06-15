import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { NewPayrollRunComponent } from './new-payroll-run.component';
import { PayrollRunService } from '../../services/payroll-run.service';
import {
  IPayrollRun,
  IPayrollRunValidation,
} from '../../models/payroll-run.models';

describe('NewPayrollRunComponent', () => {
  let fixture: ComponentFixture<NewPayrollRunComponent>;
  let component: NewPayrollRunComponent;
  let runs: jasmine.SpyObj<PayrollRunService>;
  let toastr: jasmine.SpyObj<ToastrService>;

  const ready: IPayrollRunValidation = {
    totalEmployees: 250,
    readyEmployees: 247,
    missingSalaryStructure: 3,
    canRun: true,
    blockers: [],
  };
  const blocked: IPayrollRunValidation = {
    totalEmployees: 250,
    readyEmployees: 247,
    missingSalaryStructure: 3,
    canRun: false,
    blockers: ['This period is already finalized.'],
  };
  const createdRun: IPayrollRun = {
    id: 'r-9',
    payMonth: 5,
    payYear: 2026,
    status: 'Queued',
    totalEmployees: 0,
    processedEmployees: 0,
    skippedEmployees: 0,
    totalGross: 0,
    totalDeductions: 0,
    totalNet: 0,
    initiatedByName: 'Alex HR',
    initiatedAt: '2026-05-31T10:00:00Z',
    completedAt: null,
  };

  beforeEach(async () => {
    runs = jasmine.createSpyObj<PayrollRunService>('PayrollRunService', [
      'validateRun',
      'initiateRun',
    ]);
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    runs.validateRun.and.returnValue(of(ready));
    runs.initiateRun.and.returnValue(of(createdRun));

    await TestBed.configureTestingModule({
      imports: [NewPayrollRunComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: PayrollRunService, useValue: runs },
        { provide: ToastrService, useValue: toastr },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(NewPayrollRunComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('validates the default period on init', () => {
    expect(component).toBeTruthy();
    expect(runs.validateRun).toHaveBeenCalledTimes(1);
    expect(component.validation()).toEqual(ready);
    expect(component.canSubmit()).toBeTrue();
  });

  it('renders the readiness summary', () => {
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('247');
    expect(text).toContain('ready');
    expect(text).toContain('missing salary structure');
  });

  it('re-validates when the month changes', () => {
    component.onMonthChange(3);
    expect(component.payMonth()).toBe(3);
    expect(runs.validateRun).toHaveBeenCalledTimes(2);
    expect(runs.validateRun).toHaveBeenCalledWith(
      jasmine.objectContaining({ payMonth: 3 }),
    );
  });

  it('re-validates when the year changes', () => {
    component.onYearChange(2025);
    expect(component.payYear()).toBe(2025);
    expect(runs.validateRun).toHaveBeenCalledWith(
      jasmine.objectContaining({ payYear: 2025 }),
    );
  });

  it('disables submit when validation says the period cannot run (AC-4)', () => {
    runs.validateRun.and.returnValue(of(blocked));
    component.validate();
    fixture.detectChanges();
    expect(component.canSubmit()).toBeFalse();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('already finalized');
  });

  it('sets an error state when validation fails', () => {
    runs.validateRun.and.returnValue(throwError(() => new Error('boom')));
    component.validate();
    expect(component.validationError()).toBeTrue();
    expect(component.canSubmit()).toBeFalse();
  });

  describe('submit', () => {
    it('initiates the run with an Idempotency-Key and emits the created run', () => {
      const emitted: IPayrollRun[] = [];
      component.created.subscribe((r) => emitted.push(r));

      component.submit();

      expect(runs.initiateRun).toHaveBeenCalledTimes(1);
      const key = runs.initiateRun.calls.mostRecent().args[1];
      expect(typeof key).toBe('string');
      expect(key.length).toBeGreaterThan(0);
      expect(toastr.success).toHaveBeenCalled();
      expect(emitted).toEqual([createdRun]);
      expect(component.submitting()).toBeFalse();
    });

    it('does nothing when submit is not allowed', () => {
      runs.validateRun.and.returnValue(of(blocked));
      component.validate();
      component.submit();
      expect(runs.initiateRun).not.toHaveBeenCalled();
    });

    it('shows a conflict message and re-validates on 409 (AC-4)', () => {
      runs.initiateRun.and.returnValue(
        throwError(
          () => new HttpErrorResponse({ status: 409, statusText: 'Conflict' }),
        ),
      );
      runs.validateRun.calls.reset();

      component.submit();

      expect(toastr.error).toHaveBeenCalled();
      expect(runs.validateRun).toHaveBeenCalled(); // re-validated
      expect(component.submitting()).toBeFalse();
    });

    it('shows a generic error on a non-409 failure', () => {
      runs.initiateRun.and.returnValue(
        throwError(
          () => new HttpErrorResponse({ status: 500, statusText: 'Server' }),
        ),
      );
      component.submit();
      expect(toastr.error).toHaveBeenCalled();
      expect(component.submitting()).toBeFalse();
    });
  });
});
