import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { LeaveEncashmentFormComponent } from './leave-encashment-form.component';
import { ReconciliationService } from '../../services/reconciliation.service';
import { EmployeeService } from '../../../core-hr/employees/services/employee.service';
import { IEmployee } from '../../../core-hr/employees/models/employee.models';
import { IPaginatedResponse } from '../../../core-hr/employees/models/employee.models';
import { ILeaveEncashmentResult } from '../../models/reconciliation.models';

function employee(overrides: Partial<IEmployee> = {}): IEmployee {
  return {
    employeeId: 'e-1',
    tenantId: 't-1',
    employeeNo: 'EMP-001',
    firstName: 'Alex',
    lastName: 'Doe',
    email: 'alex@acme.test',
    phone: null,
    dateOfBirth: null,
    gender: null,
    dateOfJoining: '2024-01-01',
    departmentId: 'd-1',
    departmentName: 'Eng',
    jobTitleId: 'j-1',
    jobTitleName: 'Dev',
    employmentType: 'Full-Time',
    status: 'active',
    profilePhotoUrl: null,
    customFields: null,
    isActive: true,
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
    ...overrides,
  };
}

function page(data: IEmployee[]): IPaginatedResponse<IEmployee> {
  return { data, total: data.length, page: 1, pageSize: 10 };
}

const mockResult: ILeaveEncashmentResult = {
  adjustmentId: 'adj-1',
  employeeId: 'e-1',
  encashedDays: 5,
  dailyRate: 100,
  amount: 500,
  payMonth: 6,
  payYear: 2026,
  periodDeferred: false,
};

