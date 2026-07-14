import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { By } from '@angular/platform-browser';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { AdjustmentFormComponent } from './adjustment-form.component';
import { TrappedDialogDirective } from '../../../../shared/directives';
import { AdjustmentService } from '../../services/adjustment.service';
import { EmployeeService } from '../../../core-hr/employees/services/employee.service';
import { IAdjustment } from '../../models/adjustment.models';
import { IEmployee } from '../../../core-hr/employees/models/employee.models';

function makeEmployee(over: Partial<IEmployee> = {}): IEmployee {
  return {
    employeeId: 'e-1',
    tenantId: 't-1',
    employeeNo: 'EMP001',
    firstName: 'Alex',
    lastName: 'HR',
    email: 'alex@acme.test',
    phone: null,
    dateOfBirth: null,
    gender: null,
    dateOfJoining: '2020-01-01',
    departmentId: 'd-1',
    departmentName: 'People',
    jobTitleId: 'j-1',
    jobTitleName: 'HR Officer',
    locationId: null,
    locationName: null,
    employmentType: 'FullTime' as IEmployee['employmentType'],
    status: 'active' as IEmployee['status'],
    profilePhotoUrl: null,
    customFields: null,
    isActive: true,
    createdAt: '2020-01-01T00:00:00Z',
    updatedAt: '2020-01-01T00:00:00Z',
    ...over,
  };
}

const created: IAdjustment = {
  id: 'a-1',
  employeeId: 'e-1',
  employeeName: 'Alex HR',
  employeeNo: 'EMP001',
  type: 'Bonus',
  amount: 10000,
  description: 'Q2 bonus',
  applicablePayMonth: 6,
  applicablePayYear: 2026,
  isTaxable: true,
  isRecurring: false,
  recurrenceEndMonth: null,
  recurrenceEndYear: null,
  status: 'Pending',
  hasDocument: false,
  createdAt: '2026-06-01T10:00:00Z',
};

