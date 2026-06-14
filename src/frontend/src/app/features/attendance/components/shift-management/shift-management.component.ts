import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  FormArray,
  Validators,
  AbstractControl,
  ValidationErrors,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { debounceTime, switchMap, takeUntil } from 'rxjs/operators';
import { AttendanceService } from '../../services/attendance.service';
import { EmployeeService } from '../../../core-hr/employees/services/employee.service';
import { IEmployee } from '../../../core-hr/employees/models/employee.models';
import {
  IShift,
  IShiftRequest,
  IRotation,
  IRotationStep,
  ShiftType,
  SHIFT_TYPE_OPTIONS,
  ISO_WEEKDAYS,
  WEEKDAY_LABELS,
  shiftTypeLabel,
  shiftTypeUsesTimes,
  formatWorkingDays,
  formatShiftTimes,
  todayLocalIso,
} from '../../models/attendance.models';

/**
 * US-ATT-005 (FR-1/2, BR-7/8, §8): cross-field validator on the shift form.
 *
 *  - BR-7: for SINGLE/ROTATING, start_time must differ from end_time (zero-duration
 *    shifts are invalid). Night shifts (end < start) ARE allowed (§10) — only equality
 *    is rejected. For SINGLE both times are required.
 *  - BR-8: for FLEXIBLE, minimumHours is required (> 0); start/end are not validated.
 *  - BR-6: working_days must be non-empty for non-FLEXIBLE shifts.
 *  - FR-7: ROTATING requires at least one rotation step + a reference start date.
 */
export function shiftFormValidator(group: AbstractControl): ValidationErrors | null {
  const errors: ValidationErrors = {};
  const type = group.get('type')?.value as ShiftType | '';
  const start = group.get('startTime')?.value as string;
  const end = group.get('endTime')?.value as string;
  const minHours = group.get('minimumHours')?.value;
  const workingDays = group.get('workingDays')?.value as number[] | undefined;
  const rotation = group.get('rotation') as FormGroup | null;

  if (type === 'SINGLE') {
    // SINGLE shifts have fixed start/end times; both are required.
    if (!start) {
      errors['startRequired'] = true;
    }
    if (!end) {
      errors['endRequired'] = true;
    }
  }
  // BR-7: for any fixed-time shift, start == end is a zero-duration shift (invalid).
  // ROTATING parent times are optional (the steps carry the schedule), but if both
  // are supplied they still cannot be equal.
  if ((type === 'SINGLE' || type === 'ROTATING') && start && end && start === end) {
    errors['sameTimes'] = true;
  }

  if (type === 'FLEXIBLE') {
    // BR-8: minimum hours required and positive.
    if (minHours === null || minHours === '' || minHours === undefined || Number(minHours) <= 0) {
      errors['minHoursRequired'] = true;
    }
  } else {
    // BR-6: working days required for non-flexible shifts.
    if (!workingDays || workingDays.length === 0) {
      errors['workingDaysRequired'] = true;
    }
  }

  if (type === 'ROTATING') {
    const steps = (rotation?.get('steps') as FormArray | null)?.length ?? 0;
    if (steps === 0) {
      errors['rotationStepsRequired'] = true;
    }
    if (!rotation?.get('referenceStartDate')?.value) {
      errors['rotationStartRequired'] = true;
    }
  }

  return Object.keys(errors).length ? errors : null;
}

/**
 * US-ATT-005: Shift Management and Assignment (HR Officer).
 *
 * Smart component combining (§8):
 *  - A Notion-style table/card list of shift definitions (name, type, times, break,
 *    grace, working days, default badge, assigned count, active).
 *  - A right-sliding drawer holding the reactive shift form (full-screen on mobile).
 *    The `type` selector switches which fields show: FLEXIBLE hides start/end + requires
 *    minimum hours (BR-8); ROTATING shows a rotation builder (add/remove/reorder steps).
 *  - Per-row actions: Edit, Clone (FR-8), Delete (AC-4 in-use guard shown verbatim),
 *    and Assign (multi-select searchable employee picker + effective date, AC-2).
 *
 * Role-gated to HR Officer / HR Manager / Tenant Admin via the route guard.
 */