describe('LeaveEncashmentFormComponent', () => {
  let fixture: ComponentFixture<LeaveEncashmentFormComponent>;
  let component: LeaveEncashmentFormComponent;
  let reconSpy: jasmine.SpyObj<ReconciliationService>;
  let employeeSpy: jasmine.SpyObj<EmployeeService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  beforeEach(() => {
    reconSpy = jasmine.createSpyObj<ReconciliationService>(
      'ReconciliationService',
      ['triggerLeaveEncashment', 'getReconciliation'],
    );
    employeeSpy = jasmine.createSpyObj<EmployeeService>('EmployeeService', [
      'searchActiveEmployees',
    ]);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'warning',
      'error',
    ]);

    reconSpy.triggerLeaveEncashment.and.returnValue(of(mockResult));
    employeeSpy.searchActiveEmployees.and.returnValue(of(page([employee()])));

    TestBed.configureTestingModule({
      imports: [LeaveEncashmentFormComponent],
      providers: [
        provideNoopAnimations(),
        { provide: ReconciliationService, useValue: reconSpy },
        { provide: EmployeeService, useValue: employeeSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    });

    fixture = TestBed.createComponent(LeaveEncashmentFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should be created', () => {
    expect(component).toBeTruthy();
  });

  // ─── Employee typeahead ───────────────────────────────────

  describe('employee search', () => {
    function inputEvent(value: string): Event {
      const input = document.createElement('input');
      input.value = value;
      return { target: input } as unknown as Event;
    }

    it('ignores searches under 2 characters and clears results', () => {
      component.onSearch(inputEvent('a'));
      expect(component.searchTerm()).toBe('a');
      expect(component.results().length).toBe(0);
      expect(employeeSpy.searchActiveEmployees).not.toHaveBeenCalled();
    });

    it('debounces and populates results for a 2+ char term', fakeAsync(() => {
      component.onSearch(inputEvent('al'));
      tick(250);
      expect(employeeSpy.searchActiveEmployees).toHaveBeenCalledWith('al', 8);
      expect(component.results().length).toBe(1);
      expect(component.searching()).toBeFalse();
    }));

    it('clears results and stops searching when the search errors', fakeAsync(() => {
      employeeSpy.searchActiveEmployees.and.returnValue(
        throwError(() => new Error('fail')),
      );
      component.onSearch(inputEvent('al'));
      tick(250);
      expect(component.results()).toEqual([]);
      expect(component.searching()).toBeFalse();
    }));

    it('selects an employee, clearing the term and results', () => {
      const emp = employee();
      component.selectEmployee(emp);
      expect(component.selectedEmployee()).toBe(emp);
      expect(component.results().length).toBe(0);
      expect(component.searchTerm()).toBe('');
    });

    it('clears the selected employee and surfaces the required error', () => {
      component.selectEmployee(employee());
      component.clearEmployee();
      expect(component.selectedEmployee()).toBeNull();
      expect(component.showEmployeeError()).toBeTrue();
    });
  });

  // ─── Validation ───────────────────────────────────────────

  describe('validation', () => {
    it('blocks submit when no employee is selected and shows the employee error', () => {
      component.form.patchValue({ eligibleDays: 5 });
      component.save();
      expect(component.showEmployeeError()).toBeTrue();
      expect(reconSpy.triggerLeaveEncashment).not.toHaveBeenCalled();
    });

    it('blocks submit when eligibleDays is missing or below the minimum', () => {
      component.selectEmployee(employee());
      component.form.patchValue({ eligibleDays: 0 });
      component.save();
      expect(component.form.controls.eligibleDays.invalid).toBeTrue();
      expect(reconSpy.triggerLeaveEncashment).not.toHaveBeenCalled();
    });

    it('reports a control error only after it is touched/dirty', () => {
      expect(component.showError('eligibleDays')).toBeFalse();
      component.form.controls.eligibleDays.markAsTouched();
      expect(component.showError('eligibleDays')).toBeTrue();
    });
  });

  // ─── Submit ───────────────────────────────────────────────

  describe('save', () => {
    function fillValid(leaveTypeId = ''): void {
      component.selectEmployee(employee());
      component.form.patchValue({
        eligibleDays: 5,
        leaveTypeId,
        payMonth: 5,
        payYear: 2026,
        isTaxable: true,
      });
    }

    it('POSTs a request mapping leaveTypeId to null when blank, then shows success toast', () => {
      fillValid('');
      component.save();

      expect(reconSpy.triggerLeaveEncashment).toHaveBeenCalledTimes(1);
      const req = reconSpy.triggerLeaveEncashment.calls.mostRecent().args[0];
      expect(req).toEqual({
        employeeId: 'e-1',
        eligibleDays: 5,
        leaveTypeId: null,
        payMonth: 5,
        payYear: 2026,
        isTaxable: true,
      });
      expect(toastrSpy.success).toHaveBeenCalled();
      expect(component.saving()).toBeFalse();
    });

    it('passes a trimmed non-empty leaveTypeId through', () => {
      fillValid('  lt-9  ');
      component.save();
      const req = reconSpy.triggerLeaveEncashment.calls.mostRecent().args[0];
      expect(req.leaveTypeId).toBe('lt-9');
    });

    it('emits the saved result on success', () => {
      const emitted: ILeaveEncashmentResult[] = [];
      component.saved.subscribe((r) => emitted.push(r));
      fillValid();
      component.save();
      expect(emitted).toEqual([mockResult]);
    });

    it('shows a warning toast when the period was deferred', () => {
      reconSpy.triggerLeaveEncashment.and.returnValue(
        of({ ...mockResult, periodDeferred: true }),
      );
      fillValid();
      component.save();
      expect(toastrSpy.warning).toHaveBeenCalled();
      expect(toastrSpy.success).not.toHaveBeenCalled();
    });

    it('shows the server error message on failure', () => {
      reconSpy.triggerLeaveEncashment.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              error: { message: 'Not eligible' },
              status: 400,
            }),
        ),
      );
      fillValid();
      component.save();
      expect(toastrSpy.error).toHaveBeenCalledWith('Not eligible');
      expect(component.saving()).toBeFalse();
    });

    it('falls back to a generic error message when none is provided', () => {
      reconSpy.triggerLeaveEncashment.and.returnValue(
        throwError(() => new HttpErrorResponse({ error: {}, status: 500 })),
      );
      fillValid();
      component.save();
      expect(toastrSpy.error).toHaveBeenCalledWith(
        'Could not trigger the leave encashment.',
      );
    });
  });

  // ─── Defaults / teardown ──────────────────────────────────

  it('exposes the 12 month options and a 4-year window', () => {
    expect(component.months.length).toBe(12);
    expect(component.years.length).toBe(4);
  });

  it('completes its subjects on destroy without error', () => {
    expect(() => component.ngOnDestroy()).not.toThrow();
  });
});