describe('AdjustmentFormComponent', () => {
  let fixture: ComponentFixture<AdjustmentFormComponent>;
  let component: AdjustmentFormComponent;
  let adjService: jasmine.SpyObj<AdjustmentService>;
  let empService: jasmine.SpyObj<EmployeeService>;
  let toastr: jasmine.SpyObj<ToastrService>;

  beforeEach(async () => {
    adjService = jasmine.createSpyObj('AdjustmentService', [
      'createAdjustment',
      'uploadDocument',
    ]);
    empService = jasmine.createSpyObj('EmployeeService', [
      'searchActiveEmployees',
    ]);
    toastr = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
    ]);

    await TestBed.configureTestingModule({
      imports: [AdjustmentFormComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AdjustmentService, useValue: adjService },
        { provide: EmployeeService, useValue: empService },
        { provide: ToastrService, useValue: toastr },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdjustmentFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('creates', () => {
    expect(component).toBeTruthy();
  });

  it('traps focus in the dialog (ISSUE-296)', () => {
    const dialog = fixture.debugElement.query(
      By.directive(TrappedDialogDirective),
    );
    expect(dialog).toBeTruthy();
  });

  describe('employee typeahead', () => {
    it('does not search for terms shorter than 2 chars', fakeAsync(() => {
      component.onSearch({ target: { value: 'a' } } as unknown as Event);
      tick(300);
      expect(empService.searchActiveEmployees).not.toHaveBeenCalled();
      expect(component.results()).toEqual([]);
    }));

    it('debounces then queries active employees and stores the results', fakeAsync(() => {
      empService.searchActiveEmployees.and.returnValue(
        of({ items: [makeEmployee()], totalCount: 1, page: 1, pageSize: 8 }),
      );
      component.onSearch({ target: { value: 'al' } } as unknown as Event);
      tick(250);
      expect(empService.searchActiveEmployees).toHaveBeenCalledWith('al', 8);
      expect(component.results().length).toBe(1);
    }));

    it('selectEmployee sets the selection and clears the results', () => {
      const emp = makeEmployee();
      component.selectEmployee(emp);
      expect(component.selectedEmployee()).toBe(emp);
      expect(component.results()).toEqual([]);
    });

    it('flags an employee error only after the field was touched', () => {
      expect(component.showEmployeeError()).toBeFalse();
      component.clearEmployee();
      expect(component.showEmployeeError()).toBeTrue();
    });
  });

  describe('supporting document validation (NFR-5)', () => {
    function fileEvent(file: File | null): Event {
      return {
        target: { files: file ? [file] : [], value: 'x' },
      } as unknown as Event;
    }

    it('accepts a valid PDF under 5MB', () => {
      const file = new File(['%PDF'], 'r.pdf', { type: 'application/pdf' });
      component.onFileSelected(fileEvent(file));
      expect(component.documentFile()).toBe(file);
      expect(component.documentError()).toBeNull();
    });

    it('rejects a disallowed mime type', () => {
      const file = new File(['x'], 'r.txt', { type: 'text/plain' });
      component.onFileSelected(fileEvent(file));
      expect(component.documentFile()).toBeNull();
      expect(component.documentError()).toContain('PDF');
    });

    it('rejects a file over 5MB', () => {
      const file = new File(['x'], 'big.png', { type: 'image/png' });
      Object.defineProperty(file, 'size', { value: 6 * 1024 * 1024 });
      component.onFileSelected(fileEvent(file));
      expect(component.documentFile()).toBeNull();
      expect(component.documentError()).toContain('5MB');
    });
  });

  describe('recurring preview (BR-6)', () => {
    it('is empty until isRecurring is toggled on', () => {
      expect(component.recurringPreview()).toEqual([]);
    });

    it('enumerates the affected periods when recurring with a valid range', () => {
      component.form.patchValue({
        applicablePayMonth: 6,
        applicablePayYear: 2026,
        recurrenceEndMonth: 8,
        recurrenceEndYear: 2026,
        isRecurring: true,
      });
      expect(component.recurringPreview().length).toBe(3);
    });

    it('produces an empty preview for an invalid (reversed) range', () => {
      component.form.patchValue({
        applicablePayMonth: 6,
        applicablePayYear: 2026,
        recurrenceEndMonth: 5,
        recurrenceEndYear: 2026,
        isRecurring: true,
      });
      expect(component.recurringPreview()).toEqual([]);
    });
  });

  describe('save', () => {
    function fillValidForm(): void {
      component.selectEmployee(makeEmployee());
      component.form.patchValue({
        type: 'Bonus',
        amount: 10000,
        applicablePayMonth: 6,
        applicablePayYear: 2026,
        description: 'Q2 bonus',
        isTaxable: true,
        isRecurring: false,
      });
    }

    it('does not call the service when no employee is selected', () => {
      component.form.patchValue({ amount: 1000, description: 'x' });
      component.save();
      expect(adjService.createAdjustment).not.toHaveBeenCalled();
      expect(component.showEmployeeError()).toBeTrue();
    });

    it('creates the adjustment and emits saved (no document)', () => {
      adjService.createAdjustment.and.returnValue(of(created));
      const savedSpy = jasmine.createSpy('saved');
      component.saved.subscribe(savedSpy);
      fillValidForm();

      component.save();

      const body = adjService.createAdjustment.calls.mostRecent().args[0];
      expect(body.employeeId).toBe('e-1');
      expect(body.amount).toBe(10000);
      expect(body.recurrenceEndMonth).toBeNull();
      expect(savedSpy).toHaveBeenCalledWith(created);
      expect(component.saving()).toBeFalse();
    });

    it('uploads the document after create when one is attached (AC-3)', () => {
      adjService.createAdjustment.and.returnValue(of(created));
      adjService.uploadDocument.and.returnValue(
        of({ ...created, hasDocument: true }),
      );
      const savedSpy = jasmine.createSpy('saved');
      component.saved.subscribe(savedSpy);
      fillValidForm();
      const file = new File(['%PDF'], 'r.pdf', { type: 'application/pdf' });
      component.onFileSelected({
        target: { files: [file], value: 'x' },
      } as unknown as Event);

      component.save();

      expect(adjService.uploadDocument).toHaveBeenCalledWith('a-1', file);
      expect(savedSpy).toHaveBeenCalledWith(
        jasmine.objectContaining({ hasDocument: true }),
      );
    });

    it('still emits the created record (with a warning) when the document upload fails', () => {
      adjService.createAdjustment.and.returnValue(of(created));
      adjService.uploadDocument.and.returnValue(
        throwError(() => new Error('upload failed')),
      );
      const savedSpy = jasmine.createSpy('saved');
      component.saved.subscribe(savedSpy);
      fillValidForm();
      const file = new File(['%PDF'], 'r.pdf', { type: 'application/pdf' });
      component.onFileSelected({
        target: { files: [file], value: 'x' },
      } as unknown as Event);

      component.save();

      expect(toastr.warning).toHaveBeenCalled();
      expect(savedSpy).toHaveBeenCalledWith(created);
    });

    it('blocks save when recurring with an invalid end period', () => {
      component.selectEmployee(makeEmployee());
      component.form.patchValue({
        amount: 1000,
        description: 'loan',
        applicablePayMonth: 6,
        applicablePayYear: 2026,
        recurrenceEndMonth: 5,
        recurrenceEndYear: 2026,
        isRecurring: true,
      });

      component.save();

      expect(adjService.createAdjustment).not.toHaveBeenCalled();
      expect(toastr.error).toHaveBeenCalled();
    });

    it('passes recurrence end fields through for a valid recurring adjustment', () => {
      adjService.createAdjustment.and.returnValue(
        of({ ...created, isRecurring: true }),
      );
      component.selectEmployee(makeEmployee());
      component.form.patchValue({
        type: 'Deduction',
        amount: 500,
        description: 'loan',
        applicablePayMonth: 6,
        applicablePayYear: 2026,
        recurrenceEndMonth: 12,
        recurrenceEndYear: 2026,
        isRecurring: true,
      });

      component.save();

      const body = adjService.createAdjustment.calls.mostRecent().args[0];
      expect(body.isRecurring).toBeTrue();
      expect(body.recurrenceEndMonth).toBe(12);
      expect(body.recurrenceEndYear).toBe(2026);
    });
  });
});