@Component({
  selector: 'app-shift-management',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
    trigger('backdrop', [
      transition(':enter', [style({ opacity: 0 }), animate('200ms ease-out', style({ opacity: 1 }))]),
      transition(':leave', [animate('150ms ease-in', style({ opacity: 0 }))]),
    ]),
    trigger('drawer', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate('260ms cubic-bezier(0.4, 0, 0.2, 1)', style({ transform: 'translateX(0)' })),
      ]),
      transition(':leave', [
        animate('200ms cubic-bezier(0.4, 0, 1, 1)', style({ transform: 'translateX(100%)' })),
      ]),
    ]),
  ],
  templateUrl: './shift-management.component.html',
  styleUrls: ['./shift-management.component.css'],
})
export class ShiftManagementComponent implements OnInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly attendanceService = inject(AttendanceService);
  private readonly employeeService = inject(EmployeeService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  readonly weekdays = ISO_WEEKDAYS;
  readonly weekdayLabels = WEEKDAY_LABELS;
  readonly shiftTypes = SHIFT_TYPE_OPTIONS;
  readonly maxDate = todayLocalIso();

  // ─── List state ───────────────────────────────────────────
  readonly shifts = signal<IShift[]>([]);
  readonly isLoading = signal(true);

  // ─── Edit drawer state ────────────────────────────────────
  readonly formOpen = signal(false);
  readonly isSubmitting = signal(false);
  readonly editingId = signal<string | null>(null);
  readonly serverError = signal<string | null>(null);
  /** Mirror of the form `type` so the template/computeds react to type switches. */
  private readonly formType = signal<ShiftType>('SINGLE');

  // ─── Delete confirm state ─────────────────────────────────
  readonly deleteTarget = signal<IShift | null>(null);
  readonly isDeleting = signal(false);
  /** AC-4 in-use message shown verbatim in the confirm dialog. */
  readonly deleteError = signal<string | null>(null);

  // ─── Assign drawer state ──────────────────────────────────
  readonly assignTarget = signal<IShift | null>(null);
  readonly isAssigning = signal(false);
  readonly assignSearch = signal('');
  readonly assignResults = signal<IEmployee[]>([]);
  readonly assignSelected = signal<IEmployee[]>([]);
  readonly assignEffectiveFrom = signal<string>(todayLocalIso());
  readonly assignSearching = signal(false);
  private readonly assignSearch$ = new Subject<string>();

  readonly form: FormGroup = this.fb.group(
    {
      name: ['', [Validators.required, Validators.maxLength(100)]],
      type: ['SINGLE' as ShiftType, Validators.required],
      startTime: [''],
      endTime: [''],
      breakDurationMinutes: [0, [Validators.min(0)]],
      gracePeriodMinutes: [0, [Validators.min(0)]],
      minimumHours: [null as number | null, [Validators.min(0)]],
      workingDays: [[1, 2, 3, 4, 5] as number[]],
      rotation: this.fb.group({
        cycleLengthDays: [7, [Validators.min(1)]],
        referenceStartDate: [todayLocalIso()],
        steps: this.fb.array([]),
      }),
    },
    { validators: [shiftFormValidator] },
  );

  // ─── Computed (form) ──────────────────────────────────────
  readonly currentType = computed(() => this.formType());
  readonly usesTimes = computed(() => shiftTypeUsesTimes(this.formType()));
  readonly isFlexible = computed(() => this.formType() === 'FLEXIBLE');
  readonly isRotating = computed(() => this.formType() === 'ROTATING');

  get rotationGroup(): FormGroup {
    return this.form.get('rotation') as FormGroup;
  }

  get rotationSteps(): FormArray {
    return this.rotationGroup.get('steps') as FormArray;
  }

  /** Existing shifts selectable as rotation steps (exclude rotating shifts to avoid nesting). */
  readonly stepShiftOptions = computed(() =>
    this.shifts().filter((s) => s.type !== 'ROTATING'),
  );

  // ─── Computed (assign) ────────────────────────────────────
  readonly assignSelectedIds = computed(
    () => new Set(this.assignSelected().map((e) => e.employeeId)),
  );
  /** Search results minus already-selected employees (avoid duplicates in the list). */
  readonly assignAvailable = computed(() => {
    const selected = this.assignSelectedIds();
    return this.assignResults().filter((e) => !selected.has(e.employeeId));
  });

  // ─── Lifecycle ────────────────────────────────────────────
  ngOnInit(): void {
    this.form
      .get('type')!
      .valueChanges.pipe(takeUntil(this.destroy$))
      .subscribe((t) => this.formType.set((t ?? 'SINGLE') as ShiftType));

    this.assignSearch$
      .pipe(
        debounceTime(250),
        switchMap((term) => {
          this.assignSearching.set(true);
          return this.employeeService.searchActiveEmployees(term, 10);
        }),
        takeUntil(this.destroy$),
      )
      .subscribe({
        next: (res) => {
          this.assignResults.set(res.data ?? []);
          this.assignSearching.set(false);
        },
        error: () => {
          this.assignSearching.set(false);
          this.toastr.error('Failed to search employees.');
        },
      });

    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ─── Data loading ─────────────────────────────────────────
  load(): void {
    this.isLoading.set(true);
    this.attendanceService
      .getShifts()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (shifts) => {
          this.shifts.set(shifts);
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
          this.toastr.error('Failed to load shifts.');
        },
      });
  }

  // ─── Display helpers ──────────────────────────────────────
  typeLabel(type: ShiftType): string {
    return shiftTypeLabel(type);
  }

  workingDaysLabel(shift: IShift): string {
    return formatWorkingDays(shift.workingDays);
  }

  timesLabel(shift: IShift): string {
    return formatShiftTimes(shift);
  }

  shiftName(id: string): string {
    return this.shifts().find((s) => s.id === id)?.name ?? '—';
  }

  showError(controlName: string): boolean {
    const control = this.form.get(controlName);
    return !!control && control.invalid && (control.touched || control.dirty);
  }

  // ─── Working-days picker ──────────────────────────────────
  isDaySelected(day: number): boolean {
    return (this.form.get('workingDays')!.value as number[]).includes(day);
  }

  toggleDay(day: number): void {
    const ctrl = this.form.get('workingDays')!;
    const current = [...(ctrl.value as number[])];
    const idx = current.indexOf(day);
    if (idx >= 0) {
      current.splice(idx, 1);
    } else {
      current.push(day);
    }
    ctrl.setValue(current);
    ctrl.markAsDirty();
    this.form.updateValueAndValidity();
  }

  // ─── Rotation builder (FR-7, §8) ──────────────────────────
  private newStep(step?: Partial<IRotationStep>): FormGroup {
    return this.fb.group({
      shiftId: [step?.shiftId ?? '', Validators.required],
      durationDays: [step?.durationDays ?? 1, [Validators.required, Validators.min(1)]],
    });
  }

  addRotationStep(): void {
    this.rotationSteps.push(this.newStep());
    this.form.updateValueAndValidity();
  }

  removeRotationStep(index: number): void {
    this.rotationSteps.removeAt(index);
    this.form.updateValueAndValidity();
  }

  /** Move a step up/down to reorder the cycle (drag-and-drop optional; buttons suffice). */
  moveStep(index: number, direction: -1 | 1): void {
    const target = index + direction;
    if (target < 0 || target >= this.rotationSteps.length) {
      return;
    }
    const ctrl = this.rotationSteps.at(index);
    this.rotationSteps.removeAt(index);
    this.rotationSteps.insert(target, ctrl);
    this.form.updateValueAndValidity();
  }

  // ─── Edit drawer open/close ───────────────────────────────
  openCreate(): void {
    this.editingId.set(null);
    this.serverError.set(null);
    this.resetForm();
    this.formType.set('SINGLE');
    this.formOpen.set(true);
  }

  openEdit(shift: IShift): void {
    this.editingId.set(shift.id);
    this.serverError.set(null);
    this.resetForm();
    this.form.patchValue({
      name: shift.name,
      type: shift.type,
      startTime: shift.startTime ?? '',
      endTime: shift.endTime ?? '',
      breakDurationMinutes: shift.breakDurationMinutes,
      gracePeriodMinutes: shift.gracePeriodMinutes,
      minimumHours: shift.minimumHours,
      workingDays: [...shift.workingDays],
    });
    if (shift.rotation) {
      this.rotationGroup.patchValue({
        cycleLengthDays: shift.rotation.cycleLengthDays,
        referenceStartDate: shift.rotation.referenceStartDate,
      });
      for (const step of [...shift.rotation.steps].sort((a, b) => a.order - b.order)) {
        this.rotationSteps.push(this.newStep(step));
      }
    }
    this.formType.set(shift.type);
    this.formOpen.set(true);
  }

  private resetForm(): void {
    this.rotationSteps.clear();
    this.form.reset({
      name: '',
      type: 'SINGLE',
      startTime: '',
      endTime: '',
      breakDurationMinutes: 0,
      gracePeriodMinutes: 0,
      minimumHours: null,
      workingDays: [1, 2, 3, 4, 5],
      rotation: {
        cycleLengthDays: 7,
        referenceStartDate: todayLocalIso(),
      },
    });
  }

  closeForm(): void {
    if (this.isSubmitting()) {
      return;
    }
    this.formOpen.set(false);
    this.serverError.set(null);
  }

  // ─── Submit (AC-1, AC-5) ──────────────────────────────────
  submit(): void {
    this.serverError.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.toastr.warning('Please fix the highlighted fields.');
      return;
    }

    const request = this.buildRequest();
    this.isSubmitting.set(true);
    const id = this.editingId();
    const op$ = id
      ? this.attendanceService.updateShift(id, request)
      : this.attendanceService.createShift(request);

    op$.pipe(takeUntil(this.destroy$)).subscribe({
      next: (saved) => this.onSaved(saved, !!id),
      error: (err: HttpErrorResponse) => this.onSubmitError(err),
    });
  }

  /** Build the ShiftRequest, omitting fields the type does not use (§7, BR-8). */
  private buildRequest(): IShiftRequest {
    const raw = this.form.getRawValue();
    const type = raw.type as ShiftType;
    const request: IShiftRequest = {
      name: (raw.name as string).trim(),
      type,
      breakDurationMinutes: Number(raw.breakDurationMinutes) || 0,
      gracePeriodMinutes: Number(raw.gracePeriodMinutes) || 0,
      workingDays: type === 'FLEXIBLE' ? [] : [...(raw.workingDays as number[])],
    };

    if (shiftTypeUsesTimes(type)) {
      request.startTime = raw.startTime || undefined;
      request.endTime = raw.endTime || undefined;
    }
    if (type === 'FLEXIBLE') {
      request.minimumHours = Number(raw.minimumHours);
    }
    if (type === 'ROTATING') {
      const steps: IRotationStep[] = (raw.rotation.steps as { shiftId: string; durationDays: number }[]).map(
        (s, i) => ({ order: i + 1, shiftId: s.shiftId, durationDays: Number(s.durationDays) }),
      );
      const rotation: IRotation = {
        cycleLengthDays: Number(raw.rotation.cycleLengthDays) || steps.reduce((a, s) => a + s.durationDays, 0),
        referenceStartDate: raw.rotation.referenceStartDate,
        steps,
      };
      request.rotation = rotation;
    }
    return request;
  }

  private onSaved(saved: IShift, wasEdit: boolean): void {
    this.isSubmitting.set(false);
    if (wasEdit) {
      this.shifts.set(this.shifts().map((s) => (s.id === saved.id ? saved : s)));
      this.toastr.success(`"${saved.name}" updated.`, 'Shift saved');
    } else {
      this.shifts.set([saved, ...this.shifts()]);
      this.toastr.success(`"${saved.name}" created.`, 'Shift saved');
    }
    this.formOpen.set(false);
  }

  private onSubmitError(err: HttpErrorResponse): void {
    this.isSubmitting.set(false);
    const parsed = AttendanceService.parseShiftInUseError(err);
    this.serverError.set(parsed?.message ?? 'An unexpected error occurred. Please try again.');
  }

  // ─── Clone (FR-8) ─────────────────────────────────────────
  clone(shift: IShift): void {
    this.attendanceService
      .cloneShift(shift.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (created) => {
          this.shifts.set([created, ...this.shifts()]);
          this.toastr.success(`"${created.name}" created from "${shift.name}".`, 'Shift cloned');
        },
        error: () => this.toastr.error('Failed to clone shift.'),
      });
  }

  // ─── Delete (AC-4, FR-6) ──────────────────────────────────
  confirmDelete(shift: IShift): void {
    this.deleteTarget.set(shift);
    this.deleteError.set(null);
  }

  cancelDelete(): void {
    if (this.isDeleting()) {
      return;
    }
    this.deleteTarget.set(null);
    this.deleteError.set(null);
  }

  executeDelete(): void {
    const target = this.deleteTarget();
    if (!target) {
      return;
    }
    this.isDeleting.set(true);
    this.deleteError.set(null);
    this.attendanceService
      .deleteShift(target.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.isDeleting.set(false);
          this.shifts.set(this.shifts().filter((s) => s.id !== target.id));
          this.deleteTarget.set(null);
          this.toastr.success(`"${target.name}" deleted.`);
        },
        error: (err: HttpErrorResponse) => {
          this.isDeleting.set(false);
          // AC-4: show the in-use message verbatim inside the confirm dialog.
          const parsed = AttendanceService.parseShiftInUseError(err);
          this.deleteError.set(parsed?.message ?? 'Failed to delete this shift.');
        },
      });
  }

  // ─── Assign (AC-2, FR-3, §8) ──────────────────────────────
  openAssign(shift: IShift): void {
    this.assignTarget.set(shift);
    this.assignSearch.set('');
    this.assignResults.set([]);
    this.assignSelected.set([]);
    this.assignEffectiveFrom.set(todayLocalIso());
  }

  closeAssign(): void {
    if (this.isAssigning()) {
      return;
    }
    this.assignTarget.set(null);
  }

  onAssignSearch(value: string): void {
    this.assignSearch.set(value);
    const term = value.trim();
    if (term.length === 0) {
      this.assignResults.set([]);
      return;
    }
    this.assignSearch$.next(term);
  }

  selectEmployee(emp: IEmployee): void {
    if (this.assignSelectedIds().has(emp.employeeId)) {
      return;
    }
    this.assignSelected.set([...this.assignSelected(), emp]);
  }

  deselectEmployee(emp: IEmployee): void {
    this.assignSelected.set(
      this.assignSelected().filter((e) => e.employeeId !== emp.employeeId),
    );
  }

  onEffectiveFromChange(value: string): void {
    this.assignEffectiveFrom.set(value);
  }

  employeeFullName(emp: IEmployee): string {
    return `${emp.firstName} ${emp.lastName}`.trim();
  }

  employeeInitials(emp: IEmployee): string {
    return `${emp.firstName?.[0] ?? ''}${emp.lastName?.[0] ?? ''}`.toUpperCase() || '?';
  }

  executeAssign(): void {
    const target = this.assignTarget();
    if (!target) {
      return;
    }
    const selected = this.assignSelected();
    if (selected.length === 0) {
      this.toastr.warning('Select at least one employee to assign.');
      return;
    }
    const effectiveFrom = this.assignEffectiveFrom();
    if (!effectiveFrom) {
      this.toastr.warning('Choose an effective date.');
      return;
    }

    this.isAssigning.set(true);
    this.attendanceService
      .assignShift(target.id, {
        employeeIds: selected.map((e) => e.employeeId),
        effectiveFrom,
      })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => this.onAssigned(target, result.assignedCount),
        error: (err: HttpErrorResponse) => this.onAssignError(err),
      });
  }

  private onAssigned(target: IShift, assignedCount: number): void {
    this.isAssigning.set(false);
    // Optimistically bump the assigned count on the affected row.
    this.shifts.set(
      this.shifts().map((s) =>
        s.id === target.id
          ? { ...s, assignedEmployeeCount: s.assignedEmployeeCount + assignedCount }
          : s,
      ),
    );
    this.assignTarget.set(null);
    this.toastr.success(
      `Assigned "${target.name}" to ${assignedCount} employee${assignedCount === 1 ? '' : 's'}.`,
      'Shift assigned',
    );
  }

  private onAssignError(err: HttpErrorResponse): void {
    this.isAssigning.set(false);
    const parsed = AttendanceService.parseShiftInUseError(err);
    this.toastr.error(parsed?.message ?? 'Failed to assign the shift. Please try again.');
  }
}
