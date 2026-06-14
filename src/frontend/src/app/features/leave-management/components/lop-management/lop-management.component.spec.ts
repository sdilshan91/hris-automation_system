import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { LopManagementComponent } from './lop-management.component';
import { LopService } from '../../services/lop.service';
import { LeaveTypeService } from '../../services/leave-type.service';
import { EmployeeService } from '../../../core-hr/employees/services/employee.service';
import { ILopEntry } from '../../models/lop.models';
import { ILeaveRequest } from '../../models/leave-request.models';
import { ILeaveType } from '../../models/leave-type.models';
import { IEmployee } from '../../../core-hr/employees/models/employee.models';

describe('LopManagementComponent', () => {
  let component: LopManagementComponent;
  let fixture: ComponentFixture<LopManagementComponent>;
  let lopServiceSpy: jasmine.SpyObj<LopService>;
  let leaveTypeServiceSpy: jasmine.SpyObj<LeaveTypeService>;
  let employeeServiceSpy: jasmine.SpyObj<EmployeeService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const sysEntry: ILopEntry = {
    leaveRequestId: 'lr-1',
    employeeId: 'emp-1',
    employeeName: 'Jane Doe',
    date: '2026-07-06',
    days: 1,
    source: 'system_generated',
    status: 'System-Generated',
    reason: 'No clock-in',
  };
  const hrEntry: ILopEntry = {
    ...sysEntry,
    leaveRequestId: 'lr-2',
    employeeName: 'John Roe',
    source: 'hr_assigned',
    status: 'HR-Assigned',
  };

  const leaveType: ILeaveType = {
    leaveTypeId: 'lt-1',
    tenantId: 't-1',
    name: 'Casual Leave',
    code: 'CL',
    color: '#16a34a',
    description: null,
    annualEntitlement: 7,
    accrualFrequency: 'upfront',
    carryForwardLimit: 0,
    carryForwardExpiryMonths: 0,
    probationEligible: true,
    documentsRequired: false,
    documentDayThreshold: null,
    encashable: false,
    maxEncashDays: null,
    halfDayAllowed: true,
    hourlyAllowed: false,
    gender: 'all',
    maxConsecutiveDays: null,
    negativeBalanceAllowed: false,
    negativeBalanceLimit: null,
    displayOrder: 0,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };

  const employee: IEmployee = {
    employeeId: 'emp-1',
    tenantId: 't-1',
    employeeNo: 'E-001',
    firstName: 'Jane',
    lastName: 'Doe',
    email: 'jane@acme.com',
    phone: null,
    dateOfBirth: null,
    gender: null,
    dateOfJoining: '2025-01-01',
    departmentId: 'd-1',
    departmentName: 'Eng',
    jobTitleId: 'j-1',
    jobTitleName: 'Dev',
    employmentType: 'Full-Time',
    status: 'active',
    profilePhotoUrl: null,
    customFields: null,
    isActive: true,
    createdAt: '2025-01-01T00:00:00Z',
    updatedAt: '2025-01-01T00:00:00Z',
  };
  const employee2: IEmployee = { ...employee, employeeId: 'emp-2', firstName: 'John', lastName: 'Roe', email: 'john@acme.com' };

  beforeEach(async () => {
    lopServiceSpy = jasmine.createSpyObj('LopService', [
      'getLopSummary',
      'assignLop',
      'assignCompulsoryLeave',
      'overrideLop',
    ]);
    lopServiceSpy.getLopSummary.and.returnValue(of([sysEntry, hrEntry]));
    lopServiceSpy.assignLop.and.returnValue(of({ employeeId: 'emp-1', created: 1 }));
    lopServiceSpy.assignCompulsoryLeave.and.returnValue(of({ deducted: 1, lop: 1, total: 2 }));
    lopServiceSpy.overrideLop.and.returnValue(
      of({ leaveRequestId: 'lr-1', status: 'Approved' } as unknown as ILeaveRequest),
    );

    leaveTypeServiceSpy = jasmine.createSpyObj('LeaveTypeService', ['getLeaveTypes']);
    leaveTypeServiceSpy.getLeaveTypes.and.returnValue(of([leaveType]));

    employeeServiceSpy = jasmine.createSpyObj('EmployeeService', ['getEmployees']);
    employeeServiceSpy.getEmployees.and.returnValue(of([employee, employee2]));

    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error', 'warning', 'info']);

    await TestBed.configureTestingModule({
      imports: [LopManagementComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: LopService, useValue: lopServiceSpy },
        { provide: LeaveTypeService, useValue: leaveTypeServiceSpy },
        { provide: EmployeeService, useValue: employeeServiceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LopManagementComponent);
    component = fixture.componentInstance;
  });

  // ─── List + filters ───────────────────────────────────────

  it('loads LOP entries + active leave types on init', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
    expect(component.entries().length).toBe(2);
    expect(component.leaveTypes().length).toBe(1);
    expect(component.isLoading()).toBeFalse();
  });

  it('renders LOP rows in the desktop table', () => {
    fixture.detectChanges();
    const rows = fixture.nativeElement.querySelectorAll('[data-testid="lop-row"]');
    expect(rows.length).toBe(2);
  });

  it('applies a red highlight to system-generated rows and orange to others (§8)', () => {
    fixture.detectChanges();
    expect(component.rowClasses(sysEntry)).toContain('border-red-400');
    expect(component.rowClasses(hrEntry)).toContain('border-orange-400');
  });

  it('filters entries by source', () => {
    fixture.detectChanges();
    component.setFilter('hr_assigned');
    expect(component.filteredEntries().length).toBe(1);
    expect(component.filteredEntries()[0].leaveRequestId).toBe('lr-2');
    component.setFilter('all');
    expect(component.filteredEntries().length).toBe(2);
  });

  it('shows the empty state when no entries match the filter', () => {
    lopServiceSpy.getLopSummary.and.returnValue(of([]));
    fixture.detectChanges();
    const empty = fixture.nativeElement.querySelector('[data-testid="lop-empty"]');
    expect(empty).toBeTruthy();
  });

  it('shows an error toast when loading fails', () => {
    lopServiceSpy.getLopSummary.and.returnValue(throwError(() => new Error('boom')));
    fixture.detectChanges();
    expect(component.isLoading()).toBeFalse();
    expect(toastrSpy.error).toHaveBeenCalled();
  });

  it('only allows override on system-generated entries (BR-3)', () => {
    fixture.detectChanges();
    expect(component.canOverride(sysEntry)).toBeTrue();
    expect(component.canOverride(hrEntry)).toBeFalse();
  });

  // ─── Bulk LOP assignment ──────────────────────────────────

  it('loads employees when the bulk panel opens', () => {
    fixture.detectChanges();
    component.openPanel('bulk');
    expect(employeeServiceSpy.getEmployees).toHaveBeenCalled();
    expect(component.employees().length).toBe(2);
  });

  it('multi-selects employees and filters them by search', () => {
    fixture.detectChanges();
    component.openPanel('bulk');
    component.toggleEmployee('emp-1');
    component.toggleEmployee('emp-2');
    expect(component.selectedEmployeeIds().length).toBe(2);
    component.toggleEmployee('emp-1');
    expect(component.selectedEmployeeIds()).toEqual(['emp-2']);

    component.employeeSearch.set('john');
    expect(component.filteredEmployees().length).toBe(1);
    expect(component.filteredEmployees()[0].employeeId).toBe('emp-2');
  });

  it('blocks bulk submit without an employee or valid form', () => {
    fixture.detectChanges();
    component.openPanel('bulk');
    component.submitBulk();
    expect(lopServiceSpy.assignLop).not.toHaveBeenCalled();
    expect(toastrSpy.warning).toHaveBeenCalled();
  });

  it('submits a bulk LOP assignment with expanded dates per employee (FR-3)', () => {
    fixture.detectChanges();
    component.openPanel('bulk');
    component.toggleEmployee('emp-1');
    component.bulkForm.setValue({ from: '2026-07-06', to: '2026-07-07', reason: 'Absent' });
    component.submitBulk();

    expect(lopServiceSpy.assignLop).toHaveBeenCalledTimes(1);
    const arg = lopServiceSpy.assignLop.calls.mostRecent().args[0];
    expect(arg.employeeId).toBe('emp-1');
    expect(arg.dates).toEqual(['2026-07-06', '2026-07-07']);
    expect(arg.reason).toBe('Absent');
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(component.panel()).toBe('none');
  });

  it('calls assignLop once per selected employee', () => {
    fixture.detectChanges();
    component.openPanel('bulk');
    component.toggleEmployee('emp-1');
    component.toggleEmployee('emp-2');
    component.bulkForm.setValue({ from: '2026-07-06', to: '2026-07-06', reason: 'Absent' });
    component.submitBulk();
    expect(lopServiceSpy.assignLop).toHaveBeenCalledTimes(2);
  });

  // ─── Compulsory leave ─────────────────────────────────────

  it('blocks compulsory submit when the form is invalid', () => {
    fixture.detectChanges();
    component.openPanel('compulsory');
    component.submitCompulsory();
    expect(lopServiceSpy.assignCompulsoryLeave).not.toHaveBeenCalled();
    expect(toastrSpy.warning).toHaveBeenCalled();
  });

  it('submits a compulsory-leave assignment (FR-6, applyToAll)', () => {
    fixture.detectChanges();
    component.openPanel('compulsory');
    component.compForm.setValue({
      from: '2026-12-24',
      to: '2026-12-24',
      leaveTypeId: 'lt-1',
      applyToAll: true,
      reason: 'Shutdown',
    });
    component.submitCompulsory();

    expect(lopServiceSpy.assignCompulsoryLeave).toHaveBeenCalledTimes(1);
    const arg = lopServiceSpy.assignCompulsoryLeave.calls.mostRecent().args[0];
    expect(arg.dates).toEqual(['2026-12-24']);
    expect(arg.leaveTypeId).toBe('lt-1');
    expect(arg.applyToAll).toBeTrue();
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  it('surfaces a compulsory-leave error via toast', () => {
    lopServiceSpy.assignCompulsoryLeave.and.returnValue(
      throwError(() => ({ error: { message: 'Payroll locked' } })),
    );
    fixture.detectChanges();
    component.openPanel('compulsory');
    component.compForm.setValue({
      from: '2026-12-24',
      to: '2026-12-24',
      leaveTypeId: 'lt-1',
      applyToAll: true,
      reason: 'Shutdown',
    });
    component.submitCompulsory();
    expect(toastrSpy.error).toHaveBeenCalledWith('Payroll locked');
  });

  // ─── Override (BR-3) ──────────────────────────────────────

  it('opens the override modal for a system entry', () => {
    fixture.detectChanges();
    component.openOverride(sysEntry);
    expect(component.panel()).toBe('override');
    expect(component.overrideTarget()?.leaveRequestId).toBe('lr-1');
  });

  it('blocks override submit when the form is invalid', () => {
    fixture.detectChanges();
    component.openOverride(sysEntry);
    component.submitOverride();
    expect(lopServiceSpy.overrideLop).not.toHaveBeenCalled();
    expect(toastrSpy.warning).toHaveBeenCalled();
  });

  it('submits an override converting the LOP to a leave type (BR-3)', () => {
    fixture.detectChanges();
    component.openOverride(sysEntry);
    component.overrideForm.setValue({ leaveTypeId: 'lt-1', reason: 'Provided cert' });
    component.submitOverride();

    expect(lopServiceSpy.overrideLop).toHaveBeenCalledWith('lr-1', {
      leaveTypeId: 'lt-1',
      reason: 'Provided cert',
    });
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(component.panel()).toBe('none');
  });

  it('closePanel is a no-op while saving (guards mid-request)', () => {
    fixture.detectChanges();
    component.openPanel('bulk');
    component.isSaving.set(true);
    component.closePanel();
    expect(component.panel()).toBe('bulk');
  });
});
