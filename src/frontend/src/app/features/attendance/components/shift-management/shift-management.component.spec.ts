import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import { ShiftManagementComponent } from './shift-management.component';
import { AttendanceService } from '../../services/attendance.service';
import { EmployeeService } from '../../../core-hr/employees/services/employee.service';
import { IShift, IAssignmentResult } from '../../models/attendance.models';

describe('ShiftManagementComponent (US-ATT-005)', () => {
  let fixture: ComponentFixture<ShiftManagementComponent>;
  let component: ShiftManagementComponent;
  let attendance: jasmine.SpyObj<AttendanceService>;
  let employees: jasmine.SpyObj<EmployeeService>;
  let toastr: jasmine.SpyObj<ToastrService>;

  const single: IShift = {
    id: 's1',
    name: 'Morning Shift',
    type: 'SINGLE',
    startTime: '09:00',
    endTime: '17:00',
    breakDurationMinutes: 60,
    gracePeriodMinutes: 10,
    minimumHours: null,
    workingDays: [1, 2, 3, 4, 5],
    isDefault: true,
    isActive: true,
    assignedEmployeeCount: 0,
  };

  const flexible: IShift = {
    id: 's2',
    name: 'Remote Flex',
    type: 'FLEXIBLE',
    startTime: null,
    endTime: null,
    breakDurationMinutes: 0,
    gracePeriodMinutes: 0,
    minimumHours: 8,
    workingDays: [],
    isDefault: false,
    isActive: true,
    assignedEmployeeCount: 2,
  };

  function createEmployee(id: string, first: string, last: string) {
    return {
      employeeId: id,
      employeeNo: 'E-' + id,
      firstName: first,
      lastName: last,
    } as any;
  }

  function setup(shifts: IShift[] = [single, flexible]): void {
    attendance.getShifts.and.returnValue(of(shifts));
    fixture = TestBed.createComponent(ShiftManagementComponent);
    component = fixture.componentInstance;
    fixture.detectChanges(); // triggers ngOnInit -> load()
  }

  beforeEach(() => {
    attendance = jasmine.createSpyObj<AttendanceService>('AttendanceService', [
      'getShifts',
      'createShift',
      'updateShift',
      'deleteShift',
      'cloneShift',
      'assignShift',
    ]);
    employees = jasmine.createSpyObj<EmployeeService>('EmployeeService', ['searchActiveEmployees']);
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', ['success', 'error', 'warning', 'info']);

    employees.searchActiveEmployees.and.returnValue(
      of({ data: [createEmployee('e1', 'Ada', 'Lovelace')], total: 1, page: 1, pageSize: 10 } as any),
    );

    TestBed.configureTestingModule({
      imports: [ShiftManagementComponent, NoopAnimationsModule],
      providers: [
        { provide: AttendanceService, useValue: attendance },
        { provide: EmployeeService, useValue: employees },
        { provide: ToastrService, useValue: toastr },
      ],
    });
  });

  // ── List render (AC-1) ───────────────────────────────────────
  it('renders the shift list rows', () => {
    setup();
    const html = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(html).toContain('Morning Shift');
    expect(html).toContain('Remote Flex');
    // Default badge for the default shift.
    expect(html).toContain('Default');
  });

  it('shows the empty state when there are no shifts', () => {
    setup([]);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No shifts defined yet');
  });

  // ── Create form validation ───────────────────────────────────
  it('rejects a SINGLE shift where start == end (BR-7)', () => {
    setup();
    component.openCreate();
    component.form.patchValue({
      name: 'Bad Shift',
      type: 'SINGLE',
      startTime: '09:00',
      endTime: '09:00',
      workingDays: [1, 2, 3],
    });
    component.form.updateValueAndValidity();
    expect(component.form.errors?.['sameTimes']).toBeTrue();
    component.submit();
    expect(attendance.createShift).not.toHaveBeenCalled();
  });

  it('requires minimumHours for a FLEXIBLE shift (BR-8)', () => {
    setup();
    component.openCreate();
    component.form.patchValue({ name: 'Flex', type: 'FLEXIBLE', minimumHours: null });
    component.form.updateValueAndValidity();
    expect(component.form.errors?.['minHoursRequired']).toBeTrue();
    component.submit();
    expect(attendance.createShift).not.toHaveBeenCalled();
  });

  it('requires non-empty working days for a non-flexible shift (BR-6)', () => {
    setup();
    component.openCreate();
    component.form.patchValue({
      name: 'NoDays',
      type: 'SINGLE',
      startTime: '09:00',
      endTime: '17:00',
      workingDays: [],
    });
    component.form.updateValueAndValidity();
    expect(component.form.errors?.['workingDaysRequired']).toBeTrue();
  });

  it('creates a valid SINGLE shift (AC-1)', () => {
    setup();
    attendance.createShift.and.returnValue(of({ ...single, id: 's9', name: 'New One' }));
    component.openCreate();
    component.form.patchValue({
      name: 'New One',
      type: 'SINGLE',
      startTime: '08:00',
      endTime: '16:00',
      breakDurationMinutes: 30,
      gracePeriodMinutes: 5,
      workingDays: [1, 2, 3, 4, 5],
    });
    component.form.updateValueAndValidity();
    component.submit();
    expect(attendance.createShift).toHaveBeenCalled();
    const body = attendance.createShift.calls.mostRecent().args[0];
    expect(body.name).toBe('New One');
    expect(body.startTime).toBe('08:00');
    expect(component.shifts().some((s) => s.id === 's9')).toBeTrue();
    expect(component.formOpen()).toBeFalse();
  });

  it('omits times and working days but sends minimumHours for FLEXIBLE', () => {
    setup();
    attendance.createShift.and.returnValue(of({ ...flexible, id: 's8' }));
    component.openCreate();
    component.form.patchValue({ name: 'Flexer', type: 'FLEXIBLE', minimumHours: 7.5 });
    component.form.updateValueAndValidity();
    component.submit();
    const body = attendance.createShift.calls.mostRecent().args[0];
    expect(body.minimumHours).toBe(7.5);
    expect(body.startTime).toBeUndefined();
    expect(body.workingDays).toEqual([]);
  });

  // ── Working-days toggle ──────────────────────────────────────
  it('toggles a working day on and off', () => {
    setup();
    component.openCreate();
    component.form.patchValue({ workingDays: [1, 2] });
    component.toggleDay(3);
    expect(component.form.get('workingDays')!.value).toContain(3);
    component.toggleDay(3);
    expect(component.form.get('workingDays')!.value).not.toContain(3);
  });

  // ── Rotation builder (AC-5, FR-7) ────────────────────────────
  it('adds and removes rotation steps', () => {
    setup();
    component.openCreate();
    component.form.patchValue({ type: 'ROTATING' });
    expect(component.rotationSteps.length).toBe(0);
    component.addRotationStep();
    component.addRotationStep();
    expect(component.rotationSteps.length).toBe(2);
    component.removeRotationStep(0);
    expect(component.rotationSteps.length).toBe(1);
  });

  it('reorders rotation steps with moveStep', () => {
    setup();
    component.openCreate();
    component.form.patchValue({ type: 'ROTATING' });
    component.addRotationStep();
    component.addRotationStep();
    component.rotationSteps.at(0).patchValue({ shiftId: 's1', durationDays: 3 });
    component.rotationSteps.at(1).patchValue({ shiftId: 's2', durationDays: 4 });
    component.moveStep(0, 1);
    expect(component.rotationSteps.at(0).get('shiftId')!.value).toBe('s2');
    expect(component.rotationSteps.at(1).get('shiftId')!.value).toBe('s1');
  });

  it('builds a ROTATING shift request with ordered steps', () => {
    setup();
    attendance.createShift.and.returnValue(of({ ...single, id: 'r1', type: 'ROTATING' }));
    component.openCreate();
    component.form.patchValue({ name: 'Rota', type: 'ROTATING', workingDays: [1, 2, 3, 4, 5] });
    component.addRotationStep();
    component.rotationSteps.at(0).patchValue({ shiftId: 's1', durationDays: 7 });
    component.rotationGroup.patchValue({ cycleLengthDays: 7, referenceStartDate: '2026-07-01' });
    component.form.updateValueAndValidity();
    component.submit();
    const body = attendance.createShift.calls.mostRecent().args[0];
    expect(body.type).toBe('ROTATING');
    expect(body.rotation!.steps[0]).toEqual({ order: 1, shiftId: 's1', durationDays: 7 });
    expect(body.rotation!.referenceStartDate).toBe('2026-07-01');
  });

  // ── Delete in-use error (AC-4) ───────────────────────────────
  it('shows the in-use error message verbatim when delete is blocked (AC-4)', () => {
    setup();
    const msg = 'This shift is assigned to 5 employees. Please reassign them before deleting.';
    attendance.deleteShift.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { message: msg, code: 'shift_in_use' } })),
    );
    component.confirmDelete(single);
    component.executeDelete();
    fixture.detectChanges();
    expect(component.deleteError()).toBe(msg);
    // Row is NOT removed and the dialog stays open.
    expect(component.shifts().some((s) => s.id === 's1')).toBeTrue();
    expect(component.deleteTarget()).not.toBeNull();
  });

  it('removes the shift row on successful delete', () => {
    setup();
    attendance.deleteShift.and.returnValue(of(void 0));
    component.confirmDelete(single);
    component.executeDelete();
    expect(component.shifts().some((s) => s.id === 's1')).toBeFalse();
    expect(component.deleteTarget()).toBeNull();
  });

  // ── Clone (FR-8) ─────────────────────────────────────────────
  it('prepends the cloned shift on clone', () => {
    setup();
    attendance.cloneShift.and.returnValue(of({ ...single, id: 's1-copy', name: 'Morning Shift (copy)' }));
    component.clone(single);
    expect(attendance.cloneShift).toHaveBeenCalledWith('s1');
    expect(component.shifts()[0].id).toBe('s1-copy');
  });

  // ── Assign (AC-2) ────────────────────────────────────────────
  it('assigns a shift to selected employees and reports the count (AC-2)', fakeAsync(() => {
    setup();
    const result: IAssignmentResult = { assignedCount: 2, employeeShiftIds: ['es1', 'es2'] };
    attendance.assignShift.and.returnValue(of(result));

    component.openAssign(single);
    component.onAssignSearch('ada');
    tick(300); // debounce
    expect(component.assignAvailable().length).toBe(1);

    component.selectEmployee(component.assignAvailable()[0]);
    component.selectEmployee(createEmployee('e2', 'Grace', 'Hopper'));
    expect(component.assignSelected().length).toBe(2);

    component.onEffectiveFromChange('2026-07-01');
    component.executeAssign();

    expect(attendance.assignShift).toHaveBeenCalledWith('s1', {
      employeeIds: ['e1', 'e2'],
      effectiveFrom: '2026-07-01',
    });
    expect(toastr.success).toHaveBeenCalled();
    // Optimistic count bump on the row.
    expect(component.shifts().find((s) => s.id === 's1')!.assignedEmployeeCount).toBe(2);
    expect(component.assignTarget()).toBeNull();
  }));

  it('warns and skips the call when no employees are selected', () => {
    setup();
    component.openAssign(single);
    component.executeAssign();
    expect(toastr.warning).toHaveBeenCalled();
    expect(attendance.assignShift).not.toHaveBeenCalled();
  });

  it('deselects a previously selected employee', () => {
    setup();
    const emp = createEmployee('e1', 'Ada', 'Lovelace');
    component.openAssign(single);
    component.selectEmployee(emp);
    expect(component.assignSelected().length).toBe(1);
    component.deselectEmployee(emp);
    expect(component.assignSelected().length).toBe(0);
  });

  // ── Edit pre-population ──────────────────────────────────────
  it('pre-populates the form when editing an existing shift', () => {
    setup();
    component.openEdit(single);
    expect(component.editingId()).toBe('s1');
    expect(component.form.get('name')!.value).toBe('Morning Shift');
    expect(component.form.get('startTime')!.value).toBe('09:00');
    expect(component.currentType()).toBe('SINGLE');
  });
});
